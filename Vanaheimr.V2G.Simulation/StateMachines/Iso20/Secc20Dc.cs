using Vanaheimr.V2G.Iso15118_20.CommonMessages.Generated;
using Vanaheimr.V2G.Tp;

using Dc20 = Vanaheimr.V2G.Iso15118_20.DC.Generated;

namespace Vanaheimr.V2G.Simulation.StateMachines.Iso20
{
    /// <summary>
    /// -20 DC's diverging middle: DC-specific charge-parameter discovery, the CableCheck/PreCharge
    /// sequence, one DC charge-loop iteration, and WeldingDetection. <c>DC.Generated</c> is aliased
    /// (<c>Dc20</c>) rather than imported unqualified because it redeclares <c>ResponseCode</c>/
    /// <c>Processing</c>/<c>RationalNumberType</c>/<c>MessageHeaderType</c>/<c>EVSEStatusType</c> as
    /// distinct CLR types from <c>CommonMessages.Generated</c> — both would otherwise be ambiguous.
    /// </summary>
    public sealed class Secc20Dc(TimeSpan sequenceTimeout, TimeProvider clock) : Secc20Base(sequenceTimeout, clock)
    {
        protected override bool HasPreChargeSequence => true;
        protected override bool HasPostChargeSequence => true;
        protected override ushort EnergyServiceId => 2;   // ISO 15118-20 DC energy-transfer service

        protected override (MessageSet Set, object Response) HandleChargeParameterDiscovery(object request)
        {
            var req = (Dc20.DC_ChargeParameterDiscoveryReq)request;
            var res = new Dc20.DC_ChargeParameterDiscoveryRes(SessionCtx.ToDcHeader(), Dc20.ResponseCode.OK,
                new Dc20.DC_CPDResEnergyTransferModeType(
                    EVSEMaximumChargePower: Rat(5_000, exponent: 1), EVSEMinimumChargePower: Rat(0),
                    EVSEMaximumChargeCurrent: Rat(200), EVSEMinimumChargeCurrent: Rat(0),
                    EVSEMaximumVoltage: Rat(500), EVSEMinimumVoltage: Rat(50),
                    EVSEPowerRampLimitation: null));
            return (MessageSet.Iso20DC, res);
        }

        protected override (MessageSet Set, object Response) HandleCableCheck(object request)
        {
            var req = (Dc20.DC_CableCheckReq)request;
            return (MessageSet.Iso20DC, new Dc20.DC_CableCheckRes(SessionCtx.ToDcHeader(), Dc20.ResponseCode.OK, Dc20.Processing.Finished));
        }

        protected override (MessageSet Set, object Response) HandlePreCharge(object request)
        {
            var req = (Dc20.DC_PreChargeReq)request;
            return (MessageSet.Iso20DC, new Dc20.DC_PreChargeRes(SessionCtx.ToDcHeader(), Dc20.ResponseCode.OK, Rat(390)));
        }

        protected override (MessageSet Set, object Response) HandleChargeLoop(object request)
        {
            var req = (Dc20.DC_ChargeLoopReq)request;
            var res = new Dc20.DC_ChargeLoopRes(SessionCtx.ToDcHeader(), Dc20.ResponseCode.OK,
                EVSEStatus: null, MeterInfo: null, Receipt: null,
                EVSEPresentCurrent: Rat(120), EVSEPresentVoltage: Rat(400),
                EVSEPowerLimitAchieved: false, EVSECurrentLimitAchieved: false, EVSEVoltageLimitAchieved: false,
                CLResControlMode: new Dc20.Scheduled_DC_CLResControlModeType(null, null, null, null));
            return (MessageSet.Iso20DC, res);
        }

        protected override (MessageSet Set, object Response) HandleWeldingDetection(object request)
        {
            var req = (Dc20.DC_WeldingDetectionReq)request;
            return (MessageSet.Iso20DC, new Dc20.DC_WeldingDetectionRes(SessionCtx.ToDcHeader(), Dc20.ResponseCode.OK, Rat(5)));
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
                case Dc20.DC_ChargeParameterDiscoveryRes m:  ok = Dc20.DcCodec.TryEncode(m, dest, out n); break;
                case Dc20.DC_CableCheckRes m:                ok = Dc20.DcCodec.TryEncode(m, dest, out n); break;
                case Dc20.DC_PreChargeRes m:                 ok = Dc20.DcCodec.TryEncode(m, dest, out n); break;
                case Dc20.DC_ChargeLoopRes m:                ok = Dc20.DcCodec.TryEncode(m, dest, out n); break;
                case Dc20.DC_WeldingDetectionRes m:          ok = Dc20.DcCodec.TryEncode(m, dest, out n); break;
                default: throw new InvalidOperationException($"no encoder for {message.GetType().Name}");
            }
            if (!ok) throw new InvalidOperationException("EXI encode failed (buffer too small?).");
            return n;
        }

        private static Dc20.RationalNumberType Rat(short value, sbyte exponent = 0) => new(exponent, value);
    }
}
