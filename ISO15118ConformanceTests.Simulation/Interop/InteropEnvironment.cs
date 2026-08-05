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

using Vanaheimr.V2G.Simulation.Sap;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using Vanaheimr.V2G.Simulation.Transport;

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
///   <item><term><c>V2G_INTEROP_PROTOCOL</c></term><description><c>2</c> (default), <c>20</c>, or
///   <c>both</c> — one handshake offering -20 at priority 1 and -2 at priority 2, running whichever
///   the station picks (EVCC side; see <see cref="OfferBothProtocols"/>).</description></item>
///   <item><term><c>V2G_INTEROP_MODE</c></term><description><c>ac</c> (default), <c>dc</c>, or
///         <c>mcs</c> — the Megawatt Charging System, which is the DC session under a different service
///         catalogue and implies -20 (see <see cref="Mcs"/>).</description></item>
///   <item><term><c>V2G_INTEROP_MCS_FIRST</c></term><description><c>9</c> to rank MCS_BPT ahead of MCS
///         in the EVCC's request (see <see cref="McsBptFirst"/>).</description></item>
///   <item><term><c>V2G_INTEROP_TLS</c></term><description><c>1</c> to run TLS, accepting any server
///         certificate. Development only.</description></item>
///   <item><term><c>V2G_INTEROP_TLS_TRUST</c></term><description>a PEM trust anchor — validate their
///         chain against it instead of accepting anything.</description></item>
///   <item><term><c>V2G_INTEROP_TLS_CLIENT</c></term><description><c>&lt;pfx&gt;[:password]</c> — our TLS
///         client certificate, which a -20 station requires.</description></item>
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
    /// MCS only: rank <b>MCS_BPT (9)</b> ahead of <b>MCS (8)</b> in what our EVCC asks for.
    /// <c>V2G_INTEROP_MCS_FIRST=9</c>.
    /// </summary>
    /// <remarks>
    /// The counterpart of <see cref="BothOffers"/> one negotiation later, and for the same reason: a
    /// station that advertises both services never has to reveal what it does with the second one while
    /// our EVCC keeps taking the first. EVerest's <c>config-sil-mcs.yaml</c> carries both — their
    /// <c>EvseManager</c> adds <c>MCS_BPT</c> whenever the power supply reports itself bidirectional, and
    /// their <c>DCSupplySimulator</c> defaults to exactly that — so 9 is one variable away and was
    /// untested by the 2026-08-05 run for no better reason than ordering.
    /// <para>
    /// See <see cref="McsBptFirstEvcc"/> for what selecting 9 does and does not make bidirectional.
    /// </para>
    /// </remarks>
    public static Boolean McsBptFirst()
        => Environment.GetEnvironmentVariable("V2G_INTEROP_MCS_FIRST") == "9";


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
            EnabledSslProtocols         = protocol == ProtocolVariant.Iso15118_20
                                              ? SslProtocols.Tls13
                                              : SslProtocols.Tls12,
            // …and the suites with it, because pki-model.md treats version, suites, signature algorithms
            // and curve as one unit. Pinning them here makes the run assert the profile instead of
            // inheriting it from whichever backend happened to be chosen.
            CipherSuites                = protocol == ProtocolVariant.Iso15118_20
                                              ? TlsProfiles.Iso20CipherSuites
                                              : TlsProfiles.Iso2CipherSuites,
        };

    }


    private static Boolean IsSelfSigned(X509Certificate2 certificate)
        => certificate.SubjectName.RawData.AsSpan().SequenceEqual(certificate.IssuerName.RawData);

}
