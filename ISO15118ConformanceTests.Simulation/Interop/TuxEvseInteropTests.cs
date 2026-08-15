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
using cloud.charging.open.protocols.ISO15118.Transport;

namespace ISO15118ConformanceTests.Simulation.Interop;

/// <summary>
/// Tier-2 interop against <b>tux-evse/iso15118-simulator-rs</b> — a Rust ISO 15118 simulator that plays
/// either end, driven by scenarios generated from packet captures.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ExplicitAttribute">[Explicit]</see> and environment-gated, like every interop fixture: the
/// offline <c>dotnet test -c Release</c> run must stay green with no peer, no container and no network.
/// Bring their simulator up per <c>tools/interop-tux-evse/README.md</c>, then:
/// <code>dotnet test --filter TestCategory=Interop</code>
/// </para>
///
/// <para><b>What this counterparty is, and is not.</b></para>
/// <para>
/// Their EXI comes from <i>cbexigen</i> — the same generator that produces libcbv2g, which is where our
/// own byte-exact vector corpus comes from. So an EXI disagreement here is close to impossible <i>by
/// construction</i>, and this is not a second independent codec oracle the way Josev (EXIficient) is.
/// What it can find is everything above the codec: sequencing, timing, field semantics, the V2GTP framing,
/// and what our station does when a car does something ours never does.
/// </para>
/// <para>
/// The other half of what makes it worth the setup is that their side is a <b>replayer</b> rather than a
/// state machine. Their scenarios are transactions lifted out of a real capture — an Audi against an ABB
/// charger, in the file they ship — each with a recorded request to send and a recorded response to
/// compare against. That is the same idea as our <c>Session.*.trace.json</c>, arrived at independently,
/// and it means a run puts a <i>real car's</i> messages in front of our station rather than our own idea
/// of a car's.
/// </para>
///
/// <para><b>Reading the reverse run's verdict.</b></para>
/// <para>
/// When their injector drives our station, its own pass/fail is <i>not</i> our pass/fail, and expecting
/// otherwise wastes the first afternoon. Their <c>expect</c> block is compared against what comes back,
/// and it holds the captured station's values: <c>"id":"DE*PNX*E12345*1"</c> for the SessionSetupRes,
/// specific schedules, specific status flags. Our station is not that station, so those comparisons
/// legitimately fail. The verdict that matters is the one this fixture asserts — that our SECC drove the
/// session to its terminal state — plus the recording, which is what a disagreement can actually be
/// diagnosed from afterwards. <c>tools/interop-tux-evse/scenario-expectations.py</c> lists the
/// station-specific expectations in a scenario file up front, so the noise is known before the run rather
/// than discovered during it.
/// </para>
/// </remarks>
[TestFixture]
[Category("Interop")]
[Explicit("Requires a running tux-evse simulator (see tools/interop-tux-evse/README.md); never part of the offline CI run.")]
public class TuxEvseInteropTests
{

    /// <summary>
    /// Our car against their charger: their responder answers each of our requests with the recorded
    /// station's reply.
    /// </summary>
    /// <remarks>
    /// This is the direction that tests what we <b>accept</b>, and it is the one their design favours —
    /// the answers come from a real charger, including the fields a self-consistent implementation would
    /// never think to send itself.
    /// </remarks>
    [Test]
    public async Task OurEvcc_AgainstTheirResponder_RunsToCompletion()
    {

        var endpoint         = InteropEnvironment.SeccEndpointOrIgnore(
                                   "their EVSE responder's endpoint, e.g. [fe80::1%evcc-veth]:64109");
        var (protocol, mode) = InteropEnvironment.ProtocolAndMode();
        var (protocolName, modeName) = InteropEnvironment.ProtocolAndModeNames();

        var recording = InteropRecording.FromEnvironment($"tux-evse-{modeName}-forward");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        TestContext.Out.WriteLine($"Connecting to their responder at {endpoint} ...");

        using var socket = await TcpV2GClient.ConnectAsync(endpoint.ConnectHost, endpoint.Port,
                                                           InteropEnvironment.DevTlsOrNull(protocol), cts.Token);

        var transport = InteropEnvironment.ReportTransport(socket, protocol);

        var stream = recording?.Tap(socket) ?? socket;

        try
        {
            await SapHandshake.RunEvccSideAsync(stream, protocol, cts.Token, mode, transport: transport);

            // The full parameter list, for the reason the eVDriveFlow fixture got it on 2026-08-15: a knob
            // this fixture does not pass on is a knob that reads as a counterparty result. Their responder
            // speaks -2 and DIN, so the -20-only ones are refused rather than dropped inside RunEvccAsync —
            // sendSessionId is the one that means something here, `[V2G2-460]` being the -2 twin of the rule
            // this list was completed for, and their replayer the obvious next target for it.
            var outcome = await InteropSession.RunEvccAsync(stream, protocol, mode, cts.Token,
                                                            InteropEnvironment.PreferDynamic(),
                                                            InteropEnvironment.ContractCredentialsOrNull(),
                                                            mcs: InteropEnvironment.Mcs(),
                                                            bptFirst: InteropEnvironment.BptFirst(),
                                                            requestMeterInfo: InteropEnvironment.RequestMeterInfo(),
                                                            silentInChargeLoop: InteropEnvironment.SilentInChargeLoop(),
                                                            sendSessionId: InteropEnvironment.SendSessionId(),
                                                            supportedServiceIds: InteropEnvironment.SupportedServiceIds(),
                                                            certificateProvisioning: InteropEnvironment.CertificateProvisioningOrNull(),
                                                            renegotiate: InteropEnvironment.Renegotiate(),
                                                            ongoingTimeout: InteropEnvironment.OngoingTimeout());

            TestContext.Out.WriteLine($"Authorization: {outcome.AuthorizationMode}" +
                                      (outcome.MeteringReceiptsSent > 0
                                           ? $", {outcome.MeteringReceiptsSent} signed metering receipt(s)" : ""));

            Assert.That(outcome.Exchanges, Is.GreaterThan(0),
                        "our EVCC exchanged at least one message with their responder");
        }
        finally
        {
            // In the finally block deliberately: the run that threw is the run worth keeping.
            InteropEnvironment.WarnIfIgnored();
            Report(recording?.Save(protocolName, modeName,
                                   "live interop: our EVCC against tux-evse/iso15118-simulator-rs (responder)",
                                   weAreTheEvcc: true));
        }

    }


