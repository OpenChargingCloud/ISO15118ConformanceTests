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
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso20;
using cloud.charging.open.protocols.ISO15118.Transport;

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

        // 180 s covers every ordinary run. A run that raises V2G_INTEROP_ONGOING is measuring a station
        // timer longer than that, so the budget has to clear it — and by a margin, or the run ends on
        // *our* deadline one message before theirs and reads as "the station never answered".
        //
        // A battery-driven charge loop needs the same consideration for the same reason: it runs until the
        // car is done, at whatever interval the run set, so its budget is computed from the two.
        var ongoing        = InteropEnvironment.OngoingTimeout();
        var sessionBudget  = ongoing is { } pollBudget
                                 ? pollBudget + TimeSpan.FromSeconds(120)
                                 : InteropEnvironment.ChargeSessionBudget() ?? TimeSpan.FromSeconds(180);
        using var cts = new CancellationTokenSource(sessionBudget);

        // Stand in as the certificate-provisioning backend their station publishes to, if the run asks
        // for it. Started *before* the session, because the window it has to answer in is 4,5 s wide and
        // opens the moment our CertificateInstallationReq arrives. Same process as the car on purpose:
        // two dotnet test runs racing a 4,5 s window is a coordination problem this does not need.
        var backendDirectory = InteropEnvironment.Read("V2G_INTEROP_PROVISION_BACKEND");
        var backend = String.IsNullOrEmpty(backendDirectory)
                          ? null
                          : Iso2MoBackend.RunOnceAsync(backendDirectory, cts.Token);

        if (backend is not null)
            TestContext.Out.WriteLine($"MO backend: waiting for a forwarded request in {backendDirectory}");

        TestContext.Out.WriteLine($"Connecting to their SECC at {endpoint} ...");

        using var socket = await TcpV2GClient.ConnectAsync(endpoint.ConnectHost, endpoint.Port,
                                                           InteropEnvironment.DevTlsOrNull(protocol), cts.Token);

        var transport = InteropEnvironment.ReportTransport(socket, protocol,
                                                           InteropEnvironment.OfferBothProtocols());

        var stream = recording?.Tap(socket) ?? socket;

        try
        {
            if (InteropEnvironment.OfferBothProtocols())
            {
                // The IsoMux case, from the EV side: both protocols in one offer, the station picks,
                // and the state machine is chosen after the handshake rather than before it.
                var offers   = InteropEnvironment.BothOffers(mode);
                var accepted = await SapHandshake.RunEvccSideAsync(stream, offers, cts.Token, transport: transport);
                protocol = accepted.Protocol;

                String Name(ProtocolVariant p) => p == ProtocolVariant.Iso15118_20 ? "-20" : "-2";
                TestContext.Out.WriteLine(
                    $"SAP: offered {String.Join(", ", offers.Select((o, i) => $"{Name(o.Protocol)} (priority {i + 1})"))}; " +
                    $"the station picked {Name(protocol)}.");
            }
            else
                await SapHandshake.RunEvccSideAsync(stream, protocol, cts.Token, mode, transport: transport);

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
                                                            ongoingTimeout: ongoing);

            TestContext.Out.WriteLine($"Authorization: {outcome.AuthorizationMode}" +
                                      (outcome.MeteringReceiptsSent > 0
                                           ? $", {outcome.MeteringReceiptsSent} signed metering receipt(s)" : ""));

            // Reported, never asserted — the same rule as the MeterInfo and sequence-timeout lines below,
            // and for a sharper reason here: this run exists to find out what their station does with a
            // -2 provisioning request, and a refusal, an unverifiable signature or an undecryptable key
            // are all results. The one thing that would make the run say nothing is the station never
            // offering the service, which is why Offered is printed first.
            // Reported, never asserted, like the lines below it: a station that ignores the request and
            // charges on is the finding, and a run that failed instead would say less.
            if (InteropEnvironment.Renegotiate())
                TestContext.Out.WriteLine(
                    $"Renegotiation ([V2G2-841]): the EV asked for one mid-charge; "
                  + $"{outcome.Renegotiations} cycle(s) completed and the session carried on.");

            if (backend is not null)
                TestContext.Out.WriteLine(
                    "MO backend: " + (backend.IsCompletedSuccessfully && backend.Result is { } issued
                                          ? issued
                                          : "never saw a forwarded request — their station published nothing, "
                                          + "or the bridge did not carry it"));

            if (outcome.Provisioning is { } p)
                TestContext.Out.WriteLine(
                    $"Contract provisioning ({p.Action}): "
                  + (p.Offered
                         ? $"issued {p.ContractSubject} as {p.Emaid ?? "(no eMAID)"}; "
                         + $"response signature {(p.SignatureOk ? "verified" : "NOT verified")} ([V2G2-891]), "
                         + $"contract key {(p.KeyRecovered ? "recovered and matched" : "NOT recovered")}"
                         : "the station advertised no certificate service in its ServiceList, so nothing was asked"));

            // Reported, never asserted. A station that answers nothing is the finding rather than a broken
            // run, so this line has to survive into the transcript instead of failing the test — the same
            // reason the -2 metering-receipt count above is printed and not required.
            if (protocol == ProtocolVariant.Iso15118_20 && InteropEnvironment.RequestMeterInfo())
                TestContext.Out.WriteLine(
                    $"MeterInfo: asked in every charge-loop request ([V2G20-1081]); " +
                    $"{outcome.MeterInfoResponses} response(s) carried the element ([V2G20-1082]).");

            // Reported, never asserted, for the same reason as the MeterInfo line: the number IS the
            // result. [V2G20-1500]/[V2G20-1502] give the SECC 0,5 s here (Tables 216/217); Table 215's 60 s
            // is the value for every message outside the charge loop.
            if (InteropEnvironment.SilentInChargeLoop() is { } budget)
                TestContext.Out.WriteLine(
                    "V2G_SECC_Sequence_Timeout: our EV stopped sending inside the charge loop and held the "
                  + "connection open; the station "
                  + (outcome.SilenceEndedAfter is { } t2
                         ? $"ended the session after {t2.TotalSeconds:0.00} s"
                         : $"had still not ended it after {budget.TotalSeconds:0} s")
                  + " (allowed: 0,5 s in the charge loop).");

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
            InteropEnvironment.WarnIfIgnored();
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

        // Their EV verifies the station against the V2G root in its own PKI path
        // (`get_ssl_context(server_side=False)` → `CertPath.V2G_ROOT_PEM`, `CERT_REQUIRED`, hostname
        // checking off), so the only material that can work here is theirs — SECC_LEAF plus both CPO
        // Sub-CAs as a PKCS#12. Null leaves the listener plaintext, which is what every reverse run
        // against this counterparty was until 2026-08-14: the knob has existed since the tux-evse runs
        // and the eVDriveFlow fixture uses it, and this one simply never reached for it.
        var serverTls = InteropEnvironment.ServerTlsOrNull(protocol);

        var recording = InteropRecording.FromEnvironment(
                            $"everest-{protocolName}-{modeName}-reverse{(serverTls is null ? "" : "-tls")}");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(240));

        using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.IPv6Any, listenPort), serverTls);

        // Their EV cannot be pointed at this socket — it takes a `device` and finds a station by SDP on it
        // — so without V2G_INTEROP_SDP this fixture waits for a car that has no way to arrive. See
        // InteropSdp: the run it exists for is the one below.
        //
        // The flag is the listener's own, never a constant: their EV reads the security byte out of the
        // SDP response and opens a TLS or a plaintext socket accordingly, so a station advertising one and
        // serving the other is discovered and then fails, which reads as a defect of theirs.
        await using var sdp = await InteropSdp.AdvertiseOrNullAsync(listener.LocalEndpoint.Port,
                                                                     tls: serverTls is not null, cts.Token);

        TestContext.Out.WriteLine($"Waiting for their PyEvJosev on [::]:{listenPort} " +
                                  $"({(serverTls is null ? "plain TCP" : "TLS")}) ...");

        using var socket = await listener.AcceptAsync(cts.Token);

        // Read back rather than assumed. Their EV pins `set_ecdh_curve("prime256v1")` and, under TLS 1.3,
        // presents the vehicle credential their own `create_certs.sh` mints — so what was negotiated and
        // who authenticated are results of the run rather than configuration restated.
        if (socket is SslStream tls)
            TestContext.Out.WriteLine(
                $"TLS: {tls.SslProtocol}, {tls.NegotiatedCipherSuite}, " +
                $"client certificate {(tls.RemoteCertificate is { } c ? c.Subject : "none")}.");

        var transport = InteropEnvironment.ReportTransport(socket, protocol);

        var stream = recording?.Tap(socket) ?? socket;

        try
        {
            // `mode` is passed, and that is not decoration: RunSeccSideAsync defaults it to PowerMode.Dc,
            // so this fixture announced a DC-only `-20` catalogue no matter what V2G_INTEROP_MODE said —
            // for as long as every reverse run happened to be DC, which was all of them until 2026-08-13.
            // Their AC EV then offered `-20:AC` and our own station refused it: "the EVCC offered none of
            // urn:iso:std:iso:15118:-20:DC". The forward fixture never had the bug because the EVCC side
            // takes the mode as a required argument. Same shape the sweep keeps finding, here in our
            // harness rather than in the stack: a value we already held, defaulted instead of passed.
            await SapHandshake.RunSeccSideAsync(stream, protocol, cts.Token, mode, transport);

            var outcome = await InteropSession.RunSeccAsync(stream, protocol, mode, cts.Token, preferDynamic, offerPnc,
                                                            mcs: InteropEnvironment.Mcs(),
                                                            requestRenegotiation: InteropEnvironment.RequestRenegotiation());

            ReportWhatOurStationSaw(outcome);

            if (InteropEnvironment.RequestRenegotiation())
                TestContext.Out.WriteLine(
                    "Renegotiation: our station signalled it once mid-charge " +
                    (protocol == ProtocolVariant.Iso15118_20 ? "([V2G20-1477])" : "([V2G2-841])") +
                    " — what their EV did with it is in the frame log, not in this line.");

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

            // The same guard for the bidirectional services, and this direction needs it more than the
            // forward one. `Secc20Ac` advertises { 1, 5 } and `Secc20Dc` { 2, 6 }, so an EV configured for
            // AC_BPT that quietly selects plain AC out of that catalogue charges to SessionStop exactly as
            // happily — and the run would be filed as the one in which somebody else's car chose our
            // bidirectional service. Nothing on the wire distinguishes the two afterwards except this id.
            //
            // `V2G_INTEROP_BPT_FIRST` means something different here than in a forward run, deliberately
            // rather than by accident: there its EVCC half ranks the bidirectional entry first in *our*
            // request, and in reverse we have no EVCC, so what is left is the claim the run is making.
            if (InteropEnvironment.BptFirst())
                Assert.That(outcome.SelectedEnergyServiceId is { } bpt && EnergyTransferService.IsBidirectional(bpt),
                            Is.True,
                            $"this run claims a bidirectional service (5 = AC_BPT, 6 = DC_BPT, 9 = MCS_BPT), "
                          + $"and their EV selected {outcome.SelectedEnergyServiceId?.ToString() ?? "none"} "
                          + $"out of our catalogue — so their car did not ask for the bidirectional entry");

            // And the third thing an EV can quietly not do. `[V2G20-2656]` has this station advertise both
            // control modes always; PreferDynamicControlMode only decides which one comes *first* in
            // ServiceDetailRes. So an EV that takes the Scheduled set out of a Dynamic-first offer runs a
            // Scheduled session, completes to SessionStop, and looks identical afterwards — our station
            // answers in kind either way ([V2G20-1600]). It knew which one it answered; until 2026-08-14
            // it had no property to say so.
            if (protocol == ProtocolVariant.Iso15118_20 && preferDynamic)
                Assert.That(outcome.EvControlModeIsDynamic, Is.True,
                            "this run offers Dynamic first and is written up as a Dynamic session, so their "
                          + "car had to have sent Dynamic_SEReqControlMode — it sent "
                          + (outcome.EvControlModeIsDynamic is null
                                 ? "no ScheduleExchangeReq at all"
                                 : "the Scheduled control mode"));
        }
        finally
        {
            InteropEnvironment.WarnIfIgnored();
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

        // The other thing the peer chose rather than was given. Both modes are always on offer, so this is
        // a result of the run and not a restatement of V2G_INTEROP_DYNAMIC.
        if (outcome.EvControlModeIsDynamic is { } dynamic)
            TestContext.Out.WriteLine($"Control mode: {(dynamic ? "Dynamic" : "Scheduled")} — " +
                                      $"read off their ScheduleExchangeReq, not off our offer.");

        // A -2 session that ends because our own guard refused a message reaches the terminal state like any
        // other, so IsDone alone would report it as a completed charge (see SeccOutcome.SequenceErrorAt).
        if (outcome.SequenceErrorAt is { } refused)
            TestContext.Out.WriteLine($"Sequence error: our station refused their EV's {refused} and ended " +
                                      $"the session with FAILED_SequenceError.");

        // Named as a verdict rather than a tick: a contract that arrived and failed to verify is a finding,
        // and it completes the session either way — our SECC does not refuse on a bad signature.
        // The chain verdict is part of this line and was not, until 2026-08-15: `PnCAuthResult` has
        // carried `Chain` since it existed, every reverse run printed the three signature checks without
        // it, and the matrix therefore read "verified by our SECC" for sessions whose contract chain
        // nobody had validated. Printed even when unconfigured, because "not checked" and "checked and
        // bad" must never read the same.
        if (outcome.PlugAndCharge is { } pnc)
            TestContext.Out.WriteLine(
                $"Plug & Charge (inbound): contract {pnc.ContractSubject}; " +
                $"challenge {(pnc.ChallengeOk ? "OK" : "MISMATCH")}, digest {(pnc.DigestOk ? "OK" : "FAIL")}, " +
                $"signature {(pnc.SignatureOk ? "OK" : "FAIL")} ({pnc.SignatureMethod}" +
                $"{(pnc.SignatureOk ? $", grammar={pnc.SignatureGrammar}" : "")}); " +
                $"chain {(pnc.Chain.Ok ? $"trusted, anchored at {pnc.Chain.Anchor}" : pnc.Chain.Reason)}.");

        // The `-2` twin. Their EV selects Contract the moment the transport is TLS — `-2` ties Plug &
        // Charge to it — so this is the line that says whether the signature it then sent verified,
        // rather than merely that the session completed.
        if (outcome.PlugAndChargeIso2 is { } pnc2)
            TestContext.Out.WriteLine(
                $"Plug & Charge (inbound, -2): contract {pnc2.ContractSubject}; " +
                $"challenge {(pnc2.ChallengeOk ? "OK" : "MISMATCH")}, digest {(pnc2.DigestOk ? "OK" : "FAIL")}, " +
                $"signature {(pnc2.SignatureOk ? "OK" : "FAIL")}" +
                $"{(pnc2.SignatureOk ? $", grammar={pnc2.SignatureGrammar}" : "")}; " +
                // "not checked" and "checked and bad" must never read the same, which is why ChainResult
                // keeps NotConfigured distinct from a rejection — so the reason is printed either way.
                $"chain {(pnc2.Chain.Ok ? $"trusted, anchored at {pnc2.Chain.Anchor}" : pnc2.Chain.Reason)}.");

        // `[V2G2-903]` makes a signed MeteringReceiptReq a *shall* on a Contract car, and our station
        // verifies each one. Counted rather than listed: what matters is that they all verified and how
        // many arrived.
        if (outcome.MeteringReceipts is { Count: > 0 } receipts)
            TestContext.Out.WriteLine(
                $"Metering receipts (inbound, -2): {receipts.Count}, " +
                $"{receipts.Count(r => r.SignatureOk && r.DigestOk)} verified" +
                $"{(receipts.Select(r => r.SignatureGrammar).Distinct().Count() == 1 ? $", grammar={receipts[0].SignatureGrammar}" : "")}.");

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
