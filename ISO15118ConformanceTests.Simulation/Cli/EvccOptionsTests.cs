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

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118.EVCC;
using cloud.charging.open.protocols.ISO15118.SharedCC;
using cloud.charging.open.protocols.ISO15118.StateMachines;

namespace ISO15118ConformanceTests.Simulation.Cli
{
    /// <summary>Deterministic coverage of the vehicle program's flag parsing.</summary>
    [TestFixture]
    public class EvccOptionsTests
    {

        /// <summary>
        /// A car offers what it speaks and lets the station choose, with -20 first — so the usual
        /// outcome of an unpinned run is a -20 session, and -2 is reached by falling back inside the
        /// same handshake rather than by a second connection. Pinned here because a regression would
        /// turn every unpinned run into a single-protocol car without anything on the wire saying so.
        /// </summary>
        [Test]
        public void Default_OffersBothProtocols_With20First()
        {
            var a = EvccOptions.Parse(["--connect", "127.0.0.1:5555"]);

            Assert.Multiple(() =>
            {
                Assert.That(a.OfferBoth,  Is.True, "offer what you speak, let the station choose");
                Assert.That(a.Protocol,   Is.EqualTo(ProtocolVariant.Iso15118_20), "-20 at priority 1");
                Assert.That(a.TlsStack, Is.EqualTo(TlsStack.None));
            });
        }

        /// <summary>
        /// The mode is the one thing here that is <em>not</em> negotiated — the connector decides it, and
        /// the station must be told the same or the session fails on a message set it did not expect. So
        /// this default is load-bearing in a way the protocol default is not, and it matches the station's.
        /// </summary>
        [Test]
        public void Default_ModeIsDc()
            => Assert.That(EvccOptions.Parse(["--connect", "127.0.0.1:5555"]).Mode, Is.EqualTo(PowerMode.Dc));

        [TestCase("2",    ProtocolVariant.Iso15118_2,  false)]
        [TestCase("20",   ProtocolVariant.Iso15118_20, false)]
        [TestCase("both", ProtocolVariant.Iso15118_20, true)]
        public void Protocol_PinsOrOffersBoth(string value, ProtocolVariant expected, bool both)
        {
            var a = EvccOptions.Parse(["--connect", "127.0.0.1:5555", "--protocol", value]);
            Assert.Multiple(() =>
            {
                Assert.That(a.Protocol,  Is.EqualTo(expected));
                Assert.That(a.OfferBoth, Is.EqualTo(both));
            });
        }

        [Test]
        public void MinimalTcp_Parses()
        {
            var a = EvccOptions.Parse(["--connect", "127.0.0.1:5555", "--protocol", "20", "--mode", "dc"]);

            Assert.Multiple(() =>
            {
                Assert.That(a.ConnectHost, Is.EqualTo("127.0.0.1"));
                Assert.That(a.ConnectPort, Is.EqualTo(5555));
                Assert.That(a.Protocol,    Is.EqualTo(ProtocolVariant.Iso15118_20));
                Assert.That(a.Mode,        Is.EqualTo(PowerMode.Dc));
            });
        }

        [Test]
        public void TlsShorthand_SelectsDotnetBackend()
            => Assert.That(EvccOptions.Parse(["--connect", "127.0.0.1:5555", "--tls"]).TlsStack,
                           Is.EqualTo(TlsStack.Dotnet));

        [TestCase("dotnet",       TlsStack.Dotnet)]
        [TestCase("bc",           TlsStack.BouncyCastle)]
        [TestCase("bouncycastle", TlsStack.BouncyCastle)]
        public void TlsBackend_Parses(string value, TlsStack expected)
        {
            var a = EvccOptions.Parse(["--connect", "127.0.0.1:5555", "--tls-backend", value, "--pki-dir", "/tmp/pki"]);
            Assert.That(a.TlsStack, Is.EqualTo(expected));
        }

