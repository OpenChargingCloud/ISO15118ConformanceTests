namespace Vanaheimr.V2G.Simulation.Timing;

/// <summary>The production <see cref="IAsyncDelay"/> — a real, wall-clock <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</summary>
public sealed class TaskAsyncDelay : IAsyncDelay
{
    public Task Wait(TimeSpan duration, CancellationToken ct = default) => Task.Delay(duration, ct);
}
