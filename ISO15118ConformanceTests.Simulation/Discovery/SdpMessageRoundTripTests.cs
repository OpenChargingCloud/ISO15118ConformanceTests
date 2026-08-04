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

using cloud.charging.open.protocols.ISO15118.SDP.Messages;
using cloud.charging.open.protocols.ISO15118.V2GTP;

namespace ISO15118ConformanceTests.Simulation.Discovery
{
    /// <summary>
    /// Deterministic, socket-free coverage of the SDP wire format we rely on: SDP_Request / SDP_Response
    /// encode → V2GTP-wrap → parse → decode round-trips (the real UDP/multicast exchange runs only in
    /// real/CLI runs, see <see cref="Vanaheimr.V2G.Simulation.Discovery.SdpSeccDiscovery"/>).
    /// </summary>
    [TestFixture]
    public class SdpMessageRoundTripTests
    {
        [Test]
        public void SdpRequest_RoundTripsThroughV2GTPFrame()
        {
            var request = new SDP_Request(SDP_Security.TLS, SDP_TransportProtocol.TCP);

            var frame = V2GTP_Frame.Parse(request.EncodeFrame());
            Assert.That(frame.Header.PayloadType, Is.EqualTo(V2GTP_PayloadType.SdpRequest));

            Assert.That(SDP_Request.Decode(frame.Payload.Span), Is.EqualTo(request));
        }

        [Test]
        public void SdpResponse_RoundTripsThroughV2GTPFrame()
        {
            var response = new SDP_Response(
                               SeccIPAddress:     IPAddress.Parse("fe80::2"),
                               SeccPort:          15118,
                               Security:          SDP_Security.TLS,
                               TransportProtocol: SDP_TransportProtocol.TCP);

            var frame = V2GTP_Frame.Parse(response.EncodeFrame());
            Assert.That(frame.Header.PayloadType, Is.EqualTo(V2GTP_PayloadType.SdpResponse));

            var decoded = SDP_Response.Decode(frame.Payload.Span);
            Assert.Multiple(() =>
            {
                Assert.That(decoded.SeccIPAddress, Is.EqualTo(IPAddress.Parse("fe80::2")));
                Assert.That(decoded.SeccPort, Is.EqualTo(15118));
                Assert.That(decoded.Security, Is.EqualTo(SDP_Security.TLS));
                Assert.That(decoded.TransportProtocol, Is.EqualTo(SDP_TransportProtocol.TCP));
            });
        }
    }
}
