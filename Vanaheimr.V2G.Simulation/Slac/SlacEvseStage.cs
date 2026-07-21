using cloud.charging.open.protocols.ISO15118.SLAC.StateMachine;
using cloud.charging.open.protocols.ISO15118.SLAC.Transport;

namespace Vanaheimr.V2G.Simulation.Slac
{
    /// <summary>
    /// SECC/EVSE-side SLAC pairing stage. <see cref="StartAsync"/> begins listening for a PEV (so the
    /// EVSE is ready before the EV sends its first CM_SLAC_PARM.REQ); <see cref="WaitForMatchAsync"/>
    /// completes when the first PEV finishes matching, yielding the negotiated <see cref="SlacResult"/>.
    /// </summary>
    public sealed class SlacEvseStage(ISlacTransport transport, EvseSlacOptions options) : IAsyncDisposable
    {
        private readonly TaskCompletionSource<SlacResult> _matched = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private EvseSlacListener? _listener;

        /// <summary>Start listening for a PEV. Call this before the EV begins pairing.</summary>
        public async Task StartAsync(CancellationToken ct = default)
        {
            _listener = new EvseSlacListener(transport, () => options);
            _listener.SessionCompleted += (_, e) => _matched.TrySetResult(new SlacResult(e.Result.Nid, e.Result.Nmk));
            _listener.SessionFailed    += (_, e) => _matched.TrySetException(e.Error);

            await _listener.StartAsync(ct).ConfigureAwait(false); // subscribes to transport.FrameReceived
            await transport.StartAsync(ct).ConfigureAwait(false); // begin receiving
        }

        /// <summary>Await the first completed SLAC match.</summary>
        public async Task<SlacResult> WaitForMatchAsync(CancellationToken ct = default)
        {
            using (ct.Register(() => _matched.TrySetCanceled(ct)))
                return await _matched.Task.ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (_listener is not null)
                await _listener.DisposeAsync().ConfigureAwait(false);
        }
    }
}
