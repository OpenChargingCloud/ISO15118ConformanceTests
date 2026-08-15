/*
 * Copyright (c) 2021-2025 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of WWCP ISO/IEC 15118 <https://github.com/OpenChargingCloud/WWCP_ISO15118>
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Net;
using System.Net.Security;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118.Sap;
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.Transport;

namespace ISO15118ConformanceTests.Simulation.Interop;

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
                                                           InteropEnvironment.DevTlsOrNull(protocol), cts.Token);

        var transport = InteropEnvironment.ReportTransport(socket, protocol);

        var stream = recording?.Tap(socket) ?? socket;

        try
        {
            await SapHandshake.RunEvccSideAsync(stream, protocol, cts.Token, mode, transport: transport);

            var outcome = await InteropSession.RunEvccAsync(stream, protocol, mode, cts.Token,
                                                            InteropEnvironment.PreferDynamic(),
                                                            InteropEnvironment.ContractCredentialsOrNull(),
                                                            mcs: InteropEnvironment.Mcs());

            TestContext.Out.WriteLine($"Authorization: {outcome.AuthorizationMode}" +
                                      (outcome.MeteringReceiptsSent > 0
                                           ? $", {outcome.MeteringReceiptsSent} signed metering receipt(s)" : ""));

            Assert.That(outcome.Exchanges, Is.GreaterThan(0),
                        "our EVCC exchanged at least one message with their SECC");
        }
        finally
        {
            InteropEnvironment.WarnIfIgnored();
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
        var offerPnc         = InteropEnvironment.OfferPlugAndCharge();

        WarnIfNotIso20(protocol);

        if (protocol == ProtocolVariant.Iso15118_20 && !preferDynamic)
            TestContext.Out.WriteLine(
                "V2G_INTEROP_DYNAMIC is not set, so our SECC offers Scheduled control mode first. Their " +
                "stack is built around Dynamic — if the session stops after ServiceSelection, that is the " +
                "first thing to change.");

        // Their EV verifies the station against its own V2G root (`SECC_CERTIFICATE_AUTHORITY` in
        // shared/global_values.py) and presents a vehicle certificate of its own, so the only material
        // that can work here is theirs — their `seccCertChain.pem` + `secc.key` as a PKCS#12. See
        // InteropEnvironment.ServerTlsOrNull; null leaves the listener plaintext, as before.
        var serverTls = InteropEnvironment.ServerTlsOrNull(protocol);

        var recording = InteropRecording.FromEnvironment(
                            $"evdriveflow-{modeName}-reverse{(serverTls is null ? "" : "-tls")}");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(240));

        // IPv6, dual-stack: their EV reaches the station over a link-local address on the interface named
        // in ev_config.ini, and an IPv4 wildcard socket cannot accept that connection at all.
        using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.IPv6Any, listenPort), serverTls);

        // The same flag the listener was built with: their EV reads the SDP response's security byte and
        // opens a TLS or a plaintext socket accordingly, so the two must never disagree.
        await using var sdp = await InteropSdp.AdvertiseOrNullAsync(listener.LocalEndpoint.Port,
                                                                     tls: serverTls is not null, cts.Token);

        TestContext.Out.WriteLine($"Waiting for their EV on [::]:{listenPort} " +
                                  $"({(serverTls is null ? "plain TCP" : "TLS")}, control mode: " +
                                  $"{(preferDynamic ? "Dynamic" : "Scheduled")}) ...");

        using var socket = await listener.AcceptAsync(cts.Token);

        var transport = InteropEnvironment.ReportTransport(socket, protocol);

        // Read back rather than assumed: their EV brings a secp521r1 PKI, which is the curve ISO 15118-20
        // asks for and the one no counterparty had supplied before — so what was actually negotiated is
        // the result of this run, not a detail of it.
        if (socket is SslStream tls)
            TestContext.Out.WriteLine(
                $"TLS: {tls.SslProtocol}, {tls.NegotiatedCipherSuite}, " +
                $"client certificate {(tls.RemoteCertificate is { } c ? c.Subject : "none")}.");

        // The managed backend has no SslStream to interrogate, and this fixture will not claim to have
        // read back what it only configured. What it can say is what that backend *can* negotiate: TLS 1.3
        // and the -20 suite pair, by construction (BcV2GTls.Tls13Only / .CipherSuites, refused rather than
        // downgraded). The peer's own log is the independent witness for what was actually agreed, and the
        // client certificate is reported from the validation callback either way.
        else if (serverTls is not null)
            TestContext.Out.WriteLine(
                "TLS: BouncyCastle backend — TLS 1.3 with the ISO 15118-20 suite pair by construction; " +
                "what was negotiated is confirmed from the peer's log, not asserted here.");

        var stream = recording?.Tap(socket) ?? socket;

        // Reported from the finally rather than after the assertion, because their EV has stopped
        // mid-session in every run so far and the selection is exactly what those runs are about.
        InteropSession.SeccOutcome? seen = null;

        try
        {
            await SapHandshake.RunSeccSideAsync(stream, protocol, cts.Token, transport: transport);

            var outcome = await InteropSession.RunSeccAsync(stream, protocol, mode, cts.Token, preferDynamic, offerPnc,
                                                            mcs: InteropEnvironment.Mcs(),
                                                            observed: o => seen = o);

            Assert.That(outcome.IsDone, Is.True, "our SECC drove their EV to the terminal session state");
        }
        finally
        {
            if (seen?.SelectedEnergyServiceId is { } serviceId)
                TestContext.Out.WriteLine(
                    $"Energy transfer service: {serviceId} ({InteropSession.ServiceName(serviceId)}) — " +
                    $"their EV's pick out of our catalogue.");

            InteropEnvironment.WarnIfIgnored();
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
