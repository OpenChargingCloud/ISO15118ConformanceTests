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

using ISO15118ConformanceTests.Simulation.TestData;
using cloud.charging.open.protocols.ISO15118.Transport;

using BcCipherSuite = Org.BouncyCastle.Tls.CipherSuite;

namespace ISO15118ConformanceTests.Simulation.E2E
{
    /// <summary>
    /// Guards the two assumptions the TLS profile pinning rests on, neither of which is visible at the point
    /// where a session is configured: that the .NET and BouncyCastle suite lists denote the same suites, and
    /// that a pin the BouncyCastle fallback cannot honour is refused instead of quietly ignored.
    /// </summary>
    [TestFixture]
    public class TlsProfilesTests
    {
        [Test]
        public void Iso20CipherSuites_MatchTheBouncyCastleCodePoints()
        {
            // BcV2GTls.CipherSuites casts TlsProfiles.Iso20CipherSuites straight to int. That is only correct
            // because both enumerations carry the IANA code points — assert it rather than trust it, since a
            // silent mismatch would pin the BouncyCastle backend to whatever suite those numbers happen to name.
            Assert.Multiple(() =>
            {
                Assert.That((int) TlsCipherSuite.TLS_AES_256_GCM_SHA384,
                            Is.EqualTo(BcCipherSuite.TLS_AES_256_GCM_SHA384));
                Assert.That((int) TlsCipherSuite.TLS_CHACHA20_POLY1305_SHA256,
                            Is.EqualTo(BcCipherSuite.TLS_CHACHA20_POLY1305_SHA256));
            });
        }

        [Test]
        public void Iso2AndIso20ProfilesDoNotOverlap()
        {
            // -2 is TLS 1.2/CBC, -20 is TLS 1.3/AEAD: a suite appearing in both lists would mean one of them
            // was mistyped, and the version assertions alone would not catch it.
            Assert.That(TlsProfiles.Iso2CipherSuites.Intersect(TlsProfiles.Iso20CipherSuites), Is.Empty);
        }

        // These two exercise TlsPlatform's TlsOptions -> BcTlsOptions translation, which is ordinary
        // platform-independent code: it is only *reached* through the macOS fallback, but it is callable
        // anywhere. Gating them on the platform would have left the guard covered on one machine of two.
        [Test]
        public void BouncyCastleFallback_RefusesASuitePinItCannotHonour()
        {
            using var cert = TestCertificate.CreateSelfSigned();
            var tls = new TlsOptions
            {
                ServerCertificate   = cert,
                EnabledSslProtocols = SslProtocols.Tls13,
                // Neither profile's pair. This used to be TlsProfiles.Iso2CipherSuites, and it was a fair
                // example until 2026-08-16, when this backend grew ISO 15118-2's TLS 1.2 profile so that
                // our EV could send `trusted_ca_keys` ([V2G2-651]) — which SslStream cannot. The test
                // caught that widening, which is what it is for; updated rather than deleted so the
                // guard it pins stays pinned.
                CipherSuites        = [TlsCipherSuite.TLS_AES_128_GCM_SHA256],
            };

            Assert.That(() => TlsPlatform.ToBcServerOptions(tls),
                        Throws.InstanceOf<NotSupportedException>()
                              .With.Message.Contains("BouncyCastle"));
        }


        /// <summary>And the `-2` pair *is* honourable now — the same translation, the other answer.</summary>
        [Test]
        public void BouncyCastleFallback_AcceptsTheIso2Pin()
        {
            using var cert = TestCertificate.CreateSelfSigned();
            var tls = new TlsOptions
            {
                ServerCertificate   = cert,
                EnabledSslProtocols = SslProtocols.Tls12,
                CipherSuites        = TlsProfiles.Iso2CipherSuites,
            };

            Assert.That(() => TlsPlatform.ToBcServerOptions(tls), Throws.Nothing);
            Assert.That(TlsPlatform.ToBcServerOptions(tls).Iso2Profile, Is.True);
        }

        [Test]
        public void BouncyCastleFallback_AcceptsTheIso20Pin()
        {
            // Also covers BcCredentialBridge end-to-end: TestCertificate imports with
            // X509KeyStorageFlags.Exportable, so the PKCS#8 export the bridge needs succeeds on Windows too.
            using var cert = TestCertificate.CreateSelfSigned();
            var tls = new TlsOptions
            {
                ServerCertificate   = cert,
                EnabledSslProtocols = SslProtocols.Tls13,
                CipherSuites        = TlsProfiles.Iso20CipherSuites,
            };

            Assert.That(() => TlsPlatform.ToBcServerOptions(tls), Throws.Nothing);
        }
    }
}
