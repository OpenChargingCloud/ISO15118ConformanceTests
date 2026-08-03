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


    /// <summary>How a session actually ran: the exchange count, and the authorization mode it really
    /// used. The second field exists because a Plug &amp; Charge run that quietly falls back to EIM —
    /// because the station did not offer Contract, or sent no challenge — completes just as happily and
    /// would otherwise be reported as a PnC result.</summary>
    public sealed record EvccOutcome(Int32 Exchanges, String AuthorizationMode, Int32 MeteringReceiptsSent);


    /// <param name="preferDynamic">-20 only: drive the session in Dynamic control mode (ControlMode = 2)
    /// rather than Scheduled — the EV states energy needs and a departure time and lets the station steer.
    /// Ignored for -2, which has no control modes. Set by <c>V2G_INTEROP_DYNAMIC=1</c>.</param>
    /// <param name="pnc">Contract credentials; when set <i>and</i> the station offers Contract/PnC, the
    /// session authorizes with a signed AuthorizationReq instead of EIM. Set by
    /// <c>V2G_INTEROP_CONTRACT_CERT</c>.</param>
    public static async Task<EvccOutcome> RunEvccAsync(Stream stream, ProtocolVariant protocol, PowerMode mode,
                                                       CancellationToken ct, Boolean preferDynamic = false,
                                                       PncEvccOptions? pnc = null)
    {

        if (protocol == ProtocolVariant.Iso15118_2)
        {
            var evcc = new Evcc2(stream, mode, TimeProvider.System, new TaskAsyncDelay(), PerMessageTimeout)
                           { Pnc = pnc };
            await evcc.RunAsync(ct);
            return new EvccOutcome(evcc.Exchanges, evcc.AuthorizationMode, evcc.MeteringReceiptsSent);
        }

        Evcc20Base evcc20 = mode == PowerMode.Dc
                                ? new Evcc20Dc(stream, TimeProvider.System, new TaskAsyncDelay(), PerMessageTimeout)
                                : new Evcc20Ac(stream, TimeProvider.System, new TaskAsyncDelay(), PerMessageTimeout);

        evcc20.PreferDynamicControlMode = preferDynamic;
        evcc20.Pnc                      = pnc;

        await evcc20.RunAsync(ct);
        return new EvccOutcome(evcc20.Exchanges, evcc20.AuthorizationMode, MeteringReceiptsSent: 0);

    }


    /// <param name="offerPlugAndCharge">-20 only: advertise Plug &amp; Charge alongside EIM. False narrows the
    /// offer to EIM, for an EV that cannot ignore a service it does not support.</param>
    /// <param name="preferDynamic">-20 only: offer the Dynamic (ControlMode 2) parameter set first. An EV
    /// that takes the first offered set then runs a Dynamic session — which is the mode eVDriveFlow works
    /// in, and the one that drives schedule renegotiation. Ignored for -2, which has no control modes.</param>
    /// <returns>Whether our station reached the terminal session state.</returns>
    public static async Task<Boolean> RunSeccAsync(Stream stream, ProtocolVariant protocol, PowerMode mode,
                                                   CancellationToken ct, Boolean preferDynamic = false,
                                                   Boolean offerPlugAndCharge = true)
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

        secc20.PreferDynamicControlMode = preferDynamic;
        secc20.OfferPlugAndCharge       = offerPlugAndCharge;

        await secc20.RunAsync(stream, ct);
        return secc20.IsDone;

    }

}
