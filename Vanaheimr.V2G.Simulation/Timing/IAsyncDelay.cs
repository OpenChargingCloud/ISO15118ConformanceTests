namespace Vanaheimr.V2G.Simulation.Timing
{
    /// <summary>
    /// The EVCC-side poll-loop backoff (e.g. while waiting for <c>EVSEProcessing.Finished</c> during
    /// Authorization/ChargeParameterDiscovery/ChargeLoop) goes through this seam instead of a hardcoded
    /// <c>Task.Delay</c>/<c>Thread.Sleep</c>, so tests can make polling loops run instantly instead of
    /// waiting on the real wall clock. See <see cref="System.TimeProvider"/> (constructor-injected
    /// directly, no wrapper needed) for the separate concern of elapsed-time/timeout checks.
    /// </summary>
    public interface IAsyncDelay
    {
        Task Wait(TimeSpan duration, CancellationToken ct = default);
    }
}