    /// <summary>
    /// Their car against our charger: their injector replays a captured vehicle's requests at us.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The half a self-consistent implementation is worst at — nothing in our own test suite has ever
    /// sent our station a message our own car would not send.
    /// </para>
    /// <para>
    /// The listener binds <see cref="IPAddress.IPv6Any"/> rather than <see cref="IPAddress.Any"/>, which
    /// is not a detail: their injector arrives over an IPv6 link-local address on a veth pair, and an
    /// IPv4 wildcard socket cannot accept that connection at all. It would wait out its timeout and
    /// report that no car ever came.
    /// </para>
    /// </remarks>
    [Test]
    public async Task TheirInjector_AgainstOurSecc_RunsToCompletion()
    {

        var listenPort       = InteropEnvironment.ListenPortOrIgnore(
                                   "the port our SECC should listen on for their injector");
        var (protocol, mode) = InteropEnvironment.ProtocolAndMode();
        var (protocolName, modeName) = InteropEnvironment.ProtocolAndModeNames();

        // Their station's PKI, ours to present: see InteropEnvironment.ServerTlsOrNull. Null = plaintext,
        // and every value below that mentions TLS follows from this one so they cannot disagree.
        var serverTls = InteropEnvironment.ServerTlsOrNull(protocol);

        var recording = InteropRecording.FromEnvironment(
                            $"tux-evse-{modeName}-reverse{(serverTls is null ? "" : "-tls")}");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(180));

        using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.IPv6Any, listenPort), serverTls);

        // Their injector discovers by SDP, and the response is where it learns *whether* to speak TLS —
        // their EVCC branches on the security field before it opens the socket. So this flag is the same
        // one the listener was built with: a station that advertises TLS and then answers in plaintext is
        // discovered and fails its handshake, which reads as a peer defect.
        await using var sdp = await InteropSdp.AdvertiseOrNullAsync(listener.LocalEndpoint.Port,
                                                                     tls: serverTls is not null, cts.Token);

        TestContext.Out.WriteLine($"Waiting for their injector on [::]:{listenPort} " +
                                  $"(IPv6, dual-stack, {(serverTls is null ? "plain TCP" : "TLS")}) ...");

        using var socket = await listener.AcceptAsync(cts.Token);

        var transport = InteropEnvironment.ReportTransport(socket, protocol);

        // What was actually negotiated, not what was asked for: the listener offers a pinned version and
        // suite list, and their GnuTLS profile spans more than one — so the run has to read back which
        // pairing survived rather than report the offer. Nothing else in a recording says it.
        if (socket is SslStream tls)
            TestContext.Out.WriteLine(
                $"TLS: {tls.SslProtocol}, {tls.NegotiatedCipherSuite}, " +
                $"client certificate {(tls.RemoteCertificate is { } c ? c.Subject : "none")}.");

        var stream = recording?.Tap(socket) ?? socket;

        try
        {
            await SapHandshake.RunSeccSideAsync(stream, protocol, cts.Token, transport: transport);

            var outcome = await InteropSession.RunSeccAsync(stream, protocol, mode, cts.Token,
                                                            mcs: InteropEnvironment.Mcs());

            if (outcome.SelectedEnergyServiceId is { } serviceId)
                TestContext.Out.WriteLine($"Energy transfer service: {serviceId} — their injector's pick.");

            Assert.Multiple(() =>
            {
                Assert.That(outcome.IsDone, Is.True, "our SECC drove their injector's session to the terminal state");

                // A replayer sends what its car sent, not what our station's last answer invited — so it is
                // the one peer that reaches this, and the reason our -2 guard now answers instead of closing
                // the connection (2026-08-06: a real VW polled Authorization twice). The refusal is correct
                // behaviour and a terminal state, which is exactly why the run has to say it happened:
                // otherwise a session that ended on our own sequence rules reads as a completed charge.
                Assert.That(outcome.SequenceErrorAt, Is.Null,
                            "their injector's route stayed within what our station accepts");
            });
        }
        finally
        {
            InteropEnvironment.WarnIfIgnored();
            Report(recording?.Save(protocolName, modeName,
                                   "live interop: tux-evse/iso15118-simulator-rs (injector) against our SECC",
                                   weAreTheEvcc: false));
        }

    }


    private static void Report(IReadOnlyList<String>? written)
    {
        if (written is null)
        {
            TestContext.Out.WriteLine(
                "Nothing was recorded. Set V2G_INTEROP_RECORD=<dir> — a live run against somebody else's " +
                "stack is not repeatable on demand, and a green tick keeps none of it.");
            return;
        }

        TestContext.Out.WriteLine("Recorded:");
        foreach (var path in written)
            TestContext.Out.WriteLine($"  {path}");
    }

}
