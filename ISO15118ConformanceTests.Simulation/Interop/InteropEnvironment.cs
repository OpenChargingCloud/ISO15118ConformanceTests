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

using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118.Sap;
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso2;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso20;
using cloud.charging.open.protocols.ISO15118.Transport;

namespace ISO15118ConformanceTests.Simulation.Interop;

/// <summary>
/// How an interop fixture learns where the peer is: one vocabulary of environment variables, shared by
/// every counterparty, because the fixtures differ in who is on the other end and in nothing else.
/// </summary>
/// <remarks>
/// <list type="table">
///   <item><term><c>V2G_INTEROP_SECC</c></term><description><c>host:port</c> or <c>[ipv6%zone]:port</c> —
///         their station, for a run in which we are the car.</description></item>
///   <item><term><c>V2G_INTEROP_LISTEN</c></term><description>a port — our station, for a run in which
///         they are the car.</description></item>
///   <item><term><c>V2G_INTEROP_SDP</c></term><description>an interface name — advertise that station over
///         SDP as well, for a car that discovers rather than connects (see
///         <see cref="InteropSdp"/>). The other half of <c>V2G_INTEROP_LISTEN</c>, and the reason a
///         reverse run can now be recorded.</description></item>
///   <item><term><c>V2G_INTEROP_PROTOCOL</c></term><description><c>2</c> (default), <c>20</c>, or
///   <c>both</c> — one handshake offering -20 at priority 1 and -2 at priority 2, running whichever
///   the station picks (EVCC side; see <see cref="OfferBothProtocols"/>).</description></item>
///   <item><term><c>V2G_INTEROP_MODE</c></term><description><c>ac</c> (default), <c>dc</c>, or
///         <c>mcs</c> — the Megawatt Charging System, which is the DC session under a different service
///         catalogue and implies -20 (see <see cref="Mcs"/>).</description></item>
///   <item><term><c>V2G_INTEROP_BPT_FIRST</c></term><description><c>1</c> to rank the bidirectional
///         service ahead of the unidirectional one in the EVCC's request — 5/6/9 rather than 1/2/8 (see
///         <see cref="BptFirst"/>; formerly <c>V2G_INTEROP_MCS_FIRST=9</c>, still honoured).</description></item>
///   <item><term><c>V2G_INTEROP_TLS</c></term><description><c>1</c> to run TLS, accepting any server
///         certificate. Development only.</description></item>
///   <item><term><c>V2G_INTEROP_TLS_TRUST</c></term><description>a PEM trust anchor — validate their
///         chain against it instead of accepting anything.</description></item>
///   <item><term><c>V2G_INTEROP_TLS_CLIENT</c></term><description><c>&lt;pfx&gt;[:password]</c> — our TLS
///         client certificate, which a -20 station requires.</description></item>
///   <item><term><c>V2G_INTEROP_TLS_SERVER</c></term><description><c>&lt;pfx&gt;[:password]</c> — the
///         certificate <i>our station</i> presents in a reverse run, and the only way that direction can run
///         over TLS at all (see <see cref="ServerTlsOrNull"/>). <c>V2G_INTEROP_TLS_REQUIRE_CLIENT=1</c>
///         additionally demands the car's.</description></item>
///   <item><term><c>V2G_INTEROP_NO_PNC</c></term><description><c>1</c> to advertise EIM only (-20).</description></item>
///   <item><term><c>V2G_INTEROP_RECORD</c></term><description>a directory for the artifacts — see
///         <see cref="InteropRecording"/>. Unset means a run that leaves nothing behind.</description></item>
/// </list>
/// </remarks>
internal static class InteropEnvironment
{

    /// <summary>
    /// Their station's endpoint, parsed and checked before anything opens a socket.
    /// </summary>
    /// <remarks>
    /// <see cref="V2GEndpoint"/> rather than a split at the last colon, because an ISO 15118 station is
    /// reached at a link-local address with a zone — <c>[fe80::ac52:27ff:fef3:d0d7%evcc-veth]:64109</c> is
    /// the form these simulators' own documentation uses — and a zone naming an interface this machine
    /// does not have is discarded by the platform without a word. The resulting connection failure looks
    /// exactly like "their station is not listening", which is the most expensive possible way to be told
    /// that the veth pair has not been created yet.
    /// </remarks>
    public static V2GEndpoint SeccEndpointOrIgnore(String hint)
    {

        var value = Environment.GetEnvironmentVariable("V2G_INTEROP_SECC");

        if (String.IsNullOrWhiteSpace(value))
            Assert.Ignore($"set V2G_INTEROP_SECC=host:port to run this — {hint}");

        return V2GEndpoint.Parse(value!, "V2G_INTEROP_SECC");

    }


    public static Int32 ListenPortOrIgnore(String hint)
    {

        var value = Environment.GetEnvironmentVariable("V2G_INTEROP_LISTEN");

        if (String.IsNullOrWhiteSpace(value))
            Assert.Ignore($"set V2G_INTEROP_LISTEN=port to run this — {hint}");

        return Int32.TryParse(value, out var port) && port is > 0 and <= 65535
                   ? port
                   : throw new ArgumentException($"V2G_INTEROP_LISTEN must be a TCP port, got '{value}'.");

    }


    /// <summary>-20 only: whether our station should offer the Dynamic control-mode parameter set first.
    /// <c>V2G_INTEROP_DYNAMIC=1</c>.</summary>
    public static Boolean PreferDynamic()
        => Environment.GetEnvironmentVariable("V2G_INTEROP_DYNAMIC") == "1";


