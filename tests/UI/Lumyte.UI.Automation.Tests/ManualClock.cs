namespace Lumyte.UI.Automation.Tests;

internal sealed class ManualClock : TimeProvider
{
    private readonly List<Timer> _timers = [];
    private long _timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _timestamp;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new Timer(this, callback, state);
        timer.Change(dueTime, period);
        _timers.Add(timer);
        return timer;
    }

    internal void Advance(TimeSpan elapsed)
    {
        _timestamp += elapsed.Ticks;
        foreach (Timer timer in _timers.ToArray())
        {
            timer.Fire();
        }
    }

    private sealed class Timer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        private long _due;
        private long _period;
        private bool _disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : clock._timestamp + dueTime.Ticks;
            _period = period.Ticks;
            return !_disposed;
        }

        public void Dispose() => _disposed = true;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        internal void Fire()
        {
            if (!_disposed && clock._timestamp >= _due)
            {
                _due = _period > 0 ? clock._timestamp + _period : long.MaxValue;
                callback(state);
            }
        }
    }
}
