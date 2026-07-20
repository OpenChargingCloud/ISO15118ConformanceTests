using Vanaheimr.V2G.Simulation.Session;
using Vanaheimr.V2G.Simulation.Timing;
using Vanaheimr.V2G.Tp;

using Ac20 = Vanaheimr.V2G.Iso15118_20.AC.Generated;

namespace Vanaheimr.V2G.Simulation.StateMachines.Iso20
{
    /// <summary>EVCC-side AC hooks: charge-parameter discovery and one AC charge-loop iteration. No pre-/post-charge sequence (DC-only).</summary>
    public sealed class Evcc20Ac(Stream stream, TimeProvider clock, IAsyncDelay pollDelay, TimeSpan perMessageTimeout)
        : Evcc20Base(stream, clock, pollDelay, perMessageTimeout)
    {
        protected override async Task RunChargeParameterDiscoveryAsync(CancellationToken ct)
        {
            var req = new Ac20.AC_ChargeParameterDiscoveryReq(SessionCtx.ToAcHeader(),
                new Ac20.AC_CPDReqEnergyTransferModeType(
                    EVMaximumChargePower: Rat(2_200, 1), EVMaximumChargePower_L2: null, EVMaximumChargePower_L3: null,
                    EVMinimumChargePower: Rat(0), EVMinimumChargePower_L2: null, EVMinimumChargePower_L3: null));

            var (set, message) = await ExchangeRaw(MessageSet.Iso20AC,
                dest => Ac20.AcCodec.TryEncode(req, dest, out int n) ? n : throw EncodeFailed(), ct);
            Expect<Ac20.AC_ChargeParameterDiscoveryRes>(set, message, MessageSet.Iso20AC);
        }

        protected override Task RunPreChargeSequenceAsync(CancellationToken ct) => Task.CompletedTask;   // AC: not applicable

        protected override async Task RunChargeLoopIterationAsync(CancellationToken ct)
        {
            var req = new Ac20.AC_ChargeLoopReq(SessionCtx.ToAcHeader(), DisplayParameters: null, MeterInfoRequested: false,
                CLReqControlMode: new Ac20.Scheduled_AC_CLReqControlModeType(
                    null, null, null, null, null, null, null, null, null,
                    EVPresentActivePower: Rat(2_200, 1), null, null, null, null, null));

            var (set, message) = await ExchangeRaw(MessageSet.Iso20AC,
                dest => Ac20.AcCodec.TryEncode(req, dest, out int n) ? n : throw EncodeFailed(), ct);
            Expect<Ac20.AC_ChargeLoopRes>(set, message, MessageSet.Iso20AC);
        }

        protected override Task RunPostChargeSequenceAsync(CancellationToken ct) => Task.CompletedTask;   // AC: not applicable

        private static T Expect<T>(MessageSet actualSet, object message, MessageSet expectedSet)
        {
            if (actualSet != expectedSet || message is not T typed)
                throw new SessionAborted($"expected a {typeof(T).Name} on {expectedSet}, got {message.GetType().Name} on {actualSet}.");
            return typed;
        }

        private static Ac20.RationalNumberType Rat(short value, sbyte exponent = 0) => new(exponent, value);
        private static InvalidOperationException EncodeFailed() => new("EXI encode failed (buffer too small?).");
    }
}