    /// <summary>Our <b>station</b> asks the EV to renegotiate, once, mid-charge — `[V2G2-841]` for -2,
    /// `[V2G20-1477]` for -20. <c>V2G_INTEROP_RENEG=1</c>.</summary>
    /// <remarks>
    /// A reverse-run knob: it only means anything when their EV is on the other end. The station side has
    /// existed since 2026-07-22 but only the CLI could reach it, and the CLI writes no artifacts — so the
    /// one live renegotiation against a foreign EV is a pair of console logs rather than a recorded
    /// session. This makes such a run recordable like every other.
    /// </remarks>
    public static Boolean RequestRenegotiation()
        => Environment.GetEnvironmentVariable("V2G_INTEROP_RENEG") == "1";


    /// <summary>-20 only: our EV sets <c>MeterInfoRequested</c> in every charge-loop request.
    /// <c>V2G_INTEROP_METER=1</c>.</summary>
    /// <remarks>
    /// `[V2G20-1081]` is the EV's mechanism and `[V2G20-1082]` the station's duty to answer it, and until
    /// 2026-08-10 this EVCC hardcoded the field <c>false</c> — so no run of ours had ever asked, and no
    /// counterparty's answer had ever been checked. Off by default so every recorded session keeps the
    /// bytes it was recorded with.
    /// </remarks>
    public static Boolean RequestMeterInfo()
        => Environment.GetEnvironmentVariable("V2G_INTEROP_METER") == "1";


    /// <summary>-20 only: after one charge-loop iteration our EV stops sending and holds the connection
    /// open for this many seconds, to see when the station gives up. <c>V2G_INTEROP_SILENT=&lt;seconds&gt;</c>.</summary>
    /// <remarks>
    /// This is how <c>V2G_SECC_Sequence_Timeout</c> is measured at all: a car that hangs up is an EOF and
    /// says nothing about a timer. `[V2G20-1500]`/`[V2G20-1502]` give the SECC <b>0,5 s</b> in the charge
    /// loop (Tables 216/217) against the 60 s of Table 215 elsewhere, so a budget of ~90 s tells the two
    /// apart with room to spare. Unset by default, since a run that sets it does not charge.
    /// </remarks>
    /// <summary>-20 only: our EV puts this SessionID in every request after SessionSetup, so a station's
    /// `[V2G20-460]` duty to refuse a foreign one is reachable. <c>V2G_INTEROP_SESSIONID=&lt;hex|zero&gt;</c>.</summary>
    /// <remarks>
    /// <c>zero</c> is the value worth trying first: ISO reserves the all-zero id for *"I have no session"*,
    /// and it is the one EVerest's `-2` station was measured serving as the session owner's. Unset by
    /// default, so every recorded session keeps the id it echoed.
    /// </remarks>
    public static Byte[]? SendSessionId()
    {
        var raw = Environment.GetEnvironmentVariable("V2G_INTEROP_SESSIONID");
        if (String.IsNullOrWhiteSpace(raw))          return null;
        if (raw.Equals("zero", StringComparison.OrdinalIgnoreCase)) return new Byte[8];
        var hex = raw.Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        return hex.Length == 16 ? Convert.FromHexString(hex) : null;
    }


    public static TimeSpan? SilentInChargeLoop()
        => Int32.TryParse(Environment.GetEnvironmentVariable("V2G_INTEROP_SILENT"), out var s) && s > 0
               ? TimeSpan.FromSeconds(s)
               : null;


    /// <summary>
    /// <c>V2G_INTEROP_ONGOING=&lt;seconds&gt;</c> — how long our car keeps polling a phase that answers
    /// <c>EVSEProcessing = Ongoing</c>. Both protocols; default 60 s.
    /// </summary>
    /// <remarks>
    /// For measuring a station's *own* long timers, which are routinely longer than any car should wait:
    /// EvseV2G's <c>auth_timeout_eim</c> defaults to <b>300 s</b> and its <c>auth_timeout_pnc</c> to 55 s,
    /// and libiso15118's <c>TIMEOUT_EIM_ONGOING</c> is <b>180 s</b>. A run that leaves the default in place
    /// measures our patience rather than their timer, and the two are easy to confuse in a frame log —
    /// both end in "the station stopped being asked".
    /// <para>Raising it is not a claim that a car may wait that long. It is the instrument being taken
    /// out of the way of the thing being measured; a conformance statement about our car's patience is a
    /// different test.</para>
    /// See <c>docs/interop-runs/2026-08-13-everest-d20-eim-rejection/</c>.
    /// </remarks>
    public static TimeSpan? OngoingTimeout()
        => Int32.TryParse(Environment.GetEnvironmentVariable("V2G_INTEROP_ONGOING"), out var s) && s > 0
               ? TimeSpan.FromSeconds(s)
               : null;


