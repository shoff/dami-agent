namespace Dami.Transport.Tests;

internal sealed class TestTimeProvider : TimeProvider
{
    private readonly Lock sync = new();
    private readonly List<TestTimer> timers = [];
    private long timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow()
    {
        lock (this.sync)
        {
            return DateTimeOffset.UnixEpoch.AddTicks(this.timestamp);
        }
    }

    public override long GetTimestamp()
    {
        lock (this.sync)
        {
            return this.timestamp;
        }
    }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new TestTimer(this, callback, state);
        lock (this.sync)
        {
            this.timers.Add(timer);
            timer.ChangeCore(dueTime, period, this.timestamp);
        }

        return timer;
    }

    public void Advance(TimeSpan amount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(amount, TimeSpan.Zero);
        long target;
        lock (this.sync)
        {
            target = checked(this.timestamp + amount.Ticks);
        }

        this.FireTimersThrough(target);
    }

    private void FireTimersThrough(long target)
    {
        while (this.TryCaptureNextCallbacks(target, out List<Action>? callbacks))
        {
            foreach (Action callback in callbacks)
            {
                callback();
            }
        }
    }

    private bool TryCaptureNextCallbacks(
        long target,
        out List<Action> callbacks)
    {
        lock (this.sync)
        {
            long? next = this.timers
                .Where(timer => timer.DueTimestamp is not null && timer.DueTimestamp <= target)
                .Min(timer => timer.DueTimestamp);
            if (next is null)
            {
                this.timestamp = target;
                callbacks = [];
                return false;
            }

            this.timestamp = next.Value;
            callbacks = this.timers
                .Where(timer => timer.DueTimestamp == next)
                .Select(timer => timer.CaptureCallback())
                .ToList();
            return true;
        }
    }

    private void Change(
        TestTimer timer,
        TimeSpan dueTime,
        TimeSpan period)
    {
        lock (this.sync)
        {
            timer.ChangeCore(dueTime, period, this.timestamp);
        }
    }

    private void Remove(TestTimer timer)
    {
        lock (this.sync)
        {
            this.timers.Remove(timer);
        }
    }

    private sealed class TestTimer : ITimer
    {
        private readonly TimerCallback callback;
        private readonly TestTimeProvider owner;
        private readonly object? state;
        private long periodTicks = Timeout.InfiniteTimeSpan.Ticks;

        public TestTimer(
            TestTimeProvider owner,
            TimerCallback callback,
            object? state)
        {
            this.owner = owner;
            this.callback = callback;
            this.state = state;
        }

        public long? DueTimestamp { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            this.owner.Change(this, dueTime, period);
            return true;
        }

        public void Dispose()
        {
            this.DueTimestamp = null;
            this.owner.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            this.Dispose();
            return ValueTask.CompletedTask;
        }

        public void ChangeCore(
            TimeSpan dueTime,
            TimeSpan period,
            long currentTimestamp)
        {
            ValidateTimeout(dueTime, nameof(dueTime));
            ValidateTimeout(period, nameof(period));
            this.periodTicks = period.Ticks;
            this.DueTimestamp = dueTime == Timeout.InfiniteTimeSpan
                ? null
                : checked(currentTimestamp + dueTime.Ticks);
        }

        public Action CaptureCallback()
        {
            this.DueTimestamp = this.periodTicks == Timeout.InfiniteTimeSpan.Ticks
                ? null
                : checked(this.DueTimestamp!.Value + this.periodTicks);
            return () => this.callback(this.state);
        }

        private static void ValidateTimeout(TimeSpan value, string parameterName)
        {
            if (value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }
}
