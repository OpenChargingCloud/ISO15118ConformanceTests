using System.Net;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118.SDP.Messages;
using cloud.charging.open.protocols.ISO15118.V2GTP;

namespace Vanaheimr.V2G.Simulation.Tests.Discovery
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
