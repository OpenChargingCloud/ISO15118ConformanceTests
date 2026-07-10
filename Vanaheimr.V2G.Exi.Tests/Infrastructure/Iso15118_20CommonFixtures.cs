using Vanaheimr.V2G.Iso15118_20.CommonMessages.Generated;

namespace Vanaheimr.V2G.Exi.Tests.Infrastructure;

/// <summary>
/// The fixed ISO 15118-20 CommonMessages messages shared by the cbV2G byte-diff tests. Each is keyed
/// by the same name used in <c>Vectors/Iso15118_20.CommonMessages.vectors.json</c> and in the
/// reference tool <c>tools/cbv2g-ref/main_iso20.c</c> (<c>Common_&lt;name&gt;</c>). Every message uses
/// an all-zero 8-byte SessionID, a fixed TimeStamp and no header signature — exposed as a single
/// <see cref="TryEncode"/> so the test project never needs to reference the generated types directly
/// (CommonMessages/DC/AC all declare their own <c>MessageHeaderType</c>/<c>ResponseCode</c>, which
/// would collide if imported into the same file).
/// </summary>
public static class Iso15118_20CommonFixtures
{
    private static MessageHeaderType Header() => new(SessionID: new byte[8], TimeStamp: 1_700_000_000UL, Signature: null);

