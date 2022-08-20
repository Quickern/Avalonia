using System;
using System.Diagnostics;
using Avalonia.Threading;

namespace Avalonia.FreeDesktop
{
    internal class ManagedThreadingTimer : IComparable<ManagedThreadingTimer>
    {
        private readonly Stopwatch _clock;

        public ManagedThreadingTimer(Stopwatch clock, DispatcherPriority priority, TimeSpan interval, Action tick)
        {
            _clock = clock;
            Priority = priority;
            Interval = interval;
            Tick = tick;
            Reschedule();
        }

        public DispatcherPriority Priority { get; }

        public TimeSpan NextTick { get; private set; }

        public TimeSpan Interval { get; }

        public Action Tick { get; }

        public bool Disposed { get; internal set; }

        public void Reschedule() => NextTick = _clock.Elapsed + Interval;

        public int CompareTo(ManagedThreadingTimer? other) => other is not null ? Priority - other.Priority : 1;
    }
}
