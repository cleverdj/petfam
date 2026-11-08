using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace PetFam.Desktop;

/// <summary>
/// Ticks the app's frames at an even rate. A UI timer on Windows fires in 15.6 ms steps, so asking for 33 ms gives
/// 31 and 47 ms frames in turn and movement judders; this clock runs on its own thread with 1 ms timer resolution,
/// keeps a fixed schedule (no drift), and posts each frame to the UI thread. A frame is never queued behind one
/// that has not run yet, so a slow frame cannot cause a burst of catch-up frames.
/// </summary>
public sealed class FrameClock : IDisposable
{
    private readonly Action _tick;
    private readonly Thread _thread;
    private volatile bool _running = true;
    private volatile bool _pending;
    private long _intervalTicks;
    private readonly AutoResetEvent _wake = new(false);

    public FrameClock(Action tick, TimeSpan interval)
    {
        _tick = tick;
        Interval = interval;
        if (OperatingSystem.IsWindows()) TimeBeginPeriod(1);
        _thread = new Thread(Run) { IsBackground = true, Name = "PetFam frame clock", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>Time between frames. Changing it takes effect at once (a slower clock is woken up).</summary>
    public TimeSpan Interval
    {
        get => TimeSpan.FromTicks(Interlocked.Read(ref _intervalTicks) * TimeSpan.TicksPerSecond / Stopwatch.Frequency);
        set
        {
            var ticks = (long)(value.TotalSeconds * Stopwatch.Frequency);
            if (Interlocked.Exchange(ref _intervalTicks, ticks) != ticks) _wake.Set();
        }
    }

    private void Run()
    {
        var next = Stopwatch.GetTimestamp() + Interlocked.Read(ref _intervalTicks);
        while (_running)
        {
            var interval = Interlocked.Read(ref _intervalTicks);
            var now = Stopwatch.GetTimestamp();
            if (next - now > interval) next = now + interval;             // the interval got shorter
            var waitMs = (next - now) * 1000.0 / Stopwatch.Frequency;
            // With 1 ms timer resolution this lands within about a millisecond of the schedule.
            if (waitMs >= 1 && _wake.WaitOne((int)waitMs))
                continue;                                                // interval changed: re-plan

            if (!_pending)
            {
                _pending = true;
                Dispatcher.UIThread.Post(() =>
                {
                    try { _tick(); }
                    finally { _pending = false; }
                }, DispatcherPriority.Render);
            }

            next += interval;
            // Fell far behind (the machine was asleep, a long stall): start a fresh schedule instead of catching up.
            if (Stopwatch.GetTimestamp() - next > interval * 3) next = Stopwatch.GetTimestamp() + interval;
        }
    }

    public void Dispose()
    {
        _running = false;
        _wake.Set();
        if (OperatingSystem.IsWindows()) TimeEndPeriod(1);
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint ms);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint ms);
}
