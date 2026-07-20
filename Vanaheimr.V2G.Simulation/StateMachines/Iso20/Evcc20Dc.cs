using Vanaheimr.V2G.Simulation.Session;
using Vanaheimr.V2G.Simulation.Timing;
using Vanaheimr.V2G.Tp;

using Dc20 = Vanaheimr.V2G.Iso15118_20.DC.Generated;

namespace Vanaheimr.V2G.Simulation.StateMachines.Iso20
{
    /// <summary>EVCC-side DC hooks: charge-parameter discovery, CableCheck+PreCharge, one DC charge-loop iteration, WeldingDetection.</summary>
    public sealed class Evcc20Dc(Stream stream, TimeProvider clock, IAsyncDelay pollDelay, TimeSpan perMessageTimeout)
        : Evcc20Base(stream, clock, pollDelay, perMessageTimeout)
    {
        // NOTE: uses the base class's PollDelay accessor, not the pollDelay parameter above directly,
        // to avoid capturing it twice (once here, once in the base primary constructor).
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

        protected override async Task RunChargeParameterDiscoveryAsync(CancellationToken ct)
        {
            var req = new Dc20.DC_ChargeParameterDiscoveryReq(SessionCtx.ToDcHeader(),
                new Dc20.DC_CPDReqEnergyTransferModeType(
                    EVMaximumChargePower: Rat(5_000, 1), EVMinimumChargePower: Rat(0),
                    EVMaximumChargeCurrent: Rat(200), EVMinimumChargeCurrent: Rat(0),
                    EVMaximumVoltage: Rat(500), EVMinimumVoltage: Rat(50), TargetSOC: 80));

            var (set, message) = await ExchangeRaw(MessageSet.Iso20DC,
                dest => Dc20.DcCodec.TryEncode(req, dest, out int n) ? n : throw EncodeFailed(), ct);
            Expect<Dc20.DC_ChargeParameterDiscoveryRes>(set, message, MessageSet.Iso20DC);
        }

        protected override async Task RunPreChargeSequenceAsync(CancellationToken ct)
        {
            while (true)
            {
                var (set, message) = await ExchangeRaw(MessageSet.Iso20DC,
                    dest => Dc20.DcCodec.TryEncode(new Dc20.DC_CableCheckReq(SessionCtx.ToDcHeader()), dest, out int n) ? n : throw EncodeFailed(), ct);
                var res = Expect<Dc20.DC_CableCheckRes>(set, message, MessageSet.Iso20DC);
                if (res.EVSEProcessing == Dc20.Processing.Finished) break;
                await PollDelay.Wait(PollInterval, ct);
            }

            var preChargeReq = new Dc20.DC_PreChargeReq(SessionCtx.ToDcHeader(), Dc20.Processing.Finished,
                EVPresentVoltage: Rat(0), EVTargetVoltage: Rat(400));
            var (preSet, preMessage) = await ExchangeRaw(MessageSet.Iso20DC,
                dest => Dc20.DcCodec.TryEncode(preChargeReq, dest, out int n) ? n : throw EncodeFailed(), ct);
            Expect<Dc20.DC_PreChargeRes>(preSet, preMessage, MessageSet.Iso20DC);
        }

        protected override async Task RunChargeLoopIterationAsync(CancellationToken ct)
        {
            var req = new Dc20.DC_ChargeLoopReq(SessionCtx.ToDcHeader(), DisplayParameters: null, MeterInfoRequested: false,
                EVPresentVoltage: Rat(400), CLReqControlMode: new Dc20.Scheduled_DC_CLReqControlModeType(
                    null, null, null, EVTargetCurrent: Rat(120), EVTargetVoltage: Rat(400),
                    null, null, null, null, null));

            var (set, message) = await ExchangeRaw(MessageSet.Iso20DC,
                dest => Dc20.DcCodec.TryEncode(req, dest, out int n) ? n : throw EncodeFailed(), ct);
            Expect<Dc20.DC_ChargeLoopRes>(set, message, MessageSet.Iso20DC);
        }

        protected override async Task RunPostChargeSequenceAsync(CancellationToken ct)
        {
            var req = new Dc20.DC_WeldingDetectionReq(SessionCtx.ToDcHeader(), Dc20.Processing.Finished);
            var (set, message) = await ExchangeRaw(MessageSet.Iso20DC,
                dest => Dc20.DcCodec.TryEncode(req, dest, out int n) ? n : throw EncodeFailed(), ct);
            Expect<Dc20.DC_WeldingDetectionRes>(set, message, MessageSet.Iso20DC);
        }

        private static T Expect<T>(MessageSet actualSet, object message, MessageSet expectedSet)
        {
            if (actualSet != expectedSet || message is not T typed)
                throw new SessionAborted($"expected a {typeof(T).Name} on {expectedSet}, got {message.GetType().Name} on {actualSet}.");
            return typed;
        }

        private static Dc20.RationalNumberType Rat(short value, sbyte exponent = 0) => new(exponent, value);
        private static InvalidOperationException EncodeFailed() => new("EXI encode failed (buffer too small?).");
    }
}