        [Test]
        public void BouncyCastleBackend_WithoutAnyCredentials_Throws()
            => Assert.That(() => EvccOptions.Parse(["--connect", "127.0.0.1:5555", "--tls-backend", "bc"]),
                           Throws.ArgumentException.With.Message.Contains("--pki-dir")
                                                   .And.Message.Contains("--vehicle-cert"));

        /// <summary>
        /// The managed backend has two ways to learn who the car is: the dev hierarchy the station minted
        /// (<c>--pki-dir</c>), or a Vehicle chain the caller brings (<c>--vehicle-cert</c>). Either alone is
        /// enough — the second is what makes a run against a foreign station's PKI possible at all.
        /// </summary>
        [Test]
        public void BouncyCastleBackend_AcceptsEitherCredentialSource()
        {
            Assert.That(EvccOptions.Parse(["--connect", "127.0.0.1:5555", "--tls-backend", "bc", "--pki-dir", "/tmp/pki"]).PkiDir,
                        Is.EqualTo("/tmp/pki"));

            var withVehicle = EvccOptions.Parse(["--connect", "127.0.0.1:5555", "--tls-backend", "bc",
                                                 "--vehicle-cert", VehicleCert]);
            Assert.That(withVehicle.VehicleCertPath, Is.EqualTo(VehicleCert));
        }

        /// <summary>
        /// <c>--client-cert</c> was the older spelling of <c>--vehicle-cert</c> and is gone. It named the
        /// wrong thing — "client" is a TLS role, while what the file holds is the car's identity in the V2G
        /// PKI, which -20 also binds a resumed session to. Refused rather than quietly ignored, so a stale
        /// script fails at the first argument instead of running without the certificate it meant to send.
        /// </summary>
        [Test]
        public void ClientCert_IsGone()
            => Assert.That(() => EvccOptions.Parse(["--connect", "127.0.0.1:5555", "--client-cert", VehicleCert]),
                           Throws.ArgumentException.With.Message.Contains("unknown argument"));

        /// <summary>Trust roots may be one certificate or a directory of them — a station accepting cars
        /// from several counterparties needs every one of their roots at once.</summary>
        [Test]
        public void TrustRoots_TakesAFileOrADirectory()
        {
            Assert.That(EvccOptions.Parse(["--connect", "127.0.0.1:5555", "--trust-roots", VehicleCert]).TrustRootsPath,
                        Is.EqualTo(VehicleCert));

            var dir = Path.GetDirectoryName(VehicleCert)!;
            Assert.That(EvccOptions.Parse(["--connect", "127.0.0.1:5555", "--trust-roots", dir]).TrustRootsPath,
                        Is.EqualTo(dir));
        }

        /// <summary>The car's three certificates are three separate options, not one reused.</summary>
        [Test]
        public void VehicleContractAndOem_AreDistinctOptions()
        {
            var a = EvccOptions.Parse(["--connect", "127.0.0.1:5555",
                                       "--vehicle-cert",  VehicleCert,
                                       "--contract-cert", "contract.p12",
                                       "--oem-cert",      VehicleCert]);
            Assert.Multiple(() =>
            {
                Assert.That(a.VehicleCertPath,  Is.EqualTo(VehicleCert));
                Assert.That(a.ContractCertPath, Is.EqualTo("contract.p12"));
                Assert.That(a.OemCertPath,      Is.EqualTo(VehicleCert));
            });
        }

        /// <summary>
        /// A mistyped certificate path used to surface only after the socket was open, as a station that
        /// appeared to hang up. Existence is checked while the message can still name the flag.
        /// </summary>
        [TestCase("--vehicle-cert")]
        [TestCase("--oem-cert")]
        public void MissingCertificateFile_IsRefusedByFlagName(string flag)
            => Assert.That(() => EvccOptions.Parse(["--connect", "127.0.0.1:5555", flag, "/no/such/file.p12"]),
                           Throws.ArgumentException.With.Message.Contains(flag)
                                                   .And.Message.Contains("no such file"));

