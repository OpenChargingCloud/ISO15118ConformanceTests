using System.Net;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118.SDP.Client;
using cloud.charging.open.protocols.ISO15118.SDP.Messages;

using Vanaheimr.V2G.Simulation.Discovery;

namespace Vanaheimr.V2G.Simulation.Tests.Discovery
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
