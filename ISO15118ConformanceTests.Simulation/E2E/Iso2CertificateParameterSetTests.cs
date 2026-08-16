/*
 * Copyright (c) 2021-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of ISO15118ConformanceTests
 *
 * Licensed under the Affero GPL license, Version 3.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.gnu.org/licenses/agpl.html
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118.Sap;
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso2;
using cloud.charging.open.protocols.ISO15118.Transport;

using ISO15118ConformanceTests.Simulation.Interop;
using ISO15118ConformanceTests.Simulation.Timing;

namespace ISO15118ConformanceTests.Simulation.E2E
{

    /// <summary>
    /// Which parameter set our car names when it selects the certificate service — and the knob that
    /// decouples it from the message, which exists to reach a handler behind a set nobody advertises.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ISO 15118-2's certificate service carries two parameter sets: <b>1 Installation, 2 Update</b>. A car
    /// names the one matching what it is about to send, and that is what our EVCC does by default.
    /// </para>
    /// <para>
    /// <b>Why an override exists.</b> EVerest's <c>EvseV2G</c> advertises **set 1 alone** — Update is an
    /// explicit <c>TODO</c> in <c>ISO15118_chargerImpl.cpp</c> — so a conformant car pairing
    /// <i>Update → 2</i> is answered <c>FAILED_ServiceSelectionInvalid</c> and their
    /// <c>handle_iso_certificate_update</c> is never reached. Their state after a Contract selection is
    /// nonetheless named <c>WAIT_FOR_PAYMENTDETAILS_CERTINST_CERTUPD</c> and admits the message whatever
    /// set was selected, because the state is chosen by the payment option. Naming the set they offer and
    /// sending the other message reaches code their own dispatch says should handle it — see
    /// <c>docs/reports/everest-evsev2g-certificate-update.md</c>.
    /// </para>
    /// <para>
    /// <b>The car is off-profile while it is set, and every run using it has to say so.</b> What the
    /// station does with the message is still the station's; the probe reaches the handler, it does not
    /// manufacture the behaviour.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class Iso2CertificateParameterSetTests
    {

        private static (byte[] Der, ECDsa SignKey, ECDiffieHellman Agreement) OemCredential()
        {
            var key     = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=WMIVIN0000000042", key, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyAgreement, true));

            using var leaf = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
                                                      DateTimeOffset.UtcNow.AddYears(4));

            var parameters = key.ExportParameters(includePrivateParameters: true);
            return (leaf.RawData, ECDsa.Create(parameters), ECDiffieHellman.Create(parameters));
        }

        /// <summary>One provisioning session against our own station, which offers both sets.</summary>
        private static async Task<Secc2> RunProvisioning(Iso2CertificateAction action, short? parameterSet)
        {
            var (oemDer, signKey, agreement) = OemCredential();
            using var _1 = signKey;
            using var _2 = agreement;

            using var cts      = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var s = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(s, ProtocolVariant.Iso15118_2, cts.Token);
                var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System)
                           {
                               OfferCertificateService = true
                           };
                await secc.RunAsync(s, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync(
                                       IPAddress.Loopback.ToString(), listener.LocalEndpoint.Port, ct: cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_2, cts.Token);

            var evcc = new Evcc2(evccStream, PowerMode.Ac, TimeProvider.System,
                                 new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage)
                       {
                           CertInstallRequest = new Iso2CertInstallOptions(
                                                    oemDer, signKey, agreement, action,
                                                    Emaid: action == Iso2CertificateAction.Update
                                                               ? "DE-VAN-C00000009-7"
                                                               : null),
                           CertificateParameterSetId = parameterSet
                       };

            await evcc.RunAsync(cts.Token);
            return await seccTask;
        }

        /// <summary>The conformant pairing, which is what a real car does and every recorded run used.</summary>
        [Test]
        public async Task ByDefaultTheSetFollowsTheAction(
            [Values(Iso2CertificateAction.Install, Iso2CertificateAction.Update)] Iso2CertificateAction action)
        {
            var secc = await RunProvisioning(action, parameterSet: null);

            Assert.Multiple(() =>
            {
                Assert.That(secc.CertificateServiceSelected, Is.True);
                Assert.That(secc.CertificateParameterSetSelected,
                            Is.EqualTo(action == Iso2CertificateAction.Update
                                           ? Secc2.CertificateUpdateParameterSetId
                                           : Secc2.CertificateInstallationParameterSetId));
            });
        }

        /// <summary>
        /// The probe: an <c>Update</c> that names <b>Installation</b>'s set, which is the only way to reach
        /// a handler behind a set the station never advertised — and the station still gets the Update.
        /// </summary>
        [Test]
        public async Task TheOverrideDecouplesTheSetFromTheMessage()
        {
            var secc = await RunProvisioning(Iso2CertificateAction.Update,
                                             parameterSet: Secc2.CertificateInstallationParameterSetId);

            Assert.Multiple(() =>
            {
                Assert.That(secc.CertificateParameterSetSelected,
                            Is.EqualTo(Secc2.CertificateInstallationParameterSetId),
                            "the wire has to carry the set the run named, not the one the action implies");
                Assert.That(secc.CertInstall, Is.Not.Null,
                            "and the station still receives the Update — the two are independent, which is "
                          + "the property the probe rests on");
            });
        }

    }


    /// <summary>The knob's environment reader, and that it reaches the session rather than only parsing.</summary>
    [TestFixture]
    [NonParallelizable]
    public class InteropCertificateParameterSetEnvironmentTests
    {

        private string? saved;

        [SetUp]
        public void Save()
            => saved = Environment.GetEnvironmentVariable("V2G_INTEROP_PROVISION_PARAMSET");

        [TearDown]
        public void Restore()
            => Environment.SetEnvironmentVariable("V2G_INTEROP_PROVISION_PARAMSET", saved);

        [Test]
        public void UnsetKeepsTheConformantPairing()
        {
            Environment.SetEnvironmentVariable("V2G_INTEROP_PROVISION_PARAMSET", null);
            Assert.That(InteropEnvironment.CertificateParameterSetId(), Is.Null);
        }

        [Test]
        public void TheSetIsRead()
        {
            Environment.SetEnvironmentVariable("V2G_INTEROP_PROVISION_PARAMSET", "1");
            Assert.That(InteropEnvironment.CertificateParameterSetId(), Is.EqualTo((short) 1));
        }

        /// <summary>Zero and nonsense are "not asked for" rather than a set nobody defines.</summary>
        [Test]
        public void AnUnusableValueIsNoOverride([Values("0", "-1", "two", "")] string value)
        {
            Environment.SetEnvironmentVariable("V2G_INTEROP_PROVISION_PARAMSET", value);
            Assert.That(InteropEnvironment.CertificateParameterSetId(), Is.Null);
        }

    }

}
