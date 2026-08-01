using System.Net;

using NUnit.Framework;

using Vanaheimr.V2G.Simulation.Sap;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.Transport;

namespace Vanaheimr.V2G.Simulation.Tests.Interop;

/// <summary>
/// Tier-2 interop against <b>EDF-Lab/eVDriveFlow</b> — a Python ISO 15118-<b>20</b> stack, both ends,
/// DC bidirectional power transfer in Dynamic control mode.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ExplicitAttribute">[Explicit]</see> and environment-gated, like every interop fixture. Bring
/// their stack up per <c>tools/interop-evdriveflow/README.md</c>, then:
/// <code>dotnet test --filter TestCategory=Interop</code>
/// </para>
///
/// <para><b>Why this counterparty, specifically.</b></para>
/// <para>
/// It is the only one that goes straight at the combination we have the least outside evidence for:
/// <b>-20 Edition 1, DC BPT, Dynamic control mode, mutual TLS 1.3</b>. <c>docs/pki-model.md</c> pins -20
/// to TLS 1.3 with a mutual handshake and our own tests have been the only thing that ever checked we do
/// it right. A second implementation that <i>requires</i> it is a real oracle rather than a second opinion
/// from ourselves.
/// </para>
/// <para>
/// And unlike the cbexigen-based simulators, its EXI is <b>OpenEXI</b> (Java, hence their JDK
/// requirement) — a third independent lineage after our cbV2G corpus and Josev's EXIficient. So here a
/// byte disagreement <i>is</i> a finding, and so is a flow disagreement.
/// </para>
///
/// <para><b>Dynamic control mode is the point, not a detail.</b></para>
/// <para>
/// Set <c>V2G_INTEROP_DYNAMIC=1</c> when our station is the one under test: it makes the SECC offer the
/// Dynamic parameter set first, so an EV that takes the first offered set runs a Dynamic session. That is
/// the path that drives schedule renegotiation, which our recorded corpus touches only where we chose to
/// record it — and "where we chose to record it" is precisely the blind spot a second implementation
/// exists to find.
/// </para>
/// <para>
/// There is no scenario file to compare against here: eVDriveFlow is a state machine, not a replayer. The
/// reference for the flow report is therefore one of our own recorded sessions — point
/// <c>V2G_INTEROP_SCENARIO</c> at <c>Vectors/Session.iso20-dc-eim.trace.json</c>. That comparison is not a
/// conformance claim and is not meant as one; it answers "did the live run take the same route as ours",
/// and against a Dynamic-mode peer it has every reason to say no. The divergence is the result.
/// </para>
/// </remarks>
[TestFixture]
[Category("Interop")]
[Explicit("Requires a running eVDriveFlow endpoint (see tools/interop-evdriveflow/README.md); never part of the offline CI run.")]
public class EvDriveFlowInteropTests
{

    /// <summary>Our car against their charging station (<c>start_evse.py</c>).</summary>
    [Test]
    public async Task OurEvcc_AgainstTheirSecc_RunsToCompletion()
    {

        var endpoint         = InteropEnvironment.SeccEndpointOrIgnore(
                                   "their SECC, e.g. [fe80::1%enp0s3]:49152 (evse_config.ini tcp_port)");
        var (protocol, mode) = InteropEnvironment.ProtocolAndMode();
        var (protocolName, modeName) = InteropEnvironment.ProtocolAndModeNames();

        WarnIfNotIso20(protocol);

        var recording = InteropRecording.FromEnvironment($"evdriveflow-{modeName}-forward");

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
                        "our EVCC exchanged at least one message with their SECC");
        }
        finally
        {
            Report(recording?.Save(protocolName, modeName,
                                   "live interop: our EVCC against EDF-Lab/eVDriveFlow's SECC",
                                   weAreTheEvcc: true));
        }

    }


    /// <summary>Their car (<c>start_ev.py</c>) against our charging station.</summary>
    /// <remarks>
    /// The direction that tests what we <b>accept</b>, and the one where Dynamic control mode matters:
    /// their EV expects a station that can run it.
    /// </remarks>
    [Test]
    public async Task TheirEvcc_AgainstOurSecc_RunsToCompletion()
    {

        var listenPort       = InteropEnvironment.ListenPortOrIgnore(
                                   "the port our SECC should listen on for their EV");
        var (protocol, mode) = InteropEnvironment.ProtocolAndMode();
        var (protocolName, modeName) = InteropEnvironment.ProtocolAndModeNames();
        var preferDynamic    = InteropEnvironment.PreferDynamic();

        WarnIfNotIso20(protocol);

        if (protocol == ProtocolVariant.Iso15118_20 && !preferDynamic)
            TestContext.Out.WriteLine(
                "V2G_INTEROP_DYNAMIC is not set, so our SECC offers Scheduled control mode first. Their " +
                "stack is built around Dynamic — if the session stops after ServiceSelection, that is the " +
                "first thing to change.");

        var recording = InteropRecording.FromEnvironment($"evdriveflow-{modeName}-reverse");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(240));

        // IPv6, dual-stack: their EV reaches the station over a link-local address on the interface named
        // in ev_config.ini, and an IPv4 wildcard socket cannot accept that connection at all.
        using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.IPv6Any, listenPort));

        TestContext.Out.WriteLine($"Waiting for their EV on [::]:{listenPort} " +
                                  $"(control mode: {(preferDynamic ? "Dynamic" : "Scheduled")}) ...");

        using var socket = await listener.AcceptAsync(cts.Token);

        var stream = recording?.Tap(socket) ?? socket;

        try
        {
            await SapHandshake.RunSeccSideAsync(stream, protocol, cts.Token);

            var isDone = await InteropSession.RunSeccAsync(stream, protocol, mode, cts.Token, preferDynamic);

            Assert.That(isDone, Is.True, "our SECC drove their EV to the terminal session state");
        }
        finally
        {
            Report(recording?.Save(protocolName, modeName,
                                   $"live interop: EDF-Lab/eVDriveFlow's EV against our SECC " +
                                   $"({(preferDynamic ? "Dynamic" : "Scheduled")} control mode)",
                                   weAreTheEvcc: false));
        }

    }


    private static void WarnIfNotIso20(ProtocolVariant protocol)
    {
        if (protocol != ProtocolVariant.Iso15118_20)
            TestContext.Out.WriteLine(
                "V2G_INTEROP_PROTOCOL is not 20. eVDriveFlow implements ISO 15118-20 Edition 1 only, so a " +
                "-2 session will not get past the SupportedAppProtocol handshake.");
    }


    private static void Report(IReadOnlyList<String>? written)
    {
        if (written is null)
        {
            TestContext.Out.WriteLine(
                "Nothing was recorded. Set V2G_INTEROP_RECORD=<dir>, and V2G_INTEROP_SCENARIO=<trace> to " +
                "get the flow compared against one of our recorded sessions.");
            return;
        }

        TestContext.Out.WriteLine("Recorded:");
        foreach (var path in written)
            TestContext.Out.WriteLine($"  {path}");
    }

}
