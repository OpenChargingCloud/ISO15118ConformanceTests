using Vanaheimr.V2G.Iso15118_20.CommonMessages.Generated;
using Vanaheimr.V2G.Simulation.Framing;
using Vanaheimr.V2G.Simulation.Session;
using Vanaheimr.V2G.Simulation.Timing;
using Vanaheimr.V2G.Tp;

namespace Vanaheimr.V2G.Simulation.StateMachines.Iso20
{
    /// <summary>
    /// The EVCC side of an ISO 15118-20 session, shared between AC and DC: drives the CommonMessages
    /// phases directly (EIM only), and calls the <c>protected abstract</c> hooks below for the diverging
    /// middle — implemented by <see cref="Evcc20Dc"/>/<see cref="Evcc20Ac"/>, which know which DC/AC codec
    /// and concrete request/response types their energy-transfer mode actually uses.
    /// </summary>
    public abstract class Evcc20Base(
        Stream stream, TimeProvider clock, IAsyncDelay pollDelay, TimeSpan perMessageTimeout)
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

        protected readonly SessionContext SessionCtx = new(clock);
        protected IAsyncDelay PollDelay => pollDelay;
        private readonly byte[] _buf = new byte[1024];

        public int Exchanges { get; private set; }
        public long BytesOnWire { get; private set; }

        /// <summary>Runs charge-parameter discovery exactly once (no polling — -20's DC/AC CPD response carries no EVSEProcessing field).</summary>
        protected abstract Task RunChargeParameterDiscoveryAsync(CancellationToken ct);
        /// <summary>DC: CableCheck+PreCharge. AC: no-op.</summary>
        protected abstract Task RunPreChargeSequenceAsync(CancellationToken ct);
        /// <summary>One charge-loop request/response (caller loops this a fixed number of times).</summary>
        protected abstract Task RunChargeLoopIterationAsync(CancellationToken ct);
        /// <summary>DC: WeldingDetection. AC: no-op.</summary>
        protected abstract Task RunPostChargeSequenceAsync(CancellationToken ct);

