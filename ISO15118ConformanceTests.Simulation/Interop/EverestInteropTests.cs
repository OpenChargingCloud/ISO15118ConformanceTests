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

using Vanaheimr.V2G.Simulation.Sap;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using Vanaheimr.V2G.Simulation.Transport;

namespace ISO15118ConformanceTests.Simulation.Interop;

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
///   <item><b>Their station is new.</b> <c>EvseV2G</c> (DIN 70121 and -2, C) and <c>Evse15118D20</c>
///         (-20) are implementations nothing here has met. At <c>everest-core</c> HEAD <c>EvseV2G</c>
///         sits on cbV2G — the encoder our vector corpus is generated from — so a disagreement would
///         <b>not</b> be an EXI disagreement by construction: it would be a sequencing, timing or
///         semantics one, which is exactly the class our corpora cannot see.
///         <b>Check the image, though.</b> The <c>manager:main</c> demo image is everest-core 2023.10.0
///         and links <c>libopenv2g.so.1</c>, so runs against it <i>are</i> independent-codec results —
///         see <c>docs/interop-runs/2026-08-02-everest-iso2-dc-full-charge/</c>.</item>
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
    /// Our car against their charger — <c>EvseV2G</c> for DIN/-2, <c>Evse15118D20</c> for -20 and MCS.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The direction worth the setup. Their <c>EvseV2G</c> runs an SDP server by default
    /// (<c>enable_sdp_server: true</c>) on the interface named in its <c>device</c> setting, so the
    /// endpoint is normally discovered rather than configured — see the harness scripts.
    /// </para>
    /// <para>
    /// <b>The MCS arm</b> (<c>V2G_INTEROP_MODE=mcs</c>) is the one scenario here whose counterpart did not
    /// exist until recently: everest-core <b>2026.02.1</b> is the first release shipping
    /// <c>config/config-sil-mcs.yaml</c>, whose <c>EvseManager</c> carries <c>connector_type: cMCS</c> and
    /// therefore hands <c>Evse15118D20</c> the energy-transfer modes <c>MCS</c> / <c>MCS_BPT</c> — service
    /// ids <b>8</b> and <b>9</b>. Ours were read off their <c>libiso15118</c> headers and had never been
    /// negotiated with anything but ourselves, so this run is the first thing that can turn "the numbers
    /// agree with the one other implementation that has MCS" into "the numbers were accepted by it".
    /// </para>
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
                                                           InteropEnvironment.DevTlsOrNull(protocol), cts.Token);

        ReportNegotiatedTls(socket);

        var stream = recording?.Tap(socket) ?? socket;

        try
        {
            if (InteropEnvironment.OfferBothProtocols())
            {
                // The IsoMux case, from the EV side: both protocols in one offer, the station picks,
                // and the state machine is chosen after the handshake rather than before it.
                var offers   = InteropEnvironment.BothOffers(mode);
                var accepted = await SapHandshake.RunEvccSideAsync(stream, offers, cts.Token);
                protocol = accepted.Protocol;

                String Name(ProtocolVariant p) => p == ProtocolVariant.Iso15118_20 ? "-20" : "-2";
                TestContext.Out.WriteLine(
                    $"SAP: offered {String.Join(", ", offers.Select((o, i) => $"{Name(o.Protocol)} (priority {i + 1})"))}; " +
                    $"the station picked {Name(protocol)}.");
            }
            else
                await SapHandshake.RunEvccSideAsync(stream, protocol, cts.Token, mode);

            var outcome = await InteropSession.RunEvccAsync(stream, protocol, mode, cts.Token,
                                                            InteropEnvironment.PreferDynamic(),
                                                            InteropEnvironment.ContractCredentialsOrNull(),
                                                            mcs: InteropEnvironment.Mcs(),
                                                            bptFirst: InteropEnvironment.BptFirst());

            TestContext.Out.WriteLine($"Authorization: {outcome.AuthorizationMode}" +
                                      (outcome.MeteringReceiptsSent > 0
                                           ? $", {outcome.MeteringReceiptsSent} signed metering receipt(s)" : ""));

            if (outcome.SelectedEnergyServiceId is { } serviceId)
                TestContext.Out.WriteLine($"Energy transfer service: {serviceId} ({ServiceName(serviceId)}).");

            Assert.That(outcome.Exchanges, Is.GreaterThan(0),
                        "our EVCC exchanged at least one message with their charger");

            // The MCS run's actual claim, and it needs asserting rather than reading: Evcc20Mcs falls back
            // to a plain DC service when the station advertises no MCS one — deliberately, so a megawatt
            // truck can still charge at an ordinary post. That fallback completes a session just as
            // happily, and without this line a station that ignored MCS entirely would be written up as
            // the first live MCS result.
            if (InteropEnvironment.Mcs())
                Assert.That(outcome.SelectedEnergyServiceId, Is.EqualTo(8).Or.EqualTo(9),
                            "an MCS run has to have negotiated an MCS service (8 = MCS, 9 = MCS_BPT); "
                          + "anything else means their catalogue offered none and our EVCC fell back to DC");

            // And the same shape one negotiation further in, for whichever catalogue the run uses: a
            // station advertising only the unidirectional entry lets our EVCC take it and finish, so a run
            // that asked for BPT and quietly charged one-way would otherwise be filed as a BPT result. This
            // is the assertion the MCS arm carried as `Is.EqualTo(9)`; it is stated over
            // IsBidirectional now, so 5 and 6 are held to it too.
            if (InteropEnvironment.BptFirst())
                Assert.That(outcome.SelectedEnergyServiceId is { } id && EnergyTransferService.IsBidirectional(id),
                            Is.True,
                            $"BPT was ranked first, so the session had to negotiate a bidirectional service "
                          + $"(5 = AC_BPT, 6 = DC_BPT, 9 = MCS_BPT) — it negotiated "
                          + $"{outcome.SelectedEnergyServiceId?.ToString() ?? "none"}, which means their "
                          + $"catalogue did not carry one and our EVCC fell back");
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
    /// <para>
    /// <b>MCS is the exception to "the reverse direction is the less interesting one."</b> Their
    /// <c>config-sil-mcs.yaml</c> configures <c>PyEvJosev</c> with
    /// <c>supported_d20_energy_services: MCS</c> — an EV that asks for service 8 specifically. Pointed at
    /// our <c>Secc20Mcs</c> (<c>V2G_INTEROP_MODE=mcs</c>), that is the only run in this file that puts
    /// <i>our</i> MCS catalogue in front of a foreign chooser rather than our own.
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

        // Their EV cannot be pointed at this socket — it takes a `device` and finds a station by SDP on it
        // — so without V2G_INTEROP_SDP this fixture waits for a car that has no way to arrive. See
        // InteropSdp: the run it exists for is the one below.
        await using var sdp = await InteropSdp.AdvertiseOrNullAsync(listener.LocalEndpoint.Port,
                                                                     tls: false, cts.Token);

        TestContext.Out.WriteLine($"Waiting for their PyEvJosev on [::]:{listenPort} ...");

        using var socket = await listener.AcceptAsync(cts.Token);

        var stream = recording?.Tap(socket) ?? socket;

        try
        {
            await SapHandshake.RunSeccSideAsync(stream, protocol, cts.Token);

            var outcome = await InteropSession.RunSeccAsync(stream, protocol, mode, cts.Token, preferDynamic, offerPnc,
                                                            mcs: InteropEnvironment.Mcs());

            ReportWhatOurStationSaw(outcome);

            Assert.That(outcome.IsDone, Is.True, "our SECC drove their EV to the terminal session state");

            // The forward run has asserted its negotiated service since the first MCS session; this is the
            // same guard from the other end, and the direction that needs it more. Their EV is configured
            // `supported_d20_energy_services: MCS` but our Secc20Mcs offers { 8, 9 } beside a state machine
            // that would happily run a DC session too — so an EV that ignored the MCS entries and took an
            // ordinary service completes exactly as well, and would otherwise be written up as the run in
            // which somebody else's car chose our MCS catalogue.
            if (InteropEnvironment.Mcs())
                Assert.That(outcome.SelectedEnergyServiceId, Is.EqualTo(8).Or.EqualTo(9),
                            "an MCS reverse run has to have had an MCS service (8 = MCS, 9 = MCS_BPT) " +
                            "picked out of our catalogue; anything else means their EV asked for something " +
                            "and our station gave it");
        }
        finally
        {
            Report(recording?.Save(protocolName, modeName,
                                   "live interop: EVerest's PyEvJosev against our SECC",
                                   weAreTheEvcc: false));
        }

    }


    /// <summary>
    /// What the TLS handshake actually settled on, as opposed to what we asked for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every TLS run so far has been written up from the profile it <i>offered</i>, which is fine while the
    /// offer is a single pinned version and merely optimistic once it is not. A both-protocol offer now
    /// carries 1.2 and 1.3 (see <see cref="InteropEnvironment.DevTlsOrNull"/>), so the version is the
    /// station's choice and belongs in the transcript beside the SAP result.
    /// </para>
    /// <para>
    /// Silent for a plaintext run, and for a TLS backend that is not <see cref="SslStream"/> — the
    /// BouncyCastle transport the -20 profile needs on Windows exposes no equivalent, and a line that
    /// appears for some backends and not others is better than one that guesses.
    /// </para>
    /// </remarks>
    private static void ReportNegotiatedTls(Stream socket)
    {
        if (socket is SslStream { IsAuthenticated: true } ssl)
            TestContext.Out.WriteLine(
                $"TLS: {ssl.SslProtocol}, {ssl.NegotiatedCipherSuite}" +
                (ssl.RemoteCertificate is { } certificate ? $", server {certificate.Subject}" : "") +
                (ssl.LocalCertificate is not null ? ", client certificate presented (mutual)" : ""));
    }


    /// <summary>
    /// What our station learned that nothing else in the run can report.
    /// </summary>
    /// <remarks>
    /// In a reverse run the session is <i>ours</i>, so their charger module never sees it and their logs
    /// say nothing about it. Both lines below were read off the CLI's console for the 2026-08-06 MCS
    /// reverse run because the fixture had no equivalent; that is what made the run hard to read and is
    /// why they are printed here rather than left to the assertions.
    /// </remarks>
    private static void ReportWhatOurStationSaw(InteropSession.SeccOutcome outcome)
    {

        if (outcome.SelectedEnergyServiceId is { } serviceId)
            TestContext.Out.WriteLine($"Energy transfer service: {serviceId} ({ServiceName(serviceId)}) — " +
                                      $"their EV's pick out of our catalogue.");

        // A -2 session that ends because our own guard refused a message reaches the terminal state like any
        // other, so IsDone alone would report it as a completed charge (see SeccOutcome.SequenceErrorAt).
        if (outcome.SequenceErrorAt is { } refused)
            TestContext.Out.WriteLine($"Sequence error: our station refused their EV's {refused} and ended " +
                                      $"the session with FAILED_SequenceError.");

        // Named as a verdict rather than a tick: a contract that arrived and failed to verify is a finding,
        // and it completes the session either way — our SECC does not refuse on a bad signature.
        if (outcome.PlugAndCharge is { } pnc)
            TestContext.Out.WriteLine(
                $"Plug & Charge (inbound): contract {pnc.ContractSubject}; " +
                $"challenge {(pnc.ChallengeOk ? "OK" : "MISMATCH")}, digest {(pnc.DigestOk ? "OK" : "FAIL")}, " +
                $"signature {(pnc.SignatureOk ? "OK" : "FAIL")} ({pnc.SignatureMethod}" +
                $"{(pnc.SignatureOk ? $", grammar={pnc.SignatureGrammar}" : "")}).");

    }


    /// <summary>Table 204's names, shared with every other fixture whose peer selects from our catalogue —
    /// see <see cref="InteropSession.ServiceName"/>.</summary>
    private static String ServiceName(UInt16 serviceId) => InteropSession.ServiceName(serviceId);


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
