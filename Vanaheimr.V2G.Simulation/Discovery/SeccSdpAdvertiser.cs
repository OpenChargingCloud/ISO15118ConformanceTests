using cloud.charging.open.protocols.ISO15118.SDP.Server;

namespace Vanaheimr.V2G.Simulation.Discovery
{
    /// <summary>
    /// SECC-side counterpart to <see cref="ISeccDiscovery"/>: runs an <see cref="SECC_SDPServer"/> that
    /// answers SDP_Request frames with the SECC's TCP/TLS endpoint. A thin lifetime wrapper (start on
    /// construction-via-<see cref="StartAsync"/>, stop on dispose) so the CLI can advertise while it waits
    /// for the TCP connection.
    /// </summary>
    public sealed class SeccSdpAdvertiser(SECC_SDPServer server) : IAsyncDisposable
    {
        public Task StartAsync(CancellationToken ct = default) => server.Start(ct);

        public ValueTask DisposeAsync() => server.DisposeAsync();
    }
}
