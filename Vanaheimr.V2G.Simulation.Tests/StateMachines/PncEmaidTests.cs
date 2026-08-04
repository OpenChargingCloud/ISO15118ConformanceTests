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

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using NUnit.Framework;

using Vanaheimr.V2G.Simulation.Session;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso2;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using Vanaheimr.V2G.Simulation.Tests.Timing;

namespace Vanaheimr.V2G.Simulation.Tests.StateMachines;

/// <summary>
/// The eMAID a contract certificate carries has to be one ISO 15118-2 can send.
/// </summary>
/// <remarks>
/// <para>
/// <c>eMAIDType</c> is <c>xs:string</c> with <c>minLength 14</c> and <c>maxLength 15</c>
/// (<c>V2G_CI_MsgDataTypes.xsd</c>) — country code, provider id, instance, optional check digit.
/// Nothing enforced it: the generated codec does not apply string-length facets, which is reasonable
/// for an EXI encoder that assumes schema-valid input, and means no layer below this one will object.
/// </para>
/// <para>
/// It went unnoticed until Swift got an X.509 reader that checked the length the schema states, and
/// promptly refused this repository's own corpus certificate — whose Common Name was 19 characters
/// and had been travelling in a recorded Plug &amp; Charge session, accepted by all three back ends.
/// </para>
/// <para>
/// The rule is <b>-2's</b>, not a certificate profile's. ISO 15118-20 never sends the eMAID from the
/// certificate — only the chain — so the same credential can be perfectly usable there, and
/// <see cref="Evcc20Base"/> deliberately does not check. An earlier draft of the Swift port refused
/// such a credential outright and was wrong in exactly that way.
/// </para>
/// </remarks>
[TestFixture]
public class PncEmaidTests
{

    private static PncEvccOptions CredentialWithCommonName(string commonName)
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={commonName}", key, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
                                                         DateTimeOffset.UtcNow.AddDays(1));
        return new PncEvccOptions(certificate.RawData, [certificate.RawData], key);
    }

    private static Evcc2 EvccWith(PncEvccOptions credential) =>
        // An empty stream: the check under test runs before any I/O, so if it does not fire, the
        // session fails on the transport instead — a different exception, which is the point.
        new(Stream.Null, PowerMode.Ac, TimeProvider.System, new ImmediateAsyncDelay(),
            LoopbackTimeouts.PerMessage) { Pnc = credential };


    [Test]
    public void ACommonNameTooLongToBeAnEmaidIsRefusedBeforeTheSessionOpens()
    {
        var credential = CredentialWithCommonName("TraceCorpusContract");   // 19 — the original sin

        var aborted = Assert.ThrowsAsync<SessionAborted>(async () => await EvccWith(credential).RunAsync());

        Assert.Multiple(() =>
        {
            Assert.That(aborted!.Message, Does.Contain("19 characters"));
            Assert.That(aborted.Message, Does.Contain("14 or 15"));
        });
    }

    [Test]
    public void ACommonNameTooShortIsRefusedToo()
    {
        var aborted = Assert.ThrowsAsync<SessionAborted>(
            async () => await EvccWith(CredentialWithCommonName("DE8AA1A2B3C4")).RunAsync());   // 12

        Assert.That(aborted!.Message, Does.Contain("12 characters"));
    }

    /// <summary>Both legal lengths pass the check — 14 without a check digit, 15 with one. The
    /// session then fails on the empty transport, which is how we know the check let it through
    /// rather than that nothing ran.</summary>
    [Test]
    public void BothLegalLengthsAreAccepted([Values("DE8AA1A2B3C4D5", "DE8AA1A2B3C4D5X")] string emaid)
    {
        Assert.That(async () => await EvccWith(CredentialWithCommonName(emaid)).RunAsync(),
                    Throws.Exception.Not.TypeOf<SessionAborted>(),
                    "a legal eMAID must survive the credential check and fail on the transport instead");
    }
}
