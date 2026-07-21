using cloud.charging.open.protocols.ISO15118.SLAC.Selection;
using cloud.charging.open.protocols.ISO15118.SLAC.StateMachine;
using cloud.charging.open.protocols.ISO15118.SLAC.Transport;

namespace Vanaheimr.V2G.Simulation.Slac
{
    /// <summary>
    /// EV-side SLAC pairing stage: runs the full <see cref="EvSlacSession"/> matching sequence over the
    /// given transport (a <c>UdpSlacTransport</c> in simulation, an AF_PACKET transport on real hardware)
    /// and returns the negotiated <see cref="SlacResult"/>. This is the front stage of the ISO 15118 flow
    /// (SLAC → SDP → TLS → session).
    /// </summary>
    public sealed class SlacEvStage(ISlacTransport transport, EvSlacOptions options, IEVSESelector? selector = null)
    {
        public async Task<SlacResult> PairAsync(CancellationToken ct = default)
        {
            await using var session = new EvSlacSession(transport, options, selector);
            await transport.StartAsync(ct).ConfigureAwait(false); // begin receiving after the session subscribed
            var result = await session.RunAsync(ct).ConfigureAwait(false);
            return new SlacResult(result.MatchCnf.Nid, result.MatchCnf.Nmk);
        }
    }
}