    public static bool TryEncode(string vectorName, byte[] dest, out int bytesWritten)
    {
        bytesWritten = 0;
        switch (vectorName)
        {
            case "SessionSetupReq":
                return new SessionSetupReq(Header(), "EVCCID1234567")
                    .TryEncode(dest, out bytesWritten);

            case "SessionSetupRes":
                return new SessionSetupRes(Header(), ResponseCode.OK, "EVSEID1234567")
                    .TryEncode(dest, out bytesWritten);

            case "AuthorizationSetupRes":
                // Exercises InlineChoice (EIM/PnC) and the repeating-list-with-tail
                // (AuthorizationServices -> CertificateInstallationService) together.
                return new AuthorizationSetupRes(
                        Header(), ResponseCode.OK,
                        AuthorizationServices: new[] { Authorization.EIM, Authorization.PnC },
                        CertificateInstallationService: true,
                        EIM_ASResAuthorizationMode: new EIM_ASResAuthorizationModeType(),
                        PnC_ASResAuthorizationMode: null)
                    .TryEncode(dest, out bytesWritten);

            case "ServiceDiscoveryReq":
                return new ServiceDiscoveryReq(Header(), SupportedServiceIDs: null)
                    .TryEncode(dest, out bytesWritten);

            case "ServiceDiscoveryRes":
                return new ServiceDiscoveryRes(
                        Header(), ResponseCode.OK, ServiceRenegotiationSupported: false,
                        new ServiceListType(new[] { new ServiceType(ServiceID: 1, FreeService: true) }),
                        VASList: null)
                    .TryEncode(dest, out bytesWritten);

            case "ServiceDetailReq":
                return new ServiceDetailReq(Header(), ServiceID: 1).TryEncode(dest, out bytesWritten);

            case "ServiceDetailRes":
                return new ServiceDetailRes(
                        Header(), ResponseCode.OK, ServiceID: 1,
                        new ServiceParameterListType(new[]
                        {
                            new ParameterSetType(ParameterSetID: 1, new[]
                            {
                                new ParameterType(Name: "Level", BoolValue: null, ByteValue: null,
                                    ShortValue: null, IntValue: 3, RationalNumber: null, FiniteString: null),
                            }),
                        }))
                    .TryEncode(dest, out bytesWritten);

            case "ServiceSelectionReq":
                return new ServiceSelectionReq(
                        Header(), new SelectedServiceType(ServiceID: 1, ParameterSetID: 1),
                        SelectedVASList: null)
                    .TryEncode(dest, out bytesWritten);

            case "ServiceSelectionRes":
                return new ServiceSelectionRes(Header(), ResponseCode.OK).TryEncode(dest, out bytesWritten);

            case "PowerDeliveryReq":
                return new PowerDeliveryReq(Header(), Processing.Finished, ChargeProgress.Start,
                        EVPowerProfile: null, BPT_ChannelSelection: null)
                    .TryEncode(dest, out bytesWritten);

            case "PowerDeliveryRes":
                return new PowerDeliveryRes(Header(), ResponseCode.OK, EVSEStatus: null)
                    .TryEncode(dest, out bytesWritten);

            case "SessionStopReq":
                return new SessionStopReq(Header(), ChargingSession.Terminate,
                        EVTerminationCode: null, EVTerminationExplanation: null)
                    .TryEncode(dest, out bytesWritten);

            case "SessionStopRes":
                return new SessionStopRes(Header(), ResponseCode.OK).TryEncode(dest, out bytesWritten);

            case "MeteringConfirmationReq":
                // SignedMeteringData: required Id attribute + a required trailing choice
                // (Dynamic_/Scheduled_SMDTControlMode) after a required MeterInfo.
                return new MeteringConfirmationReq(Header(),
                        new SignedMeteringDataType(
                            Id: "ID1", SessionID: new byte[8],
                            MeterInfo: new MeterInfoType(
                                MeterID: "M1", ChargedEnergyReadingWh: 5000,
                                BPT_DischargedEnergyReadingWh: null, CapacitiveEnergyReadingVARh: null,
                                BPT_InductiveEnergyReadingVARh: null, MeterSignature: null,
                                MeterStatus: null, MeterTimestamp: null),
                            Receipt: null,
                            Dynamic_SMDTControlMode: null,
                            Scheduled_SMDTControlMode: new Scheduled_SMDTControlModeType(SelectedScheduleTupleID: 1)))
                    .TryEncode(dest, out bytesWritten);

            case "MeteringConfirmationRes":
                return new MeteringConfirmationRes(Header(), ResponseCode.OK).TryEncode(dest, out bytesWritten);

            case "AuthorizationReq":
                // EIM/PnC required InlineChoice with no preceding optionals (standalone dispatch).
                return new AuthorizationReq(Header(), Authorization.EIM,
                        EIM_AReqAuthorizationMode: new EIM_AReqAuthorizationModeType(),
                        PnC_AReqAuthorizationMode: null)
                    .TryEncode(dest, out bytesWritten);

            case "AuthorizationSetupReq":
                return new AuthorizationSetupReq(Header()).TryEncode(dest, out bytesWritten);

            case "ScheduleExchangeReq":
                return new ScheduleExchangeReq(Header(), MaximumSupportingPoints: 12,
                        Dynamic_SEReqControlMode: new Dynamic_SEReqControlModeType(
                            DepartureTime: 1800, MinimumSOC: null, TargetSOC: null,
                            EVTargetEnergyRequest: new RationalNumberType(3, 20),
                            EVMaximumEnergyRequest: new RationalNumberType(3, 30),
                            EVMinimumEnergyRequest: new RationalNumberType(3, 5),
                            EVMaximumV2XEnergyRequest: null, EVMinimumV2XEnergyRequest: null),
                        Scheduled_SEReqControlMode: null)
                    .TryEncode(dest, out bytesWritten);

            case "ScheduleExchangeRes":
                // Dynamic mode with a present PriceLevelSchedule: exercises the OPTIONAL InlineChoice
                // (Absolute-/PriceLevelSchedule) nested inside the REQUIRED outer InlineChoice
                // (Dynamic_/Scheduled_SEResControlMode).
                return new ScheduleExchangeRes(Header(), ResponseCode.OK, Processing.Finished,
                        GoToPause: null,
                        Dynamic_SEResControlMode: new Dynamic_SEResControlModeType(
                            DepartureTime: null, MinimumSOC: null, TargetSOC: null,
                            AbsolutePriceSchedule: null,
                            PriceLevelSchedule: new PriceLevelScheduleType(
                                Id: null, TimeAnchor: 1_700_000_000UL, PriceScheduleID: 1,
                                PriceScheduleDescription: null, NumberOfPriceLevels: 3,
                                new PriceLevelScheduleEntryListType(new[]
                                {
                                    new PriceLevelScheduleEntryType(Duration: 3600, PriceLevel: 1),
                                }))),
                        Scheduled_SEResControlMode: null)
                    .TryEncode(dest, out bytesWritten);

            case "AuthorizationRes":
                return new AuthorizationRes(Header(), ResponseCode.OK, Processing.Finished)
                    .TryEncode(dest, out bytesWritten);

            case "CertificateInstallationReq":
                return new CertificateInstallationReq(Header(),
                        new SignedCertificateChainType(
                            Id: "OEMCERT1", Certificate: new byte[] { 0xAA, 0xBB, 0xCC }, SubCertificates: null),
                        new ListOfRootCertificateIDsType(
                            new[] { new X509IssuerSerialType(X509IssuerName: "Root CA", X509SerialNumber: 47456) }),
                        MaximumContractCertificateChains: 3,
                        PrioritizedEMAIDs: null)
                    .TryEncode(dest, out bytesWritten);

            case "CertificateInstallationRes":
                return new CertificateInstallationRes(Header(), ResponseCode.OK, Processing.Finished,
                        new CertificateChainType(Certificate: new byte[] { 0x01, 0x02 }, SubCertificates: null),
                        new SignedInstallationDataType(
                            Id: "SID1",
                            ContractCertificateChain: new ContractCertificateChainType(
                                Certificate: new byte[] { 0x03, 0x04 },
                                SubCertificates: new SubCertificatesType(new[] { new byte[] { 0x05 } })),
                            ECDHCurve: EcdhCurve.SECP521,
                            DHPublicKey: new byte[] { 0x06, 0x07 },
                            SECP521_EncryptedPrivateKey: new byte[] { 0x08, 0x09 },
                            X448_EncryptedPrivateKey: null,
                            TPM_EncryptedPrivateKey: null),
                        RemainingContractCertificateChains: 2)
                    .TryEncode(dest, out bytesWritten);

            case "VehicleCheckInReq":
                return new VehicleCheckInReq(Header(), EvCheckInStatus.CheckIn, ParkingMethod.AutoParking,
                        VehicleFrame: 100, DeviceOffset: -50, VehicleTravel: null)
                    .TryEncode(dest, out bytesWritten);

            case "VehicleCheckInRes":
                return new VehicleCheckInRes(Header(), ResponseCode.OK,
                        ParkingSpace: 200, DeviceLocation: null, TargetDistance: 30)
                    .TryEncode(dest, out bytesWritten);

            case "VehicleCheckOutReq":
                return new VehicleCheckOutReq(Header(), EvCheckOutStatus.CheckOut, CheckOutTime: 1_700_000_100UL)
                    .TryEncode(dest, out bytesWritten);

            case "VehicleCheckOutRes":
                return new VehicleCheckOutRes(Header(), ResponseCode.OK, EvseCheckOutStatus.Scheduled)
                    .TryEncode(dest, out bytesWritten);

            default:
                throw new ArgumentException($"no CommonMessages fixture for vector '{vectorName}'");
        }
    }
}