    /// <summary>
    /// <c>V2G_INTEROP_CHARGELOOP=&lt;milliseconds&gt;</c> — what our <c>-20</c> station waits for the next
    /// request after a charge-loop response. Unset leaves <see cref="Secc20Base"/>'s own <b>500 ms</b>,
    /// which is what Tables 216/217 require (<c>[V2G20-1500]</c>, <c>[V2G20-1502]</c>).
    /// </summary>
    /// <remarks>
    /// The mirror image of <see cref="OngoingTimeout"/>, and it exists for the same reason: a run that
    /// stops on <i>our</i> timer has measured us. The `-20` AC reverse run on 2026-08-13 ended at
    /// <c>SECC sequence timeout: EV silent for &gt; 500 ms in the charge loop</c> after eleven clean
    /// exchanges — which says our station enforced the requirement and says nothing whatever about how
    /// fast their EV actually is. Raising this reads the peer's real pacing off the wire; the 500 ms
    /// default is the conformance statement and stays the default.
    /// <para>Raising it is not a claim that a station may wait that long, and a run that used it cannot
    /// be quoted as a passing charge-loop conformance result.</para>
    /// </remarks>
    public static TimeSpan? ChargeLoopTimeout()
        => Int32.TryParse(Environment.GetEnvironmentVariable("V2G_INTEROP_CHARGELOOP"), out var ms) && ms > 0
               ? TimeSpan.FromMilliseconds(ms)
               : null;


    /// <summary>
    /// -20 only: <c>V2G_INTEROP_NO_PNC=1</c> makes our station advertise EIM only.
    /// </summary>
    /// <remarks>
    /// For a counterparty whose EV cannot ignore an authorization service it does not support. Both
    /// offers are legal; see <c>Secc20Base.OfferPlugAndCharge</c> and
    /// <c>docs/interop-runs/2026-08-01-edf-iso20-dc-dynamic-reverse/</c>.
    /// </remarks>
    public static Boolean OfferPlugAndCharge()
        => Environment.GetEnvironmentVariable("V2G_INTEROP_NO_PNC") != "1";


    /// <summary>
    /// Whether this run is a <b>Megawatt Charging System</b> session — <c>V2G_INTEROP_MODE=mcs</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>MCS is not a third power mode</b>, which is why it is a predicate beside
    /// <see cref="ProtocolAndMode"/> rather than a third <see cref="PowerMode"/>. It is the DC message set
    /// — <c>DC_ChargeParameterDiscovery</c> → <c>CableCheck</c> → <c>PreCharge</c> → <c>ChargeLoop</c> →
    /// <c>WeldingDetection</c>, unchanged — advertised under energy-transfer service ids <b>8 (MCS)</b> and
    /// <b>9 (MCS_BPT)</b> instead of DC's 2 / 6, with a megawatt envelope. So the mode stays
    /// <see cref="PowerMode.Dc"/> and only the state machines change, to <c>Evcc20Mcs</c> /
    /// <c>Secc20Mcs</c> — thin subclasses of the DC ones that differ in the catalogue and the limits.
    /// </para>
    /// <para>
    /// <b>What a live run of this is worth.</b> Our service ids and connector values were read off
    /// EVerest's <c>libiso15118</c> and never met a counterpart — <c>docs/roadmap.md</c> carries MCS as
    /// "implemented, untested against a live counterpart", and <c>Secc20McsTests</c> is our own two sides
    /// agreeing with each other. everest-core <b>2026.02.1</b> is the first release to ship
    /// <c>config/config-sil-mcs.yaml</c>, so the numbers can finally be put on a wire somebody else reads.
    /// </para>
    /// </remarks>
    public static Boolean Mcs()
        => Environment.GetEnvironmentVariable("V2G_INTEROP_MODE") == "mcs";


    /// <summary>
    /// Ask for the <b>bidirectional</b> entry of whichever catalogue this run uses — AC_BPT (5) ahead of
    /// AC (1), DC_BPT (6) ahead of DC (2), MCS_BPT (9) ahead of MCS (8). <c>V2G_INTEROP_BPT_FIRST=1</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The counterpart of <see cref="BothOffers"/> one negotiation later, and for the same reason: a
    /// station that advertises both services never has to reveal what it does with the second one while
    /// our EVCC keeps taking the first. EVerest's <c>EvseManager</c> adds the <c>*_BPT</c> entry whenever
    /// the power supply reports itself bidirectional, and their <c>DCSupplySimulator</c> defaults to
    /// exactly that — so their whole SIL has been advertising a service nothing here ever selected.
    /// </para>
    /// <para>
    /// <b>It used to be spelt <c>V2G_INTEROP_MCS_FIRST=9</c> and could only do MCS</b>, which is why the
    /// run notes up to 2026-08-06 say that. The old spelling is still honoured — a run note records what
    /// was actually typed, and rewriting those to a variable that did not exist at the time would make the
    /// record say something that never happened. The generalisation is the app's
    /// <c>Evcc20Base.PreferBidirectionalService</c>; there is no probe subclass any more.
    /// </para>
    /// </remarks>
    public static Boolean BptFirst()
        => Environment.GetEnvironmentVariable("V2G_INTEROP_BPT_FIRST") == "1" ||
           Environment.GetEnvironmentVariable("V2G_INTEROP_MCS_FIRST") == "9";