        public async Task RunAsync(CancellationToken ct = default)
        {
            await Exchange<SessionSetupRes>(MessageSet.Iso20CommonMessages,
                dest => new SessionSetupReq(SessionCtx.ToCommonHeader(), "EVCC01").TryEncode(dest, out int n) ? n : throw EncodeFailed(), ct);

            await Exchange<AuthorizationSetupRes>(MessageSet.Iso20CommonMessages,
                dest => new AuthorizationSetupReq(SessionCtx.ToCommonHeader()).TryEncode(dest, out int n) ? n : throw EncodeFailed(), ct);

            while ((await Exchange<AuthorizationRes>(MessageSet.Iso20CommonMessages,
                       dest => new AuthorizationReq(SessionCtx.ToCommonHeader(), Authorization.EIM,
                           new EIM_AReqAuthorizationModeType(), null).TryEncode(dest, out int n) ? n : throw EncodeFailed(), ct))
                   .EVSEProcessing != Processing.Finished)
                await pollDelay.Wait(PollInterval, ct);

            await Exchange<ServiceDiscoveryRes>(MessageSet.Iso20CommonMessages,
                dest => new ServiceDiscoveryReq(SessionCtx.ToCommonHeader(), null).TryEncode(dest, out int n) ? n : throw EncodeFailed(), ct);

            await Exchange<ServiceDetailRes>(MessageSet.Iso20CommonMessages,
                dest => new ServiceDetailReq(SessionCtx.ToCommonHeader(), ServiceID: 1).TryEncode(dest, out int n) ? n : throw EncodeFailed(), ct);

            await Exchange<ServiceSelectionRes>(MessageSet.Iso20CommonMessages,
                dest => new ServiceSelectionReq(SessionCtx.ToCommonHeader(),
                    new SelectedServiceType(ServiceID: 1, ParameterSetID: 1), null).TryEncode(dest, out int n) ? n : throw EncodeFailed(), ct);

            await RunChargeParameterDiscoveryAsync(ct);

            while ((await Exchange<ScheduleExchangeRes>(MessageSet.Iso20CommonMessages,
                       dest => new ScheduleExchangeReq(SessionCtx.ToCommonHeader(), MaximumSupportingPoints: 1,
                           Dynamic_SEReqControlMode: null,
                           Scheduled_SEReqControlMode: new Scheduled_SEReqControlModeType(null, null, null, null, null))
                           .TryEncode(dest, out int n) ? n : throw EncodeFailed(), ct))
                   .EVSEProcessing != Processing.Finished)
                await pollDelay.Wait(PollInterval, ct);

            await RunPreChargeSequenceAsync(ct);

            await Exchange<PowerDeliveryRes>(MessageSet.Iso20CommonMessages,
                dest => new PowerDeliveryReq(SessionCtx.ToCommonHeader(), Processing.Finished, ChargeProgress.Start, null, null)
                    .TryEncode(dest, out int n) ? n : throw EncodeFailed(), ct);

            for (int cycle = 0; cycle < 3; cycle++)
            {
                await RunChargeLoopIterationAsync(ct);
                await pollDelay.Wait(PollInterval, ct);
            }

            await Exchange<PowerDeliveryRes>(MessageSet.Iso20CommonMessages,
                dest => new PowerDeliveryReq(SessionCtx.ToCommonHeader(), Processing.Finished, ChargeProgress.Stop, null, null)
                    .TryEncode(dest, out int n) ? n : throw EncodeFailed(), ct);

            await RunPostChargeSequenceAsync(ct);

            await Exchange<SessionStopRes>(MessageSet.Iso20CommonMessages,
                dest => new SessionStopReq(SessionCtx.ToCommonHeader(), ChargingSession.Terminate, null, null)
                    .TryEncode(dest, out int n) ? n : throw EncodeFailed(), ct);
        }

        /// <summary>
        /// Sends one already-framed request and awaits its reply, enforcing <paramref name="expectedSet"/>
        /// and <see cref="perMessageTimeout"/>. Used directly by the CommonMessages phases above; DC/AC
        /// subclasses call <see cref="ExchangeRaw"/> instead since they need a different result type.
        /// </summary>
        private async Task<TRes> Exchange<TRes>(MessageSet expectedSet, Func<byte[], int> encode, CancellationToken ct)
        {
            var (set, message) = await ExchangeRaw(expectedSet, encode, ct).ConfigureAwait(false);
            if (message is not TRes reply)
                throw new SessionAborted($"expected a {typeof(TRes).Name} on {expectedSet}, got {message.GetType().Name} on {set}.");
            return reply;
        }

        /// <summary>Same as <see cref="Exchange{TRes}"/> but returns the undiscriminated <see cref="MessageSet"/>/object pair — for DC/AC-specific exchanges.</summary>
        protected async Task<(MessageSet Set, object Message)> ExchangeRaw(MessageSet expectedSet, Func<byte[], int> encode, CancellationToken ct)
        {
            int reqLen = encode(_buf);
            var start = clock.GetUtcNow();
            await V2GTPStream.WriteFrameAsync(stream, expectedSet, _buf.AsMemory(0, reqLen), ct).ConfigureAwait(false);
            var (set, message) = await V2GTPStream.ReadFrameAsync(stream, ct).ConfigureAwait(false);
            var elapsed = clock.GetUtcNow() - start;

            if (elapsed > perMessageTimeout)
                throw new SessionAborted($"no response within {perMessageTimeout.TotalMilliseconds:0} ms (took {elapsed.TotalMilliseconds:0} ms).");

            Exchanges++;
            BytesOnWire += V2GTP.HeaderSize + reqLen;
            return (set, message);
        }

        private static InvalidOperationException EncodeFailed() => new("EXI encode failed (buffer too small?).");
    }
}
