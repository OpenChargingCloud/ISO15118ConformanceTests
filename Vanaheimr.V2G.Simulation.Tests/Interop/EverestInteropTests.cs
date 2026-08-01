using System.Net;

using NUnit.Framework;

using Vanaheimr.V2G.Simulation.Sap;
using Vanaheimr.V2G.Simulation.Transport;

namespace Vanaheimr.V2G.Simulation.Tests.Interop;

/// <summary>
/// Tier-2 interop against <b>EVerest</b> (<c>everest-core</c>) — the Linux Foundation Energy stack, and
/// the implementation most likely to be on the other end of a real charger.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ExplicitAttribute">[Explicit]</see> and environment-gated. Bring a SIL configuration up per
/// <c>tools/interop-everest/README.md</c>, then:
/// <code>dotnet test --filter TestCategory=Interop</code>
/// </para>
///
/// <para><b>"Works against EVerest" is closer to a market claim than to a test result.</b></para>
/// <para>
/// That is the reason to do this one, and it is a different reason from the others. Josev gives an
/// independent codec (EXIficient), eVDriveFlow gives a second one plus Dynamic -20 (OpenEXI), tux-evse
/// gives a real car's captured route. EVerest gives the thing a charger in the field actually runs.
/// </para>
///
/// <para><b>Which half is new, and which is not.</b></para>
/// <list type="bullet">
///   <item><b>Their station is new.</b> <c>EvseV2G</c> (DIN 70121 and -2, C, cbV2G underneath) and
///         <c>Evse15118D20</c> (-20) are implementations nothing here has met. Since <c>EvseV2G</c> sits
///         on cbV2G — the encoder our vector corpus is generated from — a disagreement is <b>not</b> an
///         EXI disagreement by construction: it is a sequencing, timing or semantics one, which is
///         exactly the class our corpora cannot see.</item>
///   <item><b>Their car is Josev.</b> <c>PyEvJosev</c> is the same implementation family the recorded
///         runs under <c>docs/interop-runs/</c> already used, repackaged as a module. Running it is still
///         worth doing — a different configuration exercises different paths — but a green reverse run
///         is far less news than a green forward one.</item>
/// </list>
/// <para>
/// So the forward direction is the one to spend time on here, and the flow report's <i>station → EV</i>
/// half is where its findings will be. Point <c>V2G_INTEROP_SCENARIO</c> at one of our recorded traces
/// and the comparison says where their charger answered differently from ours.
/// </para>
/// </remarks>
[TestFixture]
[Category("Interop")]
[Explicit("Requires a running EVerest SIL configuration (see tools/interop-everest/README.md); never part of the offline CI run.")]
public class EverestInteropTests
{

    /// <summary>
    /// Our car against their charger — <c>EvseV2G</c> for DIN/-2, <c>Evse15118D20</c> for -20.
    /// </summary>
    /// <remarks>
    /// The direction worth the setup. Their <c>EvseV2G</c> runs an SDP server by default
    /// (<c>enable_sdp_server: true</c>) on the interface named in its <c>device</c> setting, so the
    /// endpoint is normally discovered rather than configured — see the harness scripts.
    /// </remarks>
    [Test]
    public async Task OurEvcc_AgainstTheirEvseV2G_RunsToCompletion()
    {

        var endpoint         = InteropEnvironment.SeccEndpointOrIgnore(
                                   "their SECC's endpoint, as discovered via SDP on EvseV2G's 'device'");
        var (protocol, mode) = InteropEnvironment.ProtocolAndMode();
        var (protocolName, modeName) = InteropEnvironment.ProtocolAndModeNames();

        var recording = InteropRecording.FromEnvironment($"everest-{protocolName}-{modeName}-forward");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(180));

        TestContext.Out.WriteLine($"Connecting to their SECC at {endpoint} ...");

        using var socket = await TcpV2GClient.ConnectAsync(endpoint.ConnectHost, endpoint.Port,
                                                           InteropEnvironment.DevTlsOrNull(), cts.Token);

        var stream = recording?.Tap(socket) ?? socket;

        try
        {
            await SapHandshake.RunEvccSideAsync(stream, protocol, cts.Token);

            var exchanges = await InteropSession.RunEvccAsync(stream, protocol, mode, cts.Token);

            Assert.That(exchanges, Is.GreaterThan(0),
                        "our EVCC exchanged at least one message with their charger");
        }
        finally
        {
            Report(recording?.Save(protocolName, modeName,
                                   "live interop: our EVCC against EVerest's EvseV2G / Evse15118D20",
                                   weAreTheEvcc: true));
        }

    }


    /// <summary>
    /// Their car (<c>PyEvJosev</c>) against our charger.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Their EV module's <c>device</c> is documented as "any local interface that has an ipv6 link-local
    /// and a MAC addr", and it finds a station by SDP on that interface — it is not bound to EVerest's own
    /// charger. That is what makes this direction possible at all, and it answers the question the
    /// counterparty list carried as open.
    /// </para>
    /// <para>
    /// What it does <i>not</i> answer is whether a config containing only the EV-side modules can be
    /// assembled and started; see the harness README.
    /// </para>
    /// </remarks>
    [Test]
    public async Task TheirPyEvJosev_AgainstOurSecc_RunsToCompletion()
    {

        var listenPort       = InteropEnvironment.ListenPortOrIgnore(
                                   "the port our SECC should listen on for their PyEvJosev");
        var (protocol, mode) = InteropEnvironment.ProtocolAndMode();
        var (protocolName, modeName) = InteropEnvironment.ProtocolAndModeNames();
        var preferDynamic    = InteropEnvironment.PreferDynamic();
        var offerPnc         = InteropEnvironment.OfferPlugAndCharge();

        var recording = InteropRecording.FromEnvironment($"everest-{protocolName}-{modeName}-reverse");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(240));

        using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.IPv6Any, listenPort));

        TestContext.Out.WriteLine($"Waiting for their PyEvJosev on [::]:{listenPort} ...");

        using var socket = await listener.AcceptAsync(cts.Token);

        var stream = recording?.Tap(socket) ?? socket;

        try
        {
            await SapHandshake.RunSeccSideAsync(stream, protocol, cts.Token);

            var isDone = await InteropSession.RunSeccAsync(stream, protocol, mode, cts.Token, preferDynamic, offerPnc);

            Assert.That(isDone, Is.True, "our SECC drove their EV to the terminal session state");
        }
        finally
        {
            Report(recording?.Save(protocolName, modeName,
                                   "live interop: EVerest's PyEvJosev against our SECC",
                                   weAreTheEvcc: false));
        }

    }


    private static void Report(IReadOnlyList<String>? written)
    {
        if (written is null)
        {
            TestContext.Out.WriteLine(
                "Nothing was recorded. Set V2G_INTEROP_RECORD=<dir>, and V2G_INTEROP_SCENARIO=<trace> to " +
                "get the flow compared against one of our recorded sessions — for this counterparty the " +
                "station → EV half of that comparison is the interesting one.");
            return;
        }

        TestContext.Out.WriteLine("Recorded:");
        foreach (var path in written)
            TestContext.Out.WriteLine($"  {path}");
    }

}
