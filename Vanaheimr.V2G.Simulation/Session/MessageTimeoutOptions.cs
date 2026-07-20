namespace Vanaheimr.V2G.Simulation.Session
{
    /// <summary>
    /// Simplified timeout budgets for a simulated session — not the exact per-message values from the
    /// ISO 15118 performance-timeout tables, just workable defaults for a loopback/interop session.
    /// </summary>
    public sealed record MessageTimeoutOptions(TimeSpan PerMessageTimeout, TimeSpan SequenceTimeout)
    {
        public static MessageTimeoutOptions Default { get; } = new(
            PerMessageTimeout: TimeSpan.FromSeconds(2),
            SequenceTimeout: TimeSpan.FromSeconds(60));
    }
}
