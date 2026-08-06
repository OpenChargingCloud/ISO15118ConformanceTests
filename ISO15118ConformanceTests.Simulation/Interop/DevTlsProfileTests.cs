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

using System.Security.Authentication;

using NUnit.Framework;

using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.Transport;

namespace ISO15118ConformanceTests.Simulation.Interop;

/// <summary>
/// Which TLS profile a probe offers, decided before a byte is sent.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/pki-model.md</c> pins the profile to the protocol — -2 to TLS 1.2, -20 to TLS 1.3, with the
/// suites treated as part of the same unit — and offering both to both is the permissive choice
/// <c>TlsOptions</c> itself warns about. It also cost a run once: <c>Evse15118D20</c> under
/// <c>enforce_tls_1_3</c> refuses a ClientHello that still allows 1.2.
/// </para>
/// <para>
/// <b>A both-protocol offer is the one case where pinning cannot be right</b>, and that is what these
/// tests hold. The TLS handshake finishes <i>before</i> SupportedAppProtocol runs — SAP happens inside it
/// — so at the moment the profile must be chosen there is no answer yet to "which protocol will this
/// session speak". <see cref="InteropEnvironment.ProtocolAndMode"/> resolves <c>both</c> to -20 because
/// that is the offer's priority-1 entry, which is right for naming a recording and wrong for pinning a
/// ClientHello.
/// </para>
/// <para>
/// EVerest's <c>IsoMux</c> is the counterparty that makes it concrete: it terminates TLS at the -2 profile
/// — <b>TLS 1.2 only</b>, verified with <c>openssl s_client</c> — whichever backend it later routes to. A
/// 1.3-only hello is refused with alert 70 before any of that, so the multiplexer's whole reason for
/// existing was unreachable over TLS. See <c>docs/interop-runs/2026-08-06-everest-isomux-tls/</c>.
/// </para>
/// </remarks>
[TestFixture]
[NonParallelizable]   // the profile is configured by environment variables, which are process-wide
public class DevTlsProfileTests
{

    private static readonly String[] Variables =
        ["V2G_INTEROP_TLS", "V2G_INTEROP_PROTOCOL", "V2G_INTEROP_TLS_TRUST", "V2G_INTEROP_TLS_CLIENT"];

    private Dictionary<String, String?> saved = [];

    [SetUp]
    public void RememberTheEnvironment()
    {
        saved = Variables.ToDictionary(v => v, Environment.GetEnvironmentVariable);
        foreach (var v in Variables)
            Environment.SetEnvironmentVariable(v, null);
    }

    [TearDown]
    public void RestoreTheEnvironment()
    {
        foreach (var (name, value) in saved)
            Environment.SetEnvironmentVariable(name, value);
    }


    [Test]
    public void WithoutTheTlsVariable_ThereIsNoProfileAtAll()
        => Assert.That(InteropEnvironment.DevTlsOrNull(ProtocolVariant.Iso15118_20), Is.Null,
                       "TLS is opt-in; a run that did not ask for it must connect in plaintext");


    /// <summary>The pinning that <c>pki-model.md</c> describes, unchanged for a single-protocol run.</summary>
    [TestCase("2",  ProtocolVariant.Iso15118_2,  SslProtocols.Tls12)]
    [TestCase("20", ProtocolVariant.Iso15118_20, SslProtocols.Tls13)]
    public void ASingleProtocolOffer_PinsItsOwnProfile(String requested, ProtocolVariant protocol,
                                                       SslProtocols expected)
    {

        Environment.SetEnvironmentVariable("V2G_INTEROP_TLS", "1");
        Environment.SetEnvironmentVariable("V2G_INTEROP_PROTOCOL", requested);

        var options = InteropEnvironment.DevTlsOrNull(protocol);

        Assert.That(options,                      Is.Not.Null);
        Assert.That(options!.EnabledSslProtocols, Is.EqualTo(expected));
        Assert.That(options.CipherSuites,
                    Is.EqualTo(protocol == ProtocolVariant.Iso15118_20
                                   ? TlsProfiles.Iso20CipherSuites
                                   : TlsProfiles.Iso2CipherSuites),
                    "version and suites are one unit, so they have to move together");

    }


    /// <summary>
    /// A both-protocol offer offers both TLS versions, exactly as it offers both application protocols
    /// one layer up, and lets the station settle it.
    /// </summary>
    /// <remarks>
    /// The regression this exists for is silent, which is why it is worth a test: pinned to 1.3, the offer
    /// is refused by a -2-era multiplexer at the TLS layer and never reaches SAP at all — and the failure
    /// surfaces as an opaque handshake error, not as "this station speaks a different profile".
    /// </remarks>
    [Test]
    public void ABothProtocolOffer_OffersBothProfiles()
    {

        Environment.SetEnvironmentVariable("V2G_INTEROP_TLS", "1");
        Environment.SetEnvironmentVariable("V2G_INTEROP_PROTOCOL", "both");

        // The protocol argument is what ProtocolAndMode resolves "both" to — priority 1, i.e. -20. The
        // point is that it must *not* decide the profile here.
        var options = InteropEnvironment.DevTlsOrNull(ProtocolVariant.Iso15118_20);

        Assert.That(options,                      Is.Not.Null);
        Assert.That(options!.EnabledSslProtocols, Is.EqualTo(SslProtocols.Tls12 | SslProtocols.Tls13));
        Assert.That(options.CipherSuites,         Is.SupersetOf(TlsProfiles.Iso2CipherSuites));
        Assert.That(options.CipherSuites,         Is.SupersetOf(TlsProfiles.Iso20CipherSuites),
                    "a station that answers either protocol may pick either profile's suites");

    }

}