    public static (ProtocolVariant Protocol, PowerMode Mode) ProtocolAndMode()
    {

        var requested = Environment.GetEnvironmentVariable("V2G_INTEROP_PROTOCOL");

        // MCS settles the protocol by construction: service ids 8 / 9 exist only in the -20 catalogue, and
        // a "both" offer that a mux routes to its -2 backend would leave the session with nothing to ask
        // for. Refused rather than quietly outranked, because the failure this arm exists to rule out is
        // exactly a run that degrades to plain DC and still gets filed as an MCS result.
        if (Mcs())
            return requested is null or "" or "20"
                       ? (ProtocolVariant.Iso15118_20, PowerMode.Dc)
                       : throw new ArgumentException(
                             $"V2G_INTEROP_MODE=mcs is an ISO 15118-20 session, so V2G_INTEROP_PROTOCOL="
                           + $"'{requested}' cannot be honoured — unset it, or set it to 20.");

        return (requested switch
                {
                    // "both" resolves to -20 here because that is the offer's priority-1 entry — the
                    // protocol everything decided *before* the handshake (TLS profile, recording name)
                    // should assume; the handshake itself then settles what actually runs.
                    "20" or "both" => ProtocolVariant.Iso15118_20,
                    _              => ProtocolVariant.Iso15118_2,
                },
                Environment.GetEnvironmentVariable("V2G_INTEROP_MODE") == "dc"
                    ? PowerMode.Dc
                    : PowerMode.Ac);

    }


    /// <summary>
    /// <c>V2G_INTEROP_PROTOCOL=both</c>: offer -20 at priority 1 and -2 at priority 2 in <b>one</b>
    /// SupportedAppProtocol handshake and run whichever the station picks — the case a multiplexing
    /// station (EVerest's <c>IsoMux</c>) exists for, and the one thing the 2026-08-03 run against it
    /// could not exercise while our EVCC offered exactly the protocol it was constructed for.
    /// </summary>
    public static Boolean OfferBothProtocols()
        => Environment.GetEnvironmentVariable("V2G_INTEROP_PROTOCOL") == "both";


    /// <summary>
    /// The two-entry offer, in the order <c>V2G_INTEROP_SAP_FIRST</c> asks for: <c>20</c> (default) or
    /// <c>2</c> at priority 1.
    /// </summary>
    /// <remarks>
    /// The reversal is the experiment, not a preference. A station that routes a -20-first offer to its
    /// -20 backend has done something consistent with <i>two</i> different rules — "follow the EV's
    /// ranking" and "take the first -20 you can find" — and only an offer that ranks -2 above -20 tells
    /// the two apart. EVerest's <c>IsoMux</c> answers that question by doing the latter
    /// (<c>docs/interop-runs/2026-08-03-everest-isomux-both/</c>).
    /// </remarks>
    /// <summary>
    /// Plug &amp; Charge credentials for the EVCC side: <c>V2G_INTEROP_CONTRACT_CERT</c> is a PKCS#12
    /// holding the contract leaf with its private key plus the MO sub-CAs,
    /// <c>V2G_INTEROP_CONTRACT_PASS</c> its password. Unset (the default) authorizes via EIM.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Same shape as the CLI's <c>--contract-cert</c>: the certificate that carries a private key is
    /// the leaf, every other one goes into <c>SubCertificates</c> in file order.
    /// </para>
    /// <para>
    /// <b>The credential is the counterparty's, deliberately.</b> A station verifies a contract
    /// signature against a chain it trusts, so a self-made one would be refused for a reason that says
    /// nothing about our signing. EVerest ships a complete MO hierarchy in its own test PKI
    /// (<c>tests/ocpp_tests/test_sets/everest-aux/certs/client/mo/</c>) whose root their
    /// <c>EvseSecurity</c> already trusts — so the run needs no key generation at all, only their
    /// throwaway material handed back to them.
    /// </para>
    /// </remarks>
    public static PncEvccOptions? ContractCredentialsOrNull()
    {

        var path = Environment.GetEnvironmentVariable("V2G_INTEROP_CONTRACT_CERT");
        if (String.IsNullOrEmpty(path))
            return null;

        var collection = X509CertificateLoader.LoadPkcs12CollectionFromFile(
                             path, Environment.GetEnvironmentVariable("V2G_INTEROP_CONTRACT_PASS"),
                             X509KeyStorageFlags.Exportable);

        var leaf = collection.FirstOrDefault(c => c.HasPrivateKey)
                       ?? throw new InvalidOperationException(
                              $"V2G_INTEROP_CONTRACT_CERT: no certificate in '{path}' carries a private key.");
        var key  = leaf.GetECDsaPrivateKey()
                       ?? throw new InvalidOperationException(
                              "V2G_INTEROP_CONTRACT_CERT: the contract leaf's private key is not ECDSA.");

        var subCertificates = collection.Where(c => !c.HasPrivateKey).Select(c => c.RawData).ToArray();

        TestContext.Out.WriteLine($"PnC: contract {leaf.Subject} (+{subCertificates.Length} sub-CA(s)), " +
                                  $"{key.KeySize}-bit EC.");

        return new PncEvccOptions(leaf.RawData, subCertificates, key);

    }


