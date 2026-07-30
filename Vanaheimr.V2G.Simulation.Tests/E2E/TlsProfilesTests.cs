using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

using NUnit.Framework;

using Vanaheimr.V2G.Simulation.Tests.TestData;
using Vanaheimr.V2G.Simulation.Transport;

using BcCipherSuite = Org.BouncyCastle.Tls.CipherSuite;

namespace Vanaheimr.V2G.Simulation.Tests.E2E
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
                CipherSuites        = TlsProfiles.Iso2CipherSuites,   // -2 suites on a TLS 1.3 session
            };

            Assert.That(() => TlsPlatform.ToBcServerOptions(tls),
                        Throws.InstanceOf<NotSupportedException>()
                              .With.Message.Contains("BouncyCastle"));
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
