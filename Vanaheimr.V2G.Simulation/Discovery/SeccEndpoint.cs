using System.Net;

using cloud.charging.open.protocols.ISO15118.SDP.Messages;

namespace Vanaheimr.V2G.Simulation.Discovery
{
    /// <summary>
    /// The SECC's TCP endpoint an EVCC should connect to, as produced by <see cref="ISeccDiscovery"/>:
    /// address, port, and whether TLS is expected. This is the hand-off point between the discovery
    /// stage (SDP or a fixed endpoint) and <see cref="Transport.TcpV2GClient"/>.
    /// </summary>
    public sealed record SeccEndpoint(IPAddress Address, int Port, bool Tls)
    {
        /// <summary>Address as a string, including the IPv6 scope-id if present (e.g. <c>fe80::1%12</c>).</summary>
        public string Host => Address.ToString();

        /// <summary>
        /// Maps an <see cref="SDP_Response"/> to a <see cref="SeccEndpoint"/>. For a link-local SECC
        /// address without a scope-id, <paramref name="scopeId"/> (the discovery interface index) is
        /// attached so the OS can route the connection back through the same link.
        /// </summary>
        public static SeccEndpoint FromSdp(SDP_Response response, int scopeId = 0)
        {
            var address = response.SeccIPAddress;

            if (scopeId != 0 && address.IsIPv6LinkLocal && address.ScopeId == 0)
                address = new IPAddress(address.GetAddressBytes(), scopeId);

            return new SeccEndpoint(address, response.SeccPort, response.Security == SDP_Security.TLS);
        }
    }
}
