using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.FreeDesktop;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Avalonia.Wayland
{
    internal class WlPlatformThreading : IPlatformThreadingInterface
    {
        private readonly AvaloniaWaylandPlatform _platform;
        private readonly Thread _mainThread;
        private readonly Stopwatch _clock;
        private readonly List<ManagedThreadingTimer> _timers;
        private readonly List<ManagedThreadingTimer> _readyTimers;
        private readonly int _displayFd;

        public WlPlatformThreading(AvaloniaWaylandPlatform platform)
        {
            _platform = platform;
            _mainThread = Thread.CurrentThread;
            _clock = Stopwatch.StartNew();
            _timers = new List<ManagedThreadingTimer>();
            _readyTimers = new List<ManagedThreadingTimer>();
            _displayFd = platform.WlDisplay.GetFd();
        }

        public event Action<DispatcherPriority?>? Signaled;

        public bool CurrentThreadIsLoopThread => Thread.CurrentThread == _mainThread;

        public void RunLoop(CancellationToken cancellationToken)
        {
            using (_platform)
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var nextTick = DispatchTimers(cancellationToken);
                    var timeout = nextTick == TimeSpan.MinValue ? -1 : Math.Max(1, (int)(nextTick - _clock.Elapsed).TotalMilliseconds);
                    if (DispatchDisplay(timeout) == -1)
                        break;
                    Dispatcher.UIThread.RunJobs();
                }
            }
        }

        public IDisposable StartTimer(DispatcherPriority priority, TimeSpan interval, Action tick)
        {
            var timer = new ManagedThreadingTimer(_clock, priority, interval, tick);
            _timers.Add(timer);
            return Disposable.Create(timer, t =>
            {
                t.Disposed = true;
                _timers.Remove(t);
            });
        }

        public void Signal(DispatcherPriority priority) { }

        private TimeSpan DispatchTimers(CancellationToken cancellationToken)
        {
            _readyTimers.Clear();
            var now = _clock.Elapsed;
            var nextTick = TimeSpan.MinValue;
            foreach (var timer in _timers)
            {
                if (nextTick == TimeSpan.MinValue || timer.NextTick < nextTick)
                    nextTick = timer.NextTick;
                if (timer.NextTick < now)
                    _readyTimers.Add(timer);
            }

            foreach (var t in _readyTimers)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                t.Tick.Invoke();
                if (t.Disposed)
                    continue;
                t.Reschedule();
                if (nextTick == TimeSpan.MinValue || t.NextTick < nextTick)
                    nextTick = t.NextTick;
            }

            return nextTick;
        }

        private int DispatchDisplay(int timeout)
        {
            int ret;
            if (_platform.WlDisplay.PrepareRead() == -1)
                return _platform.WlDisplay.DispatchPending();

            while (true)
            {
                ret = _platform.WlDisplay.Flush();
                if (ret != -1 || Marshal.GetLastWin32Error() == (int)Errno.EAGAIN)
                    break;
                if (PollDisplay(EpollEvents.EPOLLOUT, -1) != -1)
                    continue;
                _platform.WlDisplay.CancelRead();
                return -1;
            }

            if (ret < 0 && Marshal.GetLastWin32Error() == (int)Errno.EPIPE)
            {
                _platform.WlDisplay.CancelRead();
                return -1;
            }

            switch (PollDisplay(EpollEvents.EPOLLIN, timeout))
            {
                case 0:
                    _platform.WlDisplay.CancelRead();
                    return 0;
                case -1:
                    _platform.WlDisplay.CancelRead();
                    return -1;
            }

            if (_platform.WlDisplay.ReadEvents() == -1)
            {
                _platform.WlDisplay.CancelRead();
                return -1;
            }

            return _platform.WlDisplay.DispatchPending();
        }

        private unsafe int PollDisplay(EpollEvents events, int timeout)
        {
            int ret;
            var pollFd = new pollfd { fd = _displayFd, events = (short)events };
            do
                ret = LibC.poll(&pollFd, new IntPtr(1), timeout);
            while (ret == -1 && Marshal.GetLastWin32Error() == (int)Errno.EINTR);
            return ret;
        }
    }
}