    /// <summary>
    /// ISO 15118-2 contract provisioning for the EVCC side. <c>V2G_INTEROP_PROVISION</c> is
    /// <c>install</c> or <c>update</c>; <c>V2G_INTEROP_PROVISION_CERT</c> a PKCS#12 holding the
    /// credential that plays the part — the <b>OEM provisioning</b> certificate for an installation, the
    /// <b>expiring contract</b> for an update — with its private key, and
    /// <c>V2G_INTEROP_PROVISION_PASS</c> its password. An update also needs
    /// <c>V2G_INTEROP_PROVISION_EMAID</c>, which the message carries. Unset (the default) skips
    /// provisioning entirely.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is why no counterparty's `-2` provisioning path had ever been exercised here.</b> The
    /// capability landed in <c>Evcc2.CertInstallRequest</c> on 2026-08-11, and until this switch existed
    /// there was no way to reach it from an interop run — the fixtures threaded
    /// <see cref="ContractCredentialsOrNull"/> and nothing else. Same shape as
    /// <c>Evcc20Base.RequestMeterInfo</c> and <c>SendSessionId</c> before it: a question our car could
    /// technically ask, that no run could tell it to.
    /// </para>
    /// <para>
    /// The key must be <b>P-256</b>. That is not our choice: `-2`'s key transport wraps the issued
    /// contract key for the curve the requesting credential carries, and the schema's field sizes admit
    /// only that one. EVerest's own test PKI ships suitable material.
    /// </para>
    /// </remarks>
    /// <summary>
    /// <c>V2G_INTEROP_RENEGOTIATE=1</c> makes our ISO 15118-2 car trigger one renegotiation of its own
    /// mid-charge — <c>PowerDeliveryReq(Renegotiate)</c>, a fresh ChargeParameterDiscovery, then
    /// <c>PowerDelivery(Start)</c> and on with the loop (`[V2G2-841]`).
    /// </summary>
    /// <remarks>
    /// The car reacts to a station-initiated <c>EVSENotification.ReNegotiation</c> whether or not this is
    /// set; what the switch adds is the <em>EV-initiated</em> direction, which no station will ask for.
    /// <see cref="Evcc2.Renegotiate"/> has existed since the `-2` state machine did and, like the
    /// provisioning switch beside it, was unreachable from an interop run until this line — the fourth
    /// capability this month that our car had and no run could use.
    /// </remarks>
    public static Boolean Renegotiate()
        => Environment.GetEnvironmentVariable("V2G_INTEROP_RENEGOTIATE") == "1";


    public static Iso2CertInstallOptions? CertificateProvisioningOrNull()
    {

        var action = Environment.GetEnvironmentVariable("V2G_INTEROP_PROVISION");
        if (String.IsNullOrEmpty(action))
            return null;

        var which = action.ToLowerInvariant() switch
        {
            "install" => Iso2CertificateAction.Install,
            "update"  => Iso2CertificateAction.Update,
            _         => throw new InvalidOperationException(
                             $"V2G_INTEROP_PROVISION: expected 'install' or 'update', got '{action}'."),
        };

        var path = Environment.GetEnvironmentVariable("V2G_INTEROP_PROVISION_CERT")
                       ?? throw new InvalidOperationException(
                              "V2G_INTEROP_PROVISION is set but V2G_INTEROP_PROVISION_CERT is not: the run "
                            + "needs the credential to sign the request with.");

        var collection = X509CertificateLoader.LoadPkcs12CollectionFromFile(
                             path, Environment.GetEnvironmentVariable("V2G_INTEROP_PROVISION_PASS"),
                             X509KeyStorageFlags.Exportable);

        var leaf = collection.FirstOrDefault(c => c.HasPrivateKey)
                       ?? throw new InvalidOperationException(
                              $"V2G_INTEROP_PROVISION_CERT: no certificate in '{path}' carries a private key.");

        var signKey = leaf.GetECDsaPrivateKey()
                          ?? throw new InvalidOperationException(
                                 "V2G_INTEROP_PROVISION_CERT: the private key is not ECDSA.");

        // The same key twice, in the two shapes the exchange needs it: ECDSA to sign the request, ECDH to
        // unwrap the answer. -2 wraps the issued contract key for the key that asked, which is what makes
        // an update need no other proof of identity.
        var agreement = leaf.GetECDiffieHellmanPrivateKey()
                            ?? throw new InvalidOperationException(
                                   "V2G_INTEROP_PROVISION_CERT: the private key cannot do ECDH.");

        if (signKey.KeySize != 256)
            TestContext.Out.WriteLine(
                $"WARNING: the provisioning key is {signKey.KeySize}-bit; -2 key transport is P-256 only, "
              + "so the response will not be decryptable. Recording the run anyway — the refusal or the "
              + "failure is the measurement.");

        var subCertificates = collection.Where(c => !c.HasPrivateKey).Select(c => c.RawData).ToArray();

        TestContext.Out.WriteLine(
            $"Provisioning: {which} with {leaf.Subject} (+{subCertificates.Length} sub-CA(s)), "
          + $"{signKey.KeySize}-bit EC.");

        return new Iso2CertInstallOptions(
                   leaf.RawData, signKey, agreement, which,
                   Environment.GetEnvironmentVariable("V2G_INTEROP_PROVISION_EMAID"),
                   subCertificates.Length > 0 ? subCertificates : null);

    }


    public static SapOffer[] BothOffers(PowerMode mode)
        => Environment.GetEnvironmentVariable("V2G_INTEROP_SAP_FIRST") == "2"
               ? [new SapOffer(ProtocolVariant.Iso15118_2,  mode), new SapOffer(ProtocolVariant.Iso15118_20, mode)]
               : [new SapOffer(ProtocolVariant.Iso15118_20, mode), new SapOffer(ProtocolVariant.Iso15118_2,  mode)];


