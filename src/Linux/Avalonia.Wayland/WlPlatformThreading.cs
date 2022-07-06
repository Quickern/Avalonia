using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.FreeDesktop;
using Avalonia.Platform;
using Avalonia.Threading;
using NWayland.Interop;

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

        public unsafe void RunLoop(CancellationToken cancellationToken)
        {
            var pollFd = new pollfd
            {
                fd = _displayFd,
                events = (int)(EpollEvents.EPOLLIN | EpollEvents.EPOLLHUP)
            };

            while (!cancellationToken.IsCancellationRequested)
            {
                while (_platform.WlDisplay.PrepareRead() != 0)
                    _platform.WlDisplay.DispatchPending();

                if (cancellationToken.IsCancellationRequested)
                {
                    _platform.WlDisplay.CancelRead();
                    break;
                }

                if (!FlushDisplay())
                {
                    _platform.WlDisplay.CancelRead();
                    throw new NWaylandException($"Failed to flush display. Errno: {Marshal.GetLastWin32Error()}");
                }

                var nextTick = DispatchTimers(cancellationToken);
                var timeout = nextTick == TimeSpan.MinValue ? -1 : Math.Max(-1, (int)(nextTick - _clock.Elapsed).TotalMilliseconds);
                var ret = LibC.poll(&pollFd, new IntPtr(1), timeout);

                if (cancellationToken.IsCancellationRequested)
                {
                    _platform.WlDisplay.CancelRead();
                    break;
                }

                if (ret < 0)
                {
                    _platform.WlDisplay.CancelRead();
                    throw new NWaylandException($"Failed to poll display. Errno: {Marshal.GetLastWin32Error()}");
                }

                if ((pollFd.revents & (int)EpollEvents.EPOLLIN) > 0)
                {
                    _platform.WlDisplay.ReadEvents();
                    _platform.WlDisplay.DispatchPending();
                }
                else
                {
                    _platform.WlDisplay.CancelRead();
                }

                Dispatcher.UIThread.RunJobs();
            }

            _platform.Dispose();
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

        private unsafe bool FlushDisplay()
        {
            while (_platform.WlDisplay.Flush() == -1)
            {
                if (Marshal.GetLastWin32Error() != (int)Errno.EAGAIN)
                    return false;

                var pollFd = new pollfd
                {
                    fd = _displayFd,
                    events = (int)EpollEvents.EPOLLOUT
                };

                while (LibC.poll(&pollFd, new IntPtr(1), -1) == -1)
                {
                    if (Marshal.GetLastWin32Error() is not (int)Errno.EINTR and not (int)Errno.EAGAIN)
                        return false;
                }
            }

            return true;
        }
    }
}
