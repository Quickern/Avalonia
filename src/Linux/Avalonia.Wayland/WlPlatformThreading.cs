using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive.Disposables;
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

        public WlPlatformThreading(AvaloniaWaylandPlatform platform)
        {
            _platform = platform;
            _mainThread = Thread.CurrentThread;
            _clock = Stopwatch.StartNew();
            _timers = new List<ManagedThreadingTimer>();
        }

        public unsafe void RunLoop(CancellationToken cancellationToken)
        {
            var pollFd = new pollfd
            {
                fd = _platform.WlDisplay.GetFd(),
                events = NativeMethods.EPOLLIN | NativeMethods.EPOLLHUP
            };

            var readyTimers = new List<ManagedThreadingTimer>();
            while (!cancellationToken.IsCancellationRequested)
            {
                while (_platform.WlDisplay.PrepareRead() != 0)
                    _platform.WlDisplay.DispatchPending();
                _platform.WlDisplay.Flush();

                var ret = NativeMethods.poll(&pollFd, new IntPtr(1), -1);
                if (cancellationToken.IsCancellationRequested || ret < 0)
                {
                    _platform.WlDisplay.CancelRead();
                    break;
                }

                if (_platform.WlDisplay.ReadEvents() == -1)
                    break;

                var now = _clock.Elapsed;
                TimeSpan nextTick = new(-1);
                foreach (var timer in _timers)
                {
                    if (nextTick == new TimeSpan(-1) || timer.NextTick < nextTick)
                        nextTick = timer.NextTick;
                    if (timer.NextTick < now)
                        readyTimers.Add(timer);
                }

                readyTimers.Sort();

                foreach (var t in readyTimers)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;
                    t.Tick();
                    t.Reschedule();
                    if (nextTick == new TimeSpan(-1) || t.NextTick < nextTick)
                        nextTick = t.NextTick;
                }

                Dispatcher.UIThread.RunJobs();
            }

            _platform.Dispose();
        }

        public IDisposable StartTimer(DispatcherPriority priority, TimeSpan interval, Action tick)
        {
            var timer = new ManagedThreadingTimer(_clock, priority, interval, tick);
            _timers.Add(timer);
            return Disposable.Create(timer, t => _timers.Remove(t));
        }

        public void Signal(DispatcherPriority priority) { }

        public bool CurrentThreadIsLoopThread => Thread.CurrentThread == _mainThread;

        public event Action<DispatcherPriority?>? Signaled;
    }
}