    /// <summary>The names the trace corpus uses, so a recorded interop session is filed like any other.
    /// A both-protocol offer is filed as <c>both</c>: the offer is the fact of the run, whichever way
    /// the station decides. An MCS session is filed as <c>mcs</c> rather than <c>dc</c> even though its
    /// messages are DC's — the catalogue it negotiated is the whole content of the run, and a capture
    /// filed under <c>dc</c> would keep the bytes and lose the reason they were recorded.</summary>
    public static (String Protocol, String Mode) ProtocolAndModeNames()
    {
        var (protocol, mode) = ProtocolAndMode();
        return (OfferBothProtocols() ? "both"
                    : protocol == ProtocolVariant.Iso15118_20 ? "iso15118-20" : "iso15118-2",
                Mcs() ? "mcs" : mode == PowerMode.Dc ? "dc" : "ac");
    }


    /// <summary>
    /// TLS for a probe against a third-party station whose version we do not control.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>V2G_INTEROP_TLS=1</c> alone is the permissive probe: any server certificate is accepted and both
    /// 1.2 and 1.3 are offered. That is enough to answer "does a TLS session run at all" and is
    /// deliberately <b>not</b> a conformance path — Josev serves TLS 1.2 unilateral by default and the Rust
    /// simulators bring their own profile.
    /// </para>
    /// <para>
    /// Two variables make it mean more, and a -20 station will need both:
    /// </para>
    /// <list type="bullet">
    ///   <item><c>V2G_INTEROP_TLS_TRUST=&lt;pem&gt;</c> — validate the station's chain against this trust
    ///         anchor instead of accepting it. With it the run proves our EVCC verifies a foreign SECC
    ///         chain; without it, it proves only that bytes flowed.</item>
    ///   <item><c>V2G_INTEROP_TLS_CLIENT=&lt;pfx&gt;[:password]</c> — the TLS client certificate for mutual
    ///         TLS. ISO 15118-20 needs one: EVerest's <c>Evse15118D20</c> switches to
    ///         <c>SSL_VERIFY_FAIL_IF_NO_PEER_CERT</c> the moment the client offers TLS 1.3, and a handshake
    ///         without a client certificate is refused there.</item>
    /// </list>
    /// <para>
    /// Both take file paths rather than material, and nothing here creates a key: the certificates for a
    /// third-party station belong to that station's PKI and are the operator's to provide.
    /// </para>
    /// </remarks>
    public static TlsOptions? DevTlsOrNull(ProtocolVariant protocol)
    {

        if (Environment.GetEnvironmentVariable("V2G_INTEROP_TLS") != "1")
            return null;

        var trustPath  = Environment.GetEnvironmentVariable("V2G_INTEROP_TLS_TRUST");
        var clientSpec = Environment.GetEnvironmentVariable("V2G_INTEROP_TLS_CLIENT");

        RemoteCertificateValidationCallback validation = (_, _, _, _) => true;   // dev default

        if (!String.IsNullOrWhiteSpace(trustPath))
        {
            // A PEM *bundle*, not a single anchor: self-signed certificates in it become trust roots, the
            // rest become intermediates we are willing to supply ourselves. That second half is needed
            // because a station may send only its leaf — EVerest's Evse15118D20 does, so a chain to the V2G
            // root cannot be built from what arrives on the wire (openssl agrees: "unable to get local
            // issuer certificate"). Supplying them here keeps the run going and is recorded as a finding
            // rather than hidden: in the field the SECC is the one that has to send its chain.
            var bundle        = new X509Certificate2Collection();
            bundle.ImportFromPemFile(trustPath);
            var roots         = new X509Certificate2Collection();
            var intermediates = new X509Certificate2Collection();
            foreach (var certificate in bundle)
                (IsSelfSigned(certificate) ? roots : intermediates).Add(certificate);

            validation = (_, certificate, _, _) =>
            {
                if (certificate is null)
                    return false;

                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode      = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.CustomTrustStore.AddRange(roots);
                chain.ChainPolicy.ExtraStore.AddRange(intermediates);
                return chain.Build(new X509Certificate2(certificate));
            };
        }

        X509Certificate2?           clientCertificate = null;
        X509Certificate2Collection? clientChain       = null;
        if (!String.IsNullOrWhiteSpace(clientSpec))
        {
            var separator = clientSpec.LastIndexOf(':');
            var (path, password) = separator > 1
                                       ? (clientSpec[..separator], clientSpec[(separator + 1)..])
                                       : (clientSpec, (String?) null);

            // Everything in the PKCS#12: the one entry with a private key is the leaf, the rest are the
            // intermediates to send with it. Without them a station that holds only the root cannot build
            // the chain — the same trap as the server side, from the other end.
            // Exportable, because our -20 transport hands the key to the BouncyCastle TLS backend (the
            // profile needs secp521r1 and a pinned suite list, which SslStream will not give us). Without
            // the flag macOS parks the key in the keychain and the run dies with a clear but late error.
            var contents      = X509CertificateLoader.LoadPkcs12CollectionFromFile(
                                    path, password, X509KeyStorageFlags.Exportable);
            clientCertificate = contents.FirstOrDefault(c => c.HasPrivateKey)
                                    ?? throw new ArgumentException(
                                           $"V2G_INTEROP_TLS_CLIENT: no private key in '{path}'.");
            clientChain       = new X509Certificate2Collection(
                                    contents.Where(c => !ReferenceEquals(c, clientCertificate)).ToArray());
        }

        return new TlsOptions
        {
            ServerCertificateValidation = validation,
            ClientCertificate           = clientCertificate,
            ClientCertificateChain      = clientChain,
            // Pinned by protocol, as docs/pki-model.md pins it: -2 to TLS 1.2, -20 to TLS 1.3. This used to
            // offer both to both, which is the permissive choice TlsOptions itself warns about — and it cost
            // a run: EVerest's Evse15118D20 under `enforce_tls_1_3` refuses a ClientHello that still allows
            // 1.2 ("tls_early_post_process_client_hello: unsupported protocol"), which arrives on our side as
            // an opaque "bad protocol version". A station being strict about its own profile is not a defect.
            //
            // A *both-protocol* offer is the one case where pinning cannot be right, and a live run is how
            // that surfaced. TLS is settled before SupportedAppProtocol runs — the handshake happens inside
            // it — so "which protocol will this session speak" has no answer yet at the moment the profile
            // has to be chosen. ProtocolAndMode resolves "both" to -20 because that is the offer's
            // priority-1 entry, which is right for naming a recording and wrong for pinning a ClientHello:
            // against EVerest's IsoMux, a TLS-1.3-only hello is refused outright with alert 70, because
            // their multiplexer terminates TLS at the -2 profile (TLS 1.2 only, verified with openssl) no
            // matter which backend it later routes to. So a both-offer offers both, exactly as it does one
            // layer up, and lets the station settle it — see 2026-08-06-everest-isomux-tls.
            EnabledSslProtocols         = OfferBothProtocols()
                                              ? SslProtocols.Tls12 | SslProtocols.Tls13
                                              : protocol == ProtocolVariant.Iso15118_20
                                                    ? SslProtocols.Tls13
                                                    : SslProtocols.Tls12,
            // …and the suites with it, because pki-model.md treats version, suites, signature algorithms
            // and curve as one unit. Pinning them here makes the run assert the profile instead of
            // inheriting it from whichever backend happened to be chosen.
            CipherSuites                = OfferBothProtocols()
                                              ? [.. TlsProfiles.Iso20CipherSuites, .. TlsProfiles.Iso2CipherSuites]
                                              : protocol == ProtocolVariant.Iso15118_20
                                                    ? TlsProfiles.Iso20CipherSuites
                                                    : TlsProfiles.Iso2CipherSuites,
        };

    }