        /// <summary>Any existing file will do — these tests check parsing, not certificate contents.</summary>
        private static readonly string VehicleCert = typeof(EvccOptionsTests).Assembly.Location;

        [Test]
        public void Slac_Parses_AndRequiresPeer()
        {
            var a = EvccOptions.Parse(["--connect", "127.0.0.1:5555", "--slac", "--slac-peer", "127.0.0.1:15118"]);
            Assert.Multiple(() =>
            {
                Assert.That(a.UseSlac,      Is.True);
                Assert.That(a.SlacPeerHost, Is.EqualTo("127.0.0.1"));
                Assert.That(a.SlacPeerPort, Is.EqualTo(15118));
            });

            Assert.That(() => EvccOptions.Parse(["--connect", "127.0.0.1:5555", "--slac"]),
                        Throws.ArgumentException.With.Message.Contains("--slac-peer"));
        }

        [Test]
        public void Sdp_Parses_AndRequiresInterface()
        {
            var a = EvccOptions.Parse(["--sdp", "--interface", "eth0", "--protocol", "20"]);
            Assert.Multiple(() =>
            {
                Assert.That(a.UseSdp,      Is.True);
                Assert.That(a.Interface,   Is.EqualTo("eth0"));
                Assert.That(a.ConnectHost, Is.Null, "--sdp replaces the fixed --connect endpoint");
            });

            Assert.That(() => EvccOptions.Parse(["--sdp"]),
                        Throws.ArgumentException.With.Message.Contains("--interface"));
        }

        [Test]
        public void WithoutConnectOrSdp_Throws()
            => Assert.That(() => EvccOptions.Parse(["--protocol", "20"]),
                           Throws.ArgumentException.With.Message.Contains("--connect"));

        /// <summary>
        /// The point of splitting the two roles into two programs: a station's flag is not silently
        /// ignored here, it is refused by name. Before the split both roles shared one parser, so
        /// <c>evcc --listen 5555</c> parsed cleanly and did nothing.
        /// </summary>
        [Test]
        public void SeccFlags_AreRefusedByName()
        {
            foreach (var flag in new[] { "--listen", "--dynamic", "--no-pnc", "--server-cert", "--require-client-cert" })
                Assert.That(() => EvccOptions.Parse(["--connect", "127.0.0.1:5555", flag, "x"]),
                            Throws.ArgumentException.With.Message.Contains("unknown argument"),
                            $"the car should not accept the station's {flag}");
        }

        [Test]
        public void UnknownArgument_Throws()
            => Assert.That(() => EvccOptions.Parse(["--connect", "127.0.0.1:5555", "--bogus"]),
                           Throws.ArgumentException.With.Message.Contains("unknown argument"));

        [Test]
        public void IPv6ConnectHost_KeepsItsZone()
        {
            var a = EvccOptions.Parse(["--connect", "[fe80::1%12]:5555"]);
            Assert.Multiple(() =>
            {
                // The zone survives as a number all the way to the socket, which is the whole point:
                // fe80::1 without it does not say which wire to put the packet on.
                Assert.That(a.ConnectHost, Is.EqualTo("fe80::1%12"));
                Assert.That(a.ConnectPort, Is.EqualTo(5555));
            });
        }

        /// <summary>
        /// This used to be accepted and split at the last colon. It cannot be, safely: <c>::1:8080</c> is
        /// itself a valid address (<c>::0.1.128.128</c>), so the split is a guess that is sometimes wrong
        /// and never says so. Every recorded interop run already writes the bracketed form.
        /// </summary>
        [Test]
        public void UnbracketedIPv6ConnectHost_IsRefusedWithTheFormThatWorks()
            => Assert.That(() => EvccOptions.Parse(["--connect", "fe80::1%12:5555"]),
                           Throws.ArgumentException.With.Message.Contains("[fe80::1%12]:5555"));

    }
}
