namespace LongPizzaOrderBuilder.UnitTests;

/// <summary>
/// A clock that can freeze one caller part-way through a store operation, so a test can make a race happen on purpose
/// instead of hoping for lucky timing.
///
/// The store reads the clock inside its critical section, after it has checked an order's status (or an idempotency key)
/// and before it writes the result. Pausing there leaves one request half-done; the test then sends the competing request
/// and observes whether the store lets it in. With a correct lock the second request must wait. Without one it would
/// slip through and both would succeed.
/// </summary>
internal sealed class PausingClock : TimeProvider
{
    private int _armed;
    private long _seconds;

    /// <summary>Set when a caller has reached the pause point and is frozen there.</summary>
    public ManualResetEventSlim Paused { get; } = new();

    /// <summary>Set by the test to let the frozen caller continue.</summary>
    public ManualResetEventSlim Resume { get; } = new();

    /// <summary>The next call to read the clock freezes until <see cref="Resume"/> is set.</summary>
    public void PauseNextCall()
    {
        Paused.Reset();
        Resume.Reset();
        Volatile.Write(ref _armed, 1);
    }

    public override DateTimeOffset GetUtcNow()
    {
        if (Interlocked.Exchange(ref _armed, 0) == 1)
        {
            Paused.Set();
            if (!Resume.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("The test never resumed the paused caller.");
        }
        return new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero).AddSeconds(Interlocked.Increment(ref _seconds));
    }
}

/// <summary>
/// Starts work on its own thread. A paused request holds its thread, and the thread pool is slow to add more, so on a small machine
/// the competing request might not even start within the test's wait. A dedicated thread makes "it had not finished after 300 ms" mean
/// "it was blocked", which is what these tests need to show.
/// </summary>
internal static class DedicatedThread
{
    public static Task<T> Run<T>(Func<T> work) => Task.Factory.StartNew(work, TaskCreationOptions.LongRunning);

    /// <summary>True if the task finishes within <paramref name="time"/>. Awaits instead of blocking, so tests stay properly asynchronous.</summary>
    public static async Task<bool> FinishesWithin(Task task, TimeSpan time) => await Task.WhenAny(task, Task.Delay(time)) == task;
}

/// <summary>A clock that moves forward one second every time it is read, so times are distinct and predictable.</summary>
internal sealed class TickingClock : TimeProvider
{
    private long _seconds;
    public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero).AddSeconds(Interlocked.Increment(ref _seconds));
}

/// <summary>A clock the test moves by hand.</summary>
internal sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}
