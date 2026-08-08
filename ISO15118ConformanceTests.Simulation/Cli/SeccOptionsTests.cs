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

using cloud.charging.open.protocols.ISO15118.SECC;
using cloud.charging.open.protocols.ISO15118.SharedCC;
using cloud.charging.open.protocols.ISO15118.StateMachines;

namespace ISO15118ConformanceTests.Simulation.Cli
{
    /// <summary>Deterministic coverage of the station program's flag parsing.</summary>
    [TestFixture]
    public class SeccOptionsTests
    {

        /// <summary>
        /// The default a station ships with, and the reason it is worth a test of its own: a real SECC
        /// accepts whatever drives up, so this one offers both protocols unless told otherwise, and the
        /// handshake settles each connection. A regression here would silently turn every unpinned run
        /// into a single-protocol station and nothing on the wire would announce it.
        /// </summary>
        [Test]
        public void Default_OffersBothProtocols_OnTheIanaPort()
        {
            var a = SeccOptions.Parse([]);

            Assert.Multiple(() =>
            {
                Assert.That(a.OfferBoth,  Is.True, "a station takes whatever drives up");
                Assert.That(a.Protocol,   Is.EqualTo(ProtocolVariant.Iso15118_20), "-20 is the top preference");
                Assert.That(a.ListenPort, Is.EqualTo(15118), "the IANA-registered V2G port");
                Assert.That(a.TlsStack, Is.EqualTo(TlsStack.None));
            });
        }

        /// <summary>
        /// The mode is the one thing here that is <em>not</em> negotiated — the connector decides it, and
        /// both sides must be told the same or the session fails on a message set the other did not
        /// expect. So the default is load-bearing in a way the protocol default is not, and it is DC
        /// because that is what this station is usually pointed at.
        /// </summary>
        [Test]
        public void Default_ModeIsDc()
            => Assert.That(SeccOptions.Parse([]).Mode, Is.EqualTo(PowerMode.Dc));

        [TestCase("2",    ProtocolVariant.Iso15118_2,  false)]
        [TestCase("20",   ProtocolVariant.Iso15118_20, false)]
        [TestCase("both", ProtocolVariant.Iso15118_20, true)]
        public void Protocol_PinsOrOffersBoth(string value, ProtocolVariant expected, bool both)
        {
            var a = SeccOptions.Parse(["--protocol", value]);
            Assert.Multiple(() =>
            {
                Assert.That(a.Protocol,  Is.EqualTo(expected));
                Assert.That(a.OfferBoth, Is.EqualTo(both));
            });
        }

        [Test]
        public void MinimalListen_Parses()
        {
            var a = SeccOptions.Parse(["--listen", "5555", "--mode", "dc"]);
            Assert.Multiple(() =>
            {
                Assert.That(a.ListenPort, Is.EqualTo(5555));
                Assert.That(a.Mode,       Is.EqualTo(PowerMode.Dc));
            });
        }

        [Test]
        public void TlsShorthand_SelectsDotnetBackend()
            => Assert.That(SeccOptions.Parse(["--listen", "5555", "--tls"]).TlsStack, Is.EqualTo(TlsStack.Dotnet));

        [TestCase("dotnet",       TlsStack.Dotnet)]
        [TestCase("bc",           TlsStack.BouncyCastle)]
        [TestCase("bouncycastle", TlsStack.BouncyCastle)]
        public void TlsBackend_Parses(string value, TlsStack expected)
        {
            var a = SeccOptions.Parse(["--listen", "5555", "--tls-backend", value, "--pki-dir", "/tmp/pki"]);
            Assert.That(a.TlsStack, Is.EqualTo(expected));
        }

        [Test]
        public void BouncyCastleBackend_WithoutPkiDir_Throws()
            => Assert.That(() => SeccOptions.Parse(["--listen", "5555", "--tls-backend", "bc"]),
                           Throws.ArgumentException.With.Message.Contains("--pki-dir"));

        [Test]
        public void Slac_Parses_AndRequiresListenPort()
        {
            var a = SeccOptions.Parse(["--listen", "5555", "--slac", "--slac-listen", "15118"]);
            Assert.Multiple(() =>
            {
                Assert.That(a.UseSlac,        Is.True);
                Assert.That(a.SlacListenPort, Is.EqualTo(15118));
            });

            Assert.That(() => SeccOptions.Parse(["--listen", "5555", "--slac"]),
                        Throws.ArgumentException.With.Message.Contains("--slac-listen"));
        }

        [Test]
        public void Sdp_Parses_AndRequiresInterface()
        {
            var a = SeccOptions.Parse(["--sdp", "--interface", "eth0"]);
            Assert.Multiple(() =>
            {
                Assert.That(a.UseSdp,    Is.True);
                Assert.That(a.Interface, Is.EqualTo("eth0"));
            });

            Assert.That(() => SeccOptions.Parse(["--sdp"]),
                        Throws.ArgumentException.With.Message.Contains("--interface"));
        }

        /// <summary>
        /// The point of splitting the two roles into two programs: a car's flag is not silently ignored
        /// here, it is refused by name. Before the split both roles shared one parser, so
        /// <c>secc --connect …</c> parsed cleanly and did nothing.
        /// </summary>
        [Test]
        public void EvccFlags_AreRefusedByName()
        {
            foreach (var flag in new[] { "--connect", "--contract-cert", "--pause-resume", "--client-cert" })
                Assert.That(() => SeccOptions.Parse([flag, "x"]),
                            Throws.ArgumentException.With.Message.Contains("unknown argument"),
                            $"the station should not accept the car's {flag}");
        }

        [Test]
        public void UnknownArgument_Throws()
            => Assert.That(() => SeccOptions.Parse(["--listen", "5555", "--bogus"]),
                           Throws.ArgumentException.With.Message.Contains("unknown argument"));

        /// <summary>
        /// MCS rides energy-transfer services 8/9, which exist only in -20. Pinning -2 and asking for MCS
        /// is a request that cannot be met, and a session that silently degraded to plain DC is the one
        /// failure an MCS run must not produce. Offering both is fine — the handshake can still reach -20.
        /// </summary>
        [Test]
        public void Mcs_RefusedOnPinned2_AllowedWhenBothAreOffered()
        {
            Assert.That(() => SeccOptions.Parse(["--mode", "mcs", "--protocol", "2"]),
                        Throws.ArgumentException.With.Message.Contains("ISO 15118-20"));

            Assert.That(SeccOptions.Parse(["--mode", "mcs"]).Mcs, Is.True, "the default offers both, so MCS is reachable");
        }

    }
}
