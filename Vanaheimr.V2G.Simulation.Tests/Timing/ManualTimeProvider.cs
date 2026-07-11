namespace Vanaheimr.V2G.Simulation.Tests.Timing;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock only moves when the test calls <see cref="Advance"/> —
/// for asserting sequence-timeout behaviour without a real wall-clock wait. Deliberately does not
/// override <c>CreateTimer</c>: nothing in this project schedules a fired callback, every timeout
/// check is a pull-based "has too much time passed since I last saw you" comparison on the next
/// incoming message, so the base class's (non-functional, in a fake) timer plumbing is never exercised.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public ManualTimeProvider(DateTimeOffset? start = null) => _utcNow = start ?? DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan by) => _utcNow += by;
}
