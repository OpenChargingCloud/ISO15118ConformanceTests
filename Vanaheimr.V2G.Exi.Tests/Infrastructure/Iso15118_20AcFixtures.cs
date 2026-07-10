using Vanaheimr.V2G.Iso15118_20.AC.Generated;

namespace Vanaheimr.V2G.Exi.Tests.Infrastructure;

/// <summary>The fixed ISO 15118-20 AC messages shared by the cbV2G byte-diff tests
/// (<c>Vectors/Iso15118_20.AC.vectors.json</c>, <c>main_iso20.c</c>'s <c>do_ac</c>).</summary>
public static class Iso15118_20AcFixtures
{
    private static MessageHeaderType Header() => new(SessionID: new byte[8], TimeStamp: 1_700_000_000UL, Signature: null);
    private static RationalNumberType Rational(sbyte exponent, short value) => new(exponent, value);

    public static bool TryEncode(string vectorName, byte[] dest, out int bytesWritten)
    {
        bytesWritten = 0;
        switch (vectorName)
        {
            case "AC_ChargeParameterDiscoveryReq":
                // Exercises the concrete (non-abstract-element) substitution head
                // AC_CPDReqEnergyTransferMode, choosing the base (non-BPT) member.
                return new AC_ChargeParameterDiscoveryReq(
                        Header(),
                        new AC_CPDReqEnergyTransferModeType(
                            EVMaximumChargePower: Rational(0, 11000),
                            EVMaximumChargePower_L2: null,
                            EVMaximumChargePower_L3: null,
                            EVMinimumChargePower: Rational(0, 100),
                            EVMinimumChargePower_L2: null,
                            EVMinimumChargePower_L3: null))
                    .TryEncode(dest, out bytesWritten);

            case "AC_ChargeParameterDiscoveryRes":
                return new AC_ChargeParameterDiscoveryRes(
                        Header(), ResponseCode.OK,
                        new AC_CPDResEnergyTransferModeType(
                            EVSEMaximumChargePower: Rational(0, 22000),
                            EVSEMaximumChargePower_L2: null,
                            EVSEMaximumChargePower_L3: null,
                            EVSEMinimumChargePower: Rational(0, 100),
                            EVSEMinimumChargePower_L2: null,
                            EVSEMinimumChargePower_L3: null,
                            EVSENominalFrequency: Rational(0, 50),
                            MaximumPowerAsymmetry: null,
                            EVSEPowerRampLimitation: null,
                            EVSEPresentActivePower: null,
                            EVSEPresentActivePower_L2: null,
                            EVSEPresentActivePower_L3: null))
                    .TryEncode(dest, out bytesWritten);

            case "AC_ChargeLoopReq":
                // Exercises the transitive substitution's concrete, non-BPT member
                // (Scheduled_AC_CLReqControlMode) for the CLReqControlMode field.
                return new AC_ChargeLoopReq(
                        Header(), DisplayParameters: null, MeterInfoRequested: false,
                        new Scheduled_AC_CLReqControlModeType(
                            EVTargetEnergyRequest: null, EVMaximumEnergyRequest: null, EVMinimumEnergyRequest: null,
                            EVMaximumChargePower: null, EVMaximumChargePower_L2: null, EVMaximumChargePower_L3: null,
                            EVMinimumChargePower: null, EVMinimumChargePower_L2: null, EVMinimumChargePower_L3: null,
                            EVPresentActivePower: Rational(0, 4000), EVPresentActivePower_L2: null, EVPresentActivePower_L3: null,
                            EVPresentReactivePower: null, EVPresentReactivePower_L2: null, EVPresentReactivePower_L3: null))
                    .TryEncode(dest, out bytesWritten);

            case "AC_ChargeLoopRes":
                return new AC_ChargeLoopRes(
                        Header(), ResponseCode.OK,
                        EVSEStatus: null, MeterInfo: null, Receipt: null, EVSETargetFrequency: null,
                        new Scheduled_AC_CLResControlModeType(
                            EVSETargetActivePower: null, EVSETargetActivePower_L2: null, EVSETargetActivePower_L3: null,
                            EVSETargetReactivePower: null, EVSETargetReactivePower_L2: null, EVSETargetReactivePower_L3: null,
                            EVSEPresentActivePower: null, EVSEPresentActivePower_L2: null, EVSEPresentActivePower_L3: null))
                    .TryEncode(dest, out bytesWritten);

            default:
                throw new ArgumentException($"no AC fixture for vector '{vectorName}'");
        }
    }
}