    /// <summary>
    /// The <b>station</b> side of TLS, for a reverse run: <c>V2G_INTEROP_TLS_SERVER=&lt;pfx&gt;[:password]</c>
    /// is the certificate our SECC presents to their car. Null (the default) leaves the listener plaintext.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The counterpart of <see cref="DevTlsOrNull"/>, and it arrived much later for a reason worth stating:
    /// a forward run can accept any certificate the peer offers, so TLS costs one environment variable. In
    /// reverse, <i>we</i> are the one being trusted, and a car checks the chain against its own anchors —
    /// so there is no permissive dev shortcut, and the only material that can work is the counterparty's
    /// own. This takes a path rather than generating anything: their PKI is theirs to issue.
    /// </para>
    /// <para>
    /// For tux-evse that is <c>mkcerts.sh</c>'s <c>_server.pem</c> / <c>_server_key.pem</c> bundled into a
    /// PKCS#12 — the very certificate their own EVSE binding serves, so their EVCC trusts it by
    /// construction rather than by our arrangement.
    /// </para>
    /// <para>
    /// <b>Mutual TLS is offered, not demanded</b>, unless <c>V2G_INTEROP_TLS_REQUIRE_CLIENT=1</c>: their
    /// EVCC config carries a client chain, but a station that <i>insists</i> on one turns "their car does
    /// not send it" into a failed handshake, which reads as a TLS defect rather than as the finding it is.
    /// When required, any client certificate is accepted — this proves the car <i>presented</i> one, which
    /// is the question; validating it against their root is a separate claim and would need their trust
    /// store, not ours.
    /// </para>
    /// </remarks>
    public static TlsOptions? ServerTlsOrNull(ProtocolVariant protocol)
    {

        var spec = Environment.GetEnvironmentVariable("V2G_INTEROP_TLS_SERVER");
        if (String.IsNullOrWhiteSpace(spec))
            return null;

        var separator = spec.LastIndexOf(':');
        var (path, password) = separator > 1
                                   ? (spec[..separator], spec[(separator + 1)..])
                                   : (spec, (String?) null);

        var contents = X509CertificateLoader.LoadPkcs12CollectionFromFile(path, password,
                                                                          X509KeyStorageFlags.Exportable);
        var leaf     = contents.FirstOrDefault(c => c.HasPrivateKey)
                           ?? throw new ArgumentException(
                                  $"V2G_INTEROP_TLS_SERVER: no certificate in '{path}' carries a private key.");
        var chain    = new X509Certificate2Collection(
                           contents.Where(c => !ReferenceEquals(c, leaf)).ToArray());

        var requireClient = Environment.GetEnvironmentVariable("V2G_INTEROP_TLS_REQUIRE_CLIENT") == "1";

        TestContext.Out.WriteLine(
            $"TLS (station): presenting {leaf.Subject} (+{chain.Count} chain certificate(s)), " +
            $"{(protocol == ProtocolVariant.Iso15118_20 ? "TLS 1.3" : "TLS 1.2")}" +
            (requireClient ? ", requiring a client certificate (accept-any)" : ""));

        // Accept-any, and say whose certificate arrived — the peer's identity is the evidence a mutual
        // handshake produces, and reading it here rather than off the SslStream is what makes it
        // backend-independent: the BouncyCastle path hands the peer's DER to exactly this callback and
        // has no SslStream to interrogate afterwards.
        RemoteCertificateValidationCallback? acceptAnyClient = null;
        if (requireClient)
            acceptAnyClient = (_, certificate, _, _) =>
            {
                try
                {
                    TestContext.Progress.WriteLine(
                        $"TLS: their client certificate is {certificate?.Subject ?? "(none)"}.");
                }
                catch
                { }
                return true;
            };

        return new TlsOptions
        {
            ServerCertificate           = leaf,
            ServerCertificateChain      = chain.Count > 0 ? chain : null,
            RequireClientCertificate    = requireClient,
            ClientCertificateValidation = acceptAnyClient,
            // Pinned by protocol exactly as the client side is, and for the same reason: the profile is
            // the protocol's, not the peer's. Their GnuTLS priority string (SECURE128 minus the ancient
            // versions) spans 1.2 and 1.3, so it is our pin that decides which one a -2 session speaks.
            EnabledSslProtocols         = protocol == ProtocolVariant.Iso15118_20
                                              ? SslProtocols.Tls13
                                              : SslProtocols.Tls12,
            CipherSuites                = UnpinCipherSuites()
                                              ? null
                                              : protocol == ProtocolVariant.Iso15118_20
                                                    ? TlsProfiles.Iso20CipherSuites
                                                    : TlsProfiles.Iso2CipherSuites,
        };

    }


