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

using System.Net;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118.SDP.Client;
using cloud.charging.open.protocols.ISO15118.SDP.Messages;

using cloud.charging.open.protocols.ISO15118.Discovery;

namespace ISO15118ConformanceTests.Simulation.Discovery
{
    /// <summary>
    /// Deterministic coverage of the discovery result → <see cref="SeccEndpoint"/> mapping
    /// (<see cref="SdpSeccDiscovery.MapResult"/> and <see cref="SeccEndpoint.FromSdp"/>), including the
    /// link-local scope-id attachment and the reject/timeout failure paths — no sockets involved.
    /// </summary>
    [TestFixture]
    public class SeccDiscoveryMappingTests
    {
        private static SDP_Response Response(string addr, ushort port, SDP_Security security)
            => new(IPAddress.Parse(addr), port, security, SDP_TransportProtocol.TCP);

        [Test]
        public void Success_MapsAddressPortTls_AndAttachesLinkLocalScope()
        {
            var success = new SDP_DiscoverySuccess
            {
                Response       = Response("fe80::1", 15118, SDP_Security.TLS),
                RemoteEndpoint = new IPEndPoint(IPAddress.Parse("fe80::2"), 49152),
                Attempts       = 1,
                Elapsed        = TimeSpan.Zero,
            };

            var endpoint = SdpSeccDiscovery.MapResult(success, scopeId: 12);

            Assert.Multiple(() =>
            {
                Assert.That(endpoint.Port, Is.EqualTo(15118));
                Assert.That(endpoint.Tls, Is.True);
                Assert.That(endpoint.Address.ScopeId, Is.EqualTo(12), "the discovery interface scope must be attached to a link-local address");
            });
        }

        [Test]
        public void FromSdp_GlobalAddress_KeepsNoScope_AndNoTlsMapsToFalse()
        {
            var endpoint = SeccEndpoint.FromSdp(Response("2001:db8::1", 8443, SDP_Security.NoTLS), scopeId: 12);

            Assert.Multiple(() =>
            {
                Assert.That(endpoint.Tls, Is.False);
                Assert.That(endpoint.Address.ScopeId, Is.EqualTo(0));
                Assert.That(endpoint.Port, Is.EqualTo(8443));
            });
        }

        [Test]
        public void Rejected_Throws()
        {
            var rejected = new SDP_DiscoveryRejected
            {
                Attempts          = 3,
                Elapsed           = TimeSpan.FromSeconds(1),
                RejectedResponses = [(Response("fe80::1", 15118, SDP_Security.NoTLS), "no-TLS response rejected")],
            };

            Assert.That(() => SdpSeccDiscovery.MapResult(rejected, 0), Throws.InstanceOf<SeccDiscoveryException>());
        }

        [Test]
        public void Timeout_Throws()
        {
            var timeout = new SDP_DiscoveryTimeout { Attempts = 50, Elapsed = TimeSpan.FromSeconds(60) };

            Assert.That(() => SdpSeccDiscovery.MapResult(timeout, 0), Throws.InstanceOf<SeccDiscoveryException>());
        }
    }
}
