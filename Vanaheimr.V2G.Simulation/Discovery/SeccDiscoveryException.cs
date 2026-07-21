namespace Vanaheimr.V2G.Simulation.Discovery
{
    /// <summary>Thrown when SDP discovery does not yield a usable SECC endpoint (rejected or timed out).</summary>
    public sealed class SeccDiscoveryException(string message) : Exception(message);
}
