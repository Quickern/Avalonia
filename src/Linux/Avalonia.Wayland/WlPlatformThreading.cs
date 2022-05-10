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
    public class WlPlatformThreading : IPlatformThreadingInterface
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

        public void RunLoop(CancellationToken cancellationToken)
        {
            var clock = Stopwatch.StartNew();
            var readyTimers = new List<ManagedThreadingTimer>();
            while (!cancellationToken.IsCancellationRequested && _platform.WlDisplay.Dispatch() >= 0)
            {
                var now = clock.Elapsed;
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
            return Disposable.Create(() => _timers.Remove(timer));
        }

        public void Signal(DispatcherPriority priority) { }

        public bool CurrentThreadIsLoopThread => Thread.CurrentThread == _mainThread;

        public event Action<DispatcherPriority?>? Signaled;
    }
}
