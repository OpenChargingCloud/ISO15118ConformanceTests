using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso2;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using Vanaheimr.V2G.Simulation.Timing;

namespace Vanaheimr.V2G.Simulation.Tests.Interop;

/// <summary>
/// Our two state machines, selected by protocol and power mode, on a stream somebody else is at the other
/// end of.
/// </summary>
/// <remarks>
/// Shared by every counterparty fixture. A per-counterparty copy would be four copies of the same switch,
/// and the interesting difference between the fixtures is who is on the wire, not how we drive our own
/// side of it.
/// <para>
/// Real delays throughout (<see cref="TaskAsyncDelay"/>), unlike the loopback tests: a peer's timeouts are
/// real, and a poll loop that runs as fast as the CPU allows is a different session from the one the
/// specification describes.
/// </para>
/// </remarks>
internal static class InteropSession
{

    /// <summary>Generous on purpose: a live peer under a debugger, or a container's first message after a
    /// cold start, is slower than anything a loopback ever sees.</summary>
    public static readonly TimeSpan PerMessageTimeout = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan SequenceTimeout   = TimeSpan.FromSeconds(60);


    /// <returns>How many messages our car exchanged with their station.</returns>
    public static async Task<Int32> RunEvccAsync(Stream stream, ProtocolVariant protocol, PowerMode mode,
                                                 CancellationToken ct)
    {

        if (protocol == ProtocolVariant.Iso15118_2)
        {
            var evcc = new Evcc2(stream, mode, TimeProvider.System, new TaskAsyncDelay(), PerMessageTimeout);
            await evcc.RunAsync(ct);
            return evcc.Exchanges;
        }

        Evcc20Base evcc20 = mode == PowerMode.Dc
                                ? new Evcc20Dc(stream, TimeProvider.System, new TaskAsyncDelay(), PerMessageTimeout)
                                : new Evcc20Ac(stream, TimeProvider.System, new TaskAsyncDelay(), PerMessageTimeout);
        await evcc20.RunAsync(ct);
        return evcc20.Exchanges;

    }


    /// <returns>Whether our station reached the terminal session state.</returns>
    public static async Task<Boolean> RunSeccAsync(Stream stream, ProtocolVariant protocol, PowerMode mode,
                                                   CancellationToken ct)
    {

        if (protocol == ProtocolVariant.Iso15118_2)
        {
            var secc = new Secc2(mode, SequenceTimeout, TimeProvider.System);
            await secc.RunAsync(stream, ct);
            return secc.IsDone;
        }

        Secc20Base secc20 = mode == PowerMode.Dc
                                ? new Secc20Dc(SequenceTimeout, TimeProvider.System)
                                : new Secc20Ac(SequenceTimeout, TimeProvider.System);
        await secc20.RunAsync(stream, ct);
        return secc20.IsDone;

    }

}