    /// <summary>
    /// <c>V2G_INTEROP_TLS_SUITES=platform</c>: negotiate whatever the platform and the peer agree on
    /// instead of the protocol's pinned suite list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A deliberate deviation, spelled as one, because the unpinned state is exactly what
    /// <c>TlsOptions.CipherSuites</c> warns about — and it exists because a run met the case the warning
    /// does not cover: a counterparty whose profile contains <b>none</b> of the standard's suites.
    /// </para>
    /// <para>
    /// tux-evse ship <c>SECURE128:-VERS-SSL3.0:-VERS-TLS1.0:-ARCFOUR-128:+PSK:+DHE-PSK</c> in both their
    /// EVCC and EVSE configs. For ECDHE/ECDSA that GnuTLS list offers AES-GCM, AES-CCM and ChaCha20 —
    /// and neither <c>TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA256</c> nor
    /// <c>TLS_ECDH_ECDSA_WITH_AES_128_CBC_SHA256</c>, which are the two ISO 15118-2 prescribes. Pinned to
    /// the profile, our station and their car share no suite at all and the handshake ends in
    /// <c>no shared cipher</c> — which is the finding, not a setup mistake. Unpinning is how the run gets
    /// past TLS to test everything above it; the conformance claim about suites is then simply not made.
    /// </para>
    /// </remarks>
    public static Boolean UnpinCipherSuites()
        => Environment.GetEnvironmentVariable("V2G_INTEROP_TLS_SUITES") == "platform";


    private static Boolean IsSelfSigned(X509Certificate2 certificate)
        => certificate.SubjectName.RawData.AsSpan().SequenceEqual(certificate.IssuerName.RawData);


    /// <summary>
    /// Writes what the handshake actually settled on into the run output, and — where an ISO 15118-20
    /// offer is about to go out over a connection that may not carry it — says that too.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This line is the fix.</b> On 2026-08-06 this fixture offered both protocols over a connection
    /// that had negotiated TLS 1.2, EVerest's <c>IsoMux</c> selected the <c>-20</c> entry, and 60 exchanges
    /// later the run was written up as a success. `[V2G20-1237]` forbids the car to offer `-20` there and
    /// `[V2G20-2356]` forbids the station to select it; the station's half is filed
    /// (<c>docs/reports/everest-isomux.md</c>, §2) and the car's was ours. Nothing in the
    /// transcript said so, which is why it took two days and the arrival of the requirement text to notice.
    /// </para>
    /// <para>
    /// It <b>reports and proceeds</b> rather than refusing, and returns <see cref="TransportSecurity.Unknown"/>
    /// to stand the rule down in the handshake. Most of this matrix runs <c>-20</c> over plain TCP on
    /// purpose and a fixture that refused would delete it — the defect was never the plain-TCP run, it was
    /// the silence. The two runnable peers (<c>evcc</c>, <c>secc</c>) do the same thing for the same reason.
    /// </para>
    /// </remarks>
    public static TransportSecurity ReportTransport(Stream socket, ProtocolVariant protocol, Boolean offersIso20 = false)
    {

        if (socket is SslStream { IsAuthenticated: true } ssl)
            TestContext.Out.WriteLine(
                $"TLS: {ssl.SslProtocol}, {ssl.NegotiatedCipherSuite}" +
                (ssl.RemoteCertificate is { } certificate ? $", server {certificate.Subject}" : "") +
                (ssl.LocalCertificate is not null ? ", client certificate presented (mutual)" : ""));

        var transport = Iso20Transport.Of(socket);

        if ((protocol == ProtocolVariant.Iso15118_20 || offersIso20) && !Iso20Transport.MayCarryIso20(transport))
        {
            TestContext.Out.WriteLine(
                $"SAP: ISO 15118-20 on {Iso20Transport.Describe(transport)} — [V2G20-1237] (car) and "
              + "[V2G20-2356] (station) both forbid it, Table 5 puts -20 in the TLS 1.3 row alone. "
              + "This run does it anyway, deliberately; the claim it supports is not a conformance claim.");
            return TransportSecurity.Unknown;
        }

        return transport;

    }

}
