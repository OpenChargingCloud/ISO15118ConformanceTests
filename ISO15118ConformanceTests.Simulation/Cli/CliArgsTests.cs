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

using NUnit.Framework;

using Vanaheimr.V2G.Simulation.Cli;
using Vanaheimr.V2G.Simulation.StateMachines;

namespace ISO15118ConformanceTests.Simulation.Cli
{
    /// <summary>Deterministic coverage of the CLI flag parsing, including the new stage/backend flags.</summary>
    [TestFixture]
    public class CliArgsTests
    {
        [Test]
        public void Evcc_MinimalTcp_Parses()
        {
            var a = CliArgs.Parse(["evcc", "--connect", "127.0.0.1:5555", "--protocol", "20", "--mode", "dc"]);

            Assert.Multiple(() =>
            {
                Assert.That(a.Role, Is.EqualTo(Role.Evcc));
                Assert.That(a.ConnectHost, Is.EqualTo("127.0.0.1"));
                Assert.That(a.ConnectPort, Is.EqualTo(5555));
                Assert.That(a.Protocol, Is.EqualTo(ProtocolVariant.Iso15118_20));
                Assert.That(a.Mode, Is.EqualTo(PowerMode.Dc));
                Assert.That(a.TlsBackend, Is.EqualTo(TlsBackend.None));
            });
        }

        [Test]
        public void TlsShorthand_SelectsDotnetBackend()
            => Assert.That(CliArgs.Parse(["secc", "--listen", "5555", "--tls"]).TlsBackend, Is.EqualTo(TlsBackend.Dotnet));

        [TestCase("dotnet", TlsBackend.Dotnet)]
        [TestCase("bc",     TlsBackend.BouncyCastle)]
        [TestCase("bouncycastle", TlsBackend.BouncyCastle)]
        public void TlsBackend_Parses(string value, TlsBackend expected)
        {
            var a = CliArgs.Parse(["secc", "--listen", "5555", "--tls-backend", value, "--pki-dir", "/tmp/pki"]);
            Assert.That(a.TlsBackend, Is.EqualTo(expected));
        }

        [Test]
        public void BouncyCastleBackend_WithoutPkiDir_Throws()
            => Assert.That(() => CliArgs.Parse(["secc", "--listen", "5555", "--tls-backend", "bc"]),
                           Throws.ArgumentException.With.Message.Contains("--pki-dir"));

        [Test]
        public void Slac_Secc_Parses_AndRequiresListenPort()
        {
            var a = CliArgs.Parse(["secc", "--listen", "5555", "--slac", "--slac-listen", "15118"]);
            Assert.Multiple(() =>
            {
                Assert.That(a.UseSlac, Is.True);
                Assert.That(a.SlacListenPort, Is.EqualTo(15118));
            });

            Assert.That(() => CliArgs.Parse(["secc", "--listen", "5555", "--slac"]),
                        Throws.ArgumentException.With.Message.Contains("--slac-listen"));
        }

        [Test]
        public void Slac_Evcc_Parses_AndRequiresPeer()
        {
            var a = CliArgs.Parse(["evcc", "--connect", "127.0.0.1:5555", "--slac", "--slac-peer", "127.0.0.1:15118"]);
            Assert.Multiple(() =>
            {
                Assert.That(a.UseSlac, Is.True);
                Assert.That(a.SlacPeerHost, Is.EqualTo("127.0.0.1"));
                Assert.That(a.SlacPeerPort, Is.EqualTo(15118));
            });

            Assert.That(() => CliArgs.Parse(["evcc", "--connect", "127.0.0.1:5555", "--slac"]),
                        Throws.ArgumentException.With.Message.Contains("--slac-peer"));
        }

        [Test]
        public void Sdp_Parses_AndRequiresInterface()
        {
            var a = CliArgs.Parse(["evcc", "--sdp", "--interface", "eth0", "--protocol", "20"]);
            Assert.Multiple(() =>
            {
                Assert.That(a.UseSdp, Is.True);
                Assert.That(a.Interface, Is.EqualTo("eth0"));
                Assert.That(a.ConnectHost, Is.Null, "--sdp replaces the fixed --connect endpoint");
            });

            Assert.That(() => CliArgs.Parse(["evcc", "--sdp"]),
                        Throws.ArgumentException.With.Message.Contains("--interface"));
        }

        [Test]
        public void Evcc_WithoutConnectOrSdp_Throws()
            => Assert.That(() => CliArgs.Parse(["evcc", "--protocol", "20"]),
                           Throws.ArgumentException.With.Message.Contains("--connect"));

        [Test]
        public void UnknownArgument_Throws()
            => Assert.That(() => CliArgs.Parse(["secc", "--listen", "5555", "--bogus"]),
                           Throws.ArgumentException.With.Message.Contains("unknown argument"));

        [Test]
        public void IPv6ConnectHost_KeepsItsZone()
        {
            var a = CliArgs.Parse(["evcc", "--connect", "[fe80::1%12]:5555"]);
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
            => Assert.That(() => CliArgs.Parse(["evcc", "--connect", "fe80::1%12:5555"]),
                           Throws.ArgumentException.With.Message.Contains("[fe80::1%12]:5555"));
    }
}
