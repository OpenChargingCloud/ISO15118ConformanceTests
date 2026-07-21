namespace Vanaheimr.V2G.Simulation.Discovery
{
    /// <summary>
    /// Discovery that skips SDP and returns a fixed, explicitly-configured SECC endpoint — the
    /// behaviour of the first simulation slice, now behind the <see cref="ISeccDiscovery"/> seam.
    /// </summary>
    public sealed class FixedSeccDiscovery(SeccEndpoint endpoint) : ISeccDiscovery
    {
        public Task<SeccEndpoint> DiscoverAsync(CancellationToken ct = default) => Task.FromResult(endpoint);
    }
}
