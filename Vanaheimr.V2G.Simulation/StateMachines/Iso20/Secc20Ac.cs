using Vanaheimr.V2G.Iso15118_20.CommonMessages.Generated;
using Vanaheimr.V2G.Tp;

using Ac20 = Vanaheimr.V2G.Iso15118_20.AC.Generated;

namespace Vanaheimr.V2G.Simulation.StateMachines.Iso20
{
    /// <summary>
    /// -20 AC's diverging middle: AC-specific charge-parameter discovery and one AC charge-loop
    /// iteration. No CableCheck/PreCharge/WeldingDetection (DC-only). <c>AC.Generated</c> is aliased
    /// (<c>Ac20</c>) for the same reason as <see cref="Secc20Dc"/> — it redeclares
    /// <c>ResponseCode</c>/<c>Processing</c>/<c>RationalNumberType</c>/etc. as distinct CLR types.
    /// </summary>
    public sealed class Secc20Ac(TimeSpan sequenceTimeout, TimeProvider clock) : Secc20Base(sequenceTimeout, clock)
    {
        protected override bool HasPreChargeSequence => false;
        protected override bool HasPostChargeSequence => false;
        protected override IReadOnlyList<ushort> EnergyServiceIds => new ushort[] { 1, 5 };   // AC + AC_BPT

        protected override (MessageSet Set, object Response) HandleChargeParameterDiscovery(object request)
        {
            var req = (Ac20.AC_ChargeParameterDiscoveryReq)request;
            // Bidirectional (BPT) EV → advertise discharge power too; else the charge-only mode.
            Ac20.AC_CPDResEnergyTransferModeType mode = req.AC_CPDReqEnergyTransferMode is Ac20.BPT_AC_CPDReqEnergyTransferModeType
                ? new Ac20.BPT_AC_CPDResEnergyTransferModeType(
                    EVSEMaximumChargePower: Rat(2_200, exponent: 1), EVSEMaximumChargePower_L2: null, EVSEMaximumChargePower_L3: null,
                    EVSEMinimumChargePower: Rat(0), EVSEMinimumChargePower_L2: null, EVSEMinimumChargePower_L3: null,
                    EVSENominalFrequency: Rat(50), MaximumPowerAsymmetry: null, EVSEPowerRampLimitation: null,
                    EVSEPresentActivePower: null, EVSEPresentActivePower_L2: null, EVSEPresentActivePower_L3: null,
                    EVSEMaximumDischargePower: Rat(2_200, exponent: 1), EVSEMaximumDischargePower_L2: null, EVSEMaximumDischargePower_L3: null,
                    EVSEMinimumDischargePower: Rat(0), EVSEMinimumDischargePower_L2: null, EVSEMinimumDischargePower_L3: null)
                : new Ac20.AC_CPDResEnergyTransferModeType(
                    EVSEMaximumChargePower: Rat(2_200, exponent: 1), EVSEMaximumChargePower_L2: null, EVSEMaximumChargePower_L3: null,
                    EVSEMinimumChargePower: Rat(0), EVSEMinimumChargePower_L2: null, EVSEMinimumChargePower_L3: null,
                    EVSENominalFrequency: Rat(50), MaximumPowerAsymmetry: null, EVSEPowerRampLimitation: null,
                    EVSEPresentActivePower: null, EVSEPresentActivePower_L2: null, EVSEPresentActivePower_L3: null);
            return (MessageSet.Iso20AC, new Ac20.AC_ChargeParameterDiscoveryRes(SessionCtx.ToAcHeader(), Ac20.ResponseCode.OK, mode));
        }

        protected override (MessageSet Set, object Response) HandleChargeLoop(object request)
        {
            var req = (Ac20.AC_ChargeLoopReq)request;
            // Match the EV's control mode: a BPT (bidirectional) EV sends a BPT_* control mode → reply in kind.
            Ac20.CLResControlModeType clRes = req.CLReqControlMode is Ac20.BPT_Scheduled_AC_CLReqControlModeType or Ac20.BPT_Dynamic_AC_CLReqControlModeType
                ? new Ac20.BPT_Scheduled_AC_CLResControlModeType(null, null, null, null, null, null, null, null, null)
                : new Ac20.Scheduled_AC_CLResControlModeType(null, null, null, null, null, null, null, null, null);
            var res = new Ac20.AC_ChargeLoopRes(SessionCtx.ToAcHeader(), Ac20.ResponseCode.OK,
                EVSEStatus: null, MeterInfo: null, Receipt: null, EVSETargetFrequency: null,
                CLResControlMode: clRes);
            return (MessageSet.Iso20AC, res);
        }

        protected override int EncodeAny(MessageSet set, object message, byte[] dest)
        {
            bool ok; int n;
            switch (message)
            {
                case SessionSetupRes m:                    ok = m.TryEncode(dest, out n); break;
                case AuthorizationSetupRes m:               ok = m.TryEncode(dest, out n); break;
                case AuthorizationRes m:                    ok = m.TryEncode(dest, out n); break;
                case ServiceDiscoveryRes m:                 ok = m.TryEncode(dest, out n); break;
                case ServiceDetailRes m:                    ok = m.TryEncode(dest, out n); break;
                case ServiceSelectionRes m:                 ok = m.TryEncode(dest, out n); break;
                case ScheduleExchangeRes m:                 ok = m.TryEncode(dest, out n); break;
                case PowerDeliveryRes m:                    ok = m.TryEncode(dest, out n); break;
                case SessionStopRes m:                      ok = m.TryEncode(dest, out n); break;
                case Ac20.AC_ChargeParameterDiscoveryRes m:  ok = Ac20.AcCodec.TryEncode(m, dest, out n); break;
                case Ac20.AC_ChargeLoopRes m:                ok = Ac20.AcCodec.TryEncode(m, dest, out n); break;
                default: throw new InvalidOperationException($"no encoder for {message.GetType().Name}");
            }
            if (!ok) throw new InvalidOperationException("EXI encode failed (buffer too small?).");
            return n;
        }

        private static Ac20.RationalNumberType Rat(short value, sbyte exponent = 0) => new(exponent, value);
    }
}
