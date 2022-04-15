using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Diagnostics;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.Wayland;


namespace Avalonia.Wayland
{
    unsafe class WaylandPlatformThreading : IPlatformThreadingInterface
    {
        private readonly WaylandPlatform _platform;
        private Thread _mainThread;

        [StructLayout(LayoutKind.Explicit)]
        struct epoll_data
        {
            [FieldOffset(0)]
            public IntPtr ptr;
            [FieldOffset(0)]
            public int fd;
            [FieldOffset(0)]
            public uint u32;
            [FieldOffset(0)]
            public ulong u64;
        }

        private const int EPOLLIN = 1;
        private const int EPOLL_CTL_ADD = 1;
        private const int O_NONBLOCK = 2048;

        [StructLayout(LayoutKind.Sequential)]
        struct epoll_event
        {
            public uint events;
            public epoll_data data;
        }

        [DllImport("libc")]
        extern static int epoll_create1(int size);

        [DllImport("libc")]
        extern static int epoll_ctl(int epfd, int op, int fd, ref epoll_event __event);

        [DllImport("libc")]
        extern static int epoll_wait(int epfd, epoll_event* events, int maxevents, int timeout);

        [DllImport("libc")]
        extern static int pipe2(int* fds, int flags);
        [DllImport("libc")]
        extern static IntPtr write(int fd, void* buf, IntPtr count);

        [DllImport("libc")]
        extern static IntPtr read(int fd, void* buf, IntPtr count);

        private int _sigread, _sigwrite;
        private object _lock = new object();
        private bool _signaled;
        private DispatcherPriority _signaledPriority;
        private int _epoll;
        private Stopwatch _clock = Stopwatch.StartNew();

        enum EventCodes
        {
            Wayland = 1,
            Signal = 2,
        }

        class WaylandTimer : IDisposable
        {
            private readonly WaylandPlatformThreading _parent;

            public WaylandTimer(WaylandPlatformThreading parent, DispatcherPriority prio, TimeSpan interval, Action tick)
            {
                _parent = parent;
                Priority = prio;
                Tick = tick;
                Interval = interval;
                Reschedule();
            }

            public DispatcherPriority Priority { get; }
            public TimeSpan NextTick { get; private set; }
            public TimeSpan Interval { get; }
            public Action Tick { get; }
            public bool Disposed { get; private set; }

            public void Reschedule()
            {
                NextTick = _parent._clock.Elapsed + Interval;
            }

            public void Dispose()
            {
                Disposed = true;
                lock (_parent._lock)
                    _parent._timers.Remove(this);
            }
        }

        List<WaylandTimer> _timers = new List<WaylandTimer>();


        public WaylandPlatformThreading(WaylandPlatform platform)
        {
            _platform = platform;
            _mainThread = Thread.CurrentThread;

            _epoll = epoll_create1(0);
            if (_epoll == -1)
                throw new Exception("Ooops EPOLL returned error");

            var fds = stackalloc int[2];
            pipe2(fds, O_NONBLOCK);
            _sigread = fds[0];
            _sigwrite = fds[1];

            Console.WriteLine($"sigread: {_sigread} sigwrite: {_sigwrite}");

            var ev = new epoll_event
            {
                events = EPOLLIN,
                data = { u32 = (int)EventCodes.Signal }
            };

            if(epoll_ctl(_epoll, EPOLL_CTL_ADD, _sigread, ref ev) == -1)
                throw new Exception("Unable to attach signal to pipe to epoll");
        }

        int TimerComparer(WaylandTimer t1, WaylandTimer t2)
        {
            return t2.Priority - t1.Priority;
        }

        void CheckSignaled()
        {
            int buf = 0;
            IntPtr ret = read(_sigread, &buf, new IntPtr(4));

            Console.WriteLine($"Read from pipt: {ret.ToInt64()}");

            while (read(_sigread, &buf, new IntPtr(4)).ToInt64() > 0)
            {
            }

            DispatcherPriority prio;
            lock (_lock)
            {
                if (!_signaled)
                    return;
                _signaled = false;
                prio = _signaledPriority;
                _signaledPriority = DispatcherPriority.MinValue;
            }

            Signaled?.Invoke(prio);
        }

        public bool CurrentThreadIsLoopThread => Thread.CurrentThread == _mainThread;

        public event Action<DispatcherPriority?> Signaled;

        public void RunLoop(CancellationToken cancellationToken)
        {
            var readyTimers = new List<WaylandTimer>();
            while (!cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine("TICK!!");
                var now = _clock.Elapsed;
                TimeSpan? nextTick = null;
                readyTimers.Clear();
                lock (_timers)
                    foreach (var t in _timers)
                    {
                        if (nextTick == null || t.NextTick < nextTick.Value)
                            nextTick = t.NextTick;
                        if (t.NextTick < now)
                            readyTimers.Add(t);
                    }

                readyTimers.Sort(TimerComparer);

                foreach (var t in readyTimers)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;
                    t.Tick();
                    if (!t.Disposed)
                    {
                        t.Reschedule();
                        if (nextTick == null || t.NextTick < nextTick.Value)
                            nextTick = t.NextTick;
                    }
                }

                _platform.Display.Roundtrip();

                epoll_event ev;
                epoll_wait(_epoll, &ev, 1, nextTick == null ? -1 : Math.Max(1, (int)(nextTick.Value-_clock.Elapsed).TotalMilliseconds));

                if (cancellationToken.IsCancellationRequested)
                    return;

                Dispatcher.UIThread.RunJobs();

                CheckSignaled();
            }
        }

        public void Signal(DispatcherPriority priority)
        {
            Console.WriteLine("Signal has been called");
            lock (_lock)
            {
                if (priority > _signaledPriority)
                    _signaledPriority = priority;

                if (_signaled)
                    return;
                _signaled = true;
                int buf = 0;
                write(_sigwrite, &buf, new IntPtr(4));
            }
        }

        public IDisposable StartTimer(DispatcherPriority priority, TimeSpan interval, Action tick)
        {
            if (_mainThread != Thread.CurrentThread)
                throw new InvalidOperationException("StartTimer can be only called from UI thread");
            if (interval <= TimeSpan.Zero)
                throw new ArgumentException("Interval must be positive", nameof(interval));

            // We assume that we are on the main thread and outside of epoll_wait, so there is no need for wakeup signal

            var timer = new WaylandTimer(this, priority, interval, tick);
            lock (_timers)
                _timers.Add(timer);
            return timer;

        }
    }

}
