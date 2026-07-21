using cloud.charging.open.protocols.ISO15118.SLAC.Avln;

namespace Vanaheimr.V2G.Simulation.Slac
{
    /// <summary>
    /// Programs the local PLC chip with the SLAC-negotiated credentials and waits until the AVLN is up —
    /// the step after a match completes. In simulation a <c>SimulatedChipController</c> records the key and
    /// reports the AVLN ready immediately (optionally after a configurable delay).
    /// </summary>
    internal static class SlacChip
    {
        internal static readonly TimeSpan DefaultAvlnReadyTimeout = TimeSpan.FromSeconds(5);

        internal static async Task ProgramAsync(IPlcChipController? chip,
                                                SlacResult          result,
                                                TimeSpan            avlnReadyTimeout,
                                                CancellationToken   ct)
        {
            if (chip is null)
                return;

            await chip.SetKeyAsync(result.Nid, result.Nmk, ct).ConfigureAwait(false);
            await chip.WaitForAvlnReadyAsync(avlnReadyTimeout, ct).ConfigureAwait(false);
        }
    }
}
