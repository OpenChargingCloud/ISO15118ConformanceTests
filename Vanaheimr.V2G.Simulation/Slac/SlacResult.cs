namespace Vanaheimr.V2G.Simulation.Slac
{
    /// <summary>
    /// The outcome of a completed SLAC pairing: the PLC network credentials both sides agreed on
    /// (<c>NID</c>, 7 bytes; <c>NMK</c>, 16 bytes) that would program the local PLC chip to join the AVLN.
    /// In this loopback simulation the subsequent TCP/TLS session does not consume them — SLAC is the
    /// pairing stage that must simply complete before discovery.
    /// </summary>
    public sealed record SlacResult(byte[] Nid, byte[] Nmk);
}
