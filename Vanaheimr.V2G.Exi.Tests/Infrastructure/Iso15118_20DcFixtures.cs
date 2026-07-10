using Vanaheimr.V2G.Iso15118_20.DC.Generated;

namespace Vanaheimr.V2G.Exi.Tests.Infrastructure;

/// <summary>The fixed ISO 15118-20 DC messages shared by the cbV2G byte-diff tests
/// (<c>Vectors/Iso15118_20.DC.vectors.json</c>, <c>main_iso20.c</c>'s <c>do_dc</c>).</summary>
public static class Iso15118_20DcFixtures
{
    private static MessageHeaderType Header() => new(SessionID: new byte[8], TimeStamp: 1_700_000_000UL, Signature: null);
    private static RationalNumberType Rational(sbyte exponent, short value) => new(exponent, value);

    public static bool TryEncode(string vectorName, byte[] dest, out int bytesWritten)
    {
        bytesWritten = 0;
        switch (vectorName)
        {
            case "DC_CableCheckReq":
                return new DC_CableCheckReq(Header()).TryEncode(dest, out bytesWritten);

            case "DC_CableCheckRes":
                return new DC_CableCheckRes(Header(), ResponseCode.OK, Processing.Finished)
                    .TryEncode(dest, out bytesWritten);

            case "DC_ChargeParameterDiscoveryReq":
                return new DC_ChargeParameterDiscoveryReq(Header(),
                        new DC_CPDReqEnergyTransferModeType(
                            EVMaximumChargePower: Rational(0, 20000), EVMinimumChargePower: Rational(0, 100),
                            EVMaximumChargeCurrent: Rational(0, 200), EVMinimumChargeCurrent: Rational(0, 1),
                            EVMaximumVoltage: Rational(0, 500), EVMinimumVoltage: Rational(0, 200),
                            TargetSOC: null))
                    .TryEncode(dest, out bytesWritten);

            case "DC_ChargeParameterDiscoveryRes":
                return new DC_ChargeParameterDiscoveryRes(Header(), ResponseCode.OK,
                        new DC_CPDResEnergyTransferModeType(
                            EVSEMaximumChargePower: Rational(1, 15000), EVSEMinimumChargePower: Rational(0, 100),
                            EVSEMaximumChargeCurrent: Rational(0, 200), EVSEMinimumChargeCurrent: Rational(0, 1),
                            EVSEMaximumVoltage: Rational(0, 500), EVSEMinimumVoltage: Rational(0, 200),
                            EVSEPowerRampLimitation: null))
                    .TryEncode(dest, out bytesWritten);

            case "DC_PreChargeReq":
                return new DC_PreChargeReq(Header(), Processing.Finished, Rational(0, 390), Rational(0, 400))
                    .TryEncode(dest, out bytesWritten);

            case "DC_PreChargeRes":
                return new DC_PreChargeRes(Header(), ResponseCode.OK, Rational(0, 395))
                    .TryEncode(dest, out bytesWritten);

            case "DC_ChargeLoopReq":
                // Exercises transitive substitution's concrete, non-BPT member
                // (Scheduled_DC_CLReqControlMode) for the CLReqControlMode field.
                return new DC_ChargeLoopReq(Header(), DisplayParameters: null, MeterInfoRequested: false,
                        Rational(0, 400),
                        new Scheduled_DC_CLReqControlModeType(
                            EVTargetEnergyRequest: null, EVMaximumEnergyRequest: null, EVMinimumEnergyRequest: null,
                            EVTargetCurrent: Rational(0, 120), EVTargetVoltage: Rational(0, 400),
                            EVMaximumChargePower: null, EVMinimumChargePower: null, EVMaximumChargeCurrent: null,
                            EVMaximumVoltage: null, EVMinimumVoltage: null))
                    .TryEncode(dest, out bytesWritten);

            case "DC_ChargeLoopRes":
                return new DC_ChargeLoopRes(Header(), ResponseCode.OK,
                        EVSEStatus: null, MeterInfo: null, Receipt: null,
                        EVSEPresentCurrent: Rational(0, 118), EVSEPresentVoltage: Rational(0, 398),
                        EVSEPowerLimitAchieved: false, EVSECurrentLimitAchieved: false, EVSEVoltageLimitAchieved: false,
                        new Scheduled_DC_CLResControlModeType(
                            EVSEMaximumChargePower: null, EVSEMinimumChargePower: null,
                            EVSEMaximumChargeCurrent: null, EVSEMaximumVoltage: null))
                    .TryEncode(dest, out bytesWritten);

            case "DC_ChargeLoopReq_Dynamic":
                // Exercises the untested Dynamic_DC_CLReqControlMode branch (different
                // event code / bit width than the Scheduled branch above).
                return new DC_ChargeLoopReq(Header(), DisplayParameters: null, MeterInfoRequested: false,
                        Rational(0, 400),
                        new Dynamic_DC_CLReqControlModeType(
                            DepartureTime: null,
                            EVTargetEnergyRequest: Rational(1, 4000), EVMaximumEnergyRequest: Rational(1, 6000),
                            EVMinimumEnergyRequest: Rational(0, 0),
                            EVMaximumChargePower: Rational(0, 20000), EVMinimumChargePower: Rational(0, 100),
                            EVMaximumChargeCurrent: Rational(0, 200),
                            EVMaximumVoltage: Rational(0, 500), EVMinimumVoltage: Rational(0, 200)))
                    .TryEncode(dest, out bytesWritten);

            case "DC_ChargeLoopRes_Dynamic":
                return new DC_ChargeLoopRes(Header(), ResponseCode.OK,
                        EVSEStatus: null, MeterInfo: null, Receipt: null,
                        EVSEPresentCurrent: Rational(0, 118), EVSEPresentVoltage: Rational(0, 398),
                        EVSEPowerLimitAchieved: false, EVSECurrentLimitAchieved: false, EVSEVoltageLimitAchieved: false,
                        new Dynamic_DC_CLResControlModeType(
                            DepartureTime: null, MinimumSOC: null, TargetSOC: null, AckMaxDelay: null,
                            EVSEMaximumChargePower: Rational(0, 19500), EVSEMinimumChargePower: Rational(0, 100),
                            EVSEMaximumChargeCurrent: Rational(0, 195), EVSEMaximumVoltage: Rational(0, 500)))
                    .TryEncode(dest, out bytesWritten);

            case "DC_WeldingDetectionReq":
                return new DC_WeldingDetectionReq(Header(), Processing.Finished).TryEncode(dest, out bytesWritten);

            case "DC_WeldingDetectionRes":
                return new DC_WeldingDetectionRes(Header(), ResponseCode.OK, Rational(0, 5))
                    .TryEncode(dest, out bytesWritten);

            default:
                throw new ArgumentException($"no DC fixture for vector '{vectorName}'");
        }
    }
}
