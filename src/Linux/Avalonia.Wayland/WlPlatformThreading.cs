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
        private readonly object _lock;
        private readonly int _displayFd;
        private readonly int _sigRead;
        private readonly int _sigWrite;

        private bool _signaled;
        private DispatcherPriority _signaledPriority;

        public unsafe WlPlatformThreading(AvaloniaWaylandPlatform platform)
        {
            _platform = platform;
            _mainThread = Thread.CurrentThread;
            _clock = Stopwatch.StartNew();
            _timers = new List<ManagedThreadingTimer>();
            _readyTimers = new List<ManagedThreadingTimer>();
            _lock = new object();
            _displayFd = platform.WlDisplay.GetFd();
            var fds = stackalloc int[2];
            LibC.pipe2(fds, FileDescriptorFlags.O_NONBLOCK);
            _sigRead = fds[0];
            _sigWrite = fds[1];
        }

        public event Action<DispatcherPriority?>? Signaled;

        public bool CurrentThreadIsLoopThread => Thread.CurrentThread == _mainThread;

        public void RunLoop(CancellationToken cancellationToken)
        {
            using (_platform)
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var nextTick = DispatchTimers();
                    var timeout = nextTick == TimeSpan.MinValue ? -1 : Math.Max(1, (int)(nextTick - _clock.Elapsed).TotalMilliseconds);
                    if (DispatchDisplay(timeout) == -1)
                        break;
                    Dispatcher.UIThread.RunJobs();
                }
            }
        }

        public IDisposable StartTimer(DispatcherPriority priority, TimeSpan interval, Action tick)
        {
            if (interval <= TimeSpan.Zero)
                throw new ArgumentException("Interval must be positive", nameof(interval));

            var timer = new ManagedThreadingTimer(_clock, priority, interval, tick);
            _timers.Add(timer);
            return Disposable.Create(timer, t =>
            {
                t.Disposed = true;
                _timers.Remove(t);
            });
        }

        public unsafe void Signal(DispatcherPriority priority)
        {
            lock (_lock)
            {
                if (priority > _signaledPriority)
                    _signaledPriority = priority;
                if (_signaled)
                    return;
                _signaled = true;
                var buf = 0;
                LibC.write(_sigWrite, new IntPtr(&buf), 1);
            }
        }

        private TimeSpan DispatchTimers()
        {
            _readyTimers.Clear();
            var now = _clock.Elapsed;
            var nextTick = TimeSpan.MaxValue;
            foreach (var timer in _timers)
            {
                if (timer.NextTick < nextTick)
                    nextTick = timer.NextTick;
                if (timer.NextTick < now)
                    _readyTimers.Add(timer);
            }

            foreach (var t in _readyTimers)
            {
                t.Tick.Invoke();
                if (t.Disposed)
                    continue;
                t.Reschedule();
                if (t.NextTick < nextTick)
                    nextTick = t.NextTick;
            }

            return nextTick;
        }

        private int DispatchDisplay(int timeout)
        {
            if (_platform.WlDisplay.PrepareRead() == -1)
                return _platform.WlDisplay.DispatchPending();

            var ret = FlushDisplay();
            if (ret < 0 && Marshal.GetLastWin32Error() == (int)Errno.EPIPE)
            {
                _platform.WlDisplay.CancelRead();
                return -1;
            }

            ret = PollDisplay(EpollEvents.EPOLLIN, timeout);
            if (ret <= 0)
            {
                _platform.WlDisplay.CancelRead();
                return ret;
            }

            if (_platform.WlDisplay.ReadEvents() == -1)
            {
                _platform.WlDisplay.CancelRead();
                return -1;
            }

            return _platform.WlDisplay.DispatchPending();
        }

        private int FlushDisplay()
        {
            int ret;
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

            return ret;
        }

        private unsafe int PollDisplay(EpollEvents events, int timeout)
        {
            int ret;
            var pollFds = stackalloc pollfd[]
            {
                new pollfd { fd = _displayFd, events = (short)events },
                new pollfd { fd = _sigRead, events = (short)EpollEvents.EPOLLIN }
            };

            do
                ret = LibC.poll(pollFds, 2, timeout);
            while (ret == -1 && Marshal.GetLastWin32Error() == (int)Errno.EINTR);
            return CheckSignaled() ? 0 : ret;
        }

        private unsafe bool CheckSignaled()
        {
            var buffer = 0;
            while (LibC.read(_sigRead, new IntPtr(&buffer), 4) > 0) { }
            DispatcherPriority priority;
            lock (_lock)
            {
                if (!_signaled)
                    return false;
                _signaled = false;
                priority = _signaledPriority;
                _signaledPriority = DispatcherPriority.MinValue;
            }

            Signaled?.Invoke(priority);
            return true;
        }
    }
}
