using Vanaheimr.V2G.Simulation.Timing;

namespace Vanaheimr.V2G.Simulation.Tests.Timing;

/// <summary>Test double for <see cref="IAsyncDelay"/>: never actually waits, so poll loops in tests finish instantly.</summary>
public sealed class ImmediateAsyncDelay : IAsyncDelay
{
    public Task Wait(TimeSpan duration, CancellationToken ct = default) => Task.CompletedTask;
}
