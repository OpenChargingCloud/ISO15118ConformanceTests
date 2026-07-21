namespace Vanaheimr.V2G.Simulation.Discovery
{
    /// <summary>
    /// The EVCC-side discovery stage that yields the SECC endpoint to connect to. Implementations:
    /// <see cref="FixedSeccDiscovery"/> (a configured host:port — the slice-1 behaviour) and
    /// <see cref="SdpSeccDiscovery"/> (real SECC Discovery Protocol over UDP/IPv6).
    /// </summary>
    public interface ISeccDiscovery
    {
        Task<SeccEndpoint> DiscoverAsync(CancellationToken ct = default);
    }
}
