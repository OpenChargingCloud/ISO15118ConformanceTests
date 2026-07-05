using Vanaheimr.V2G.Iso15118_2.Generated;

namespace Vanaheimr.V2G.Exi.Tests.Infrastructure;

/// <summary>
/// The fixed ISO 15118-2 messages shared by the round-trip and the cbV2G byte-diff tests. Each is
/// keyed by the same name used in <c>Vectors/Iso15118_2.vectors.json</c> and in the reference tool
/// <c>tools/cbv2g-ref/main_iso2.c</c>, so all three stay in lock-step. Every message uses an
/// all-zero 8-byte SessionID and an otherwise empty header.
/// </summary>
public static class Iso15118_2Fixtures
{
    private static MessageHeaderType Header() =>
        new(SessionID: new byte[8], Notification: null, Signature: null);

    public static readonly IReadOnlyDictionary<string, V2G_Message> ByName = new Dictionary<string, V2G_Message>
    {
        ["SessionSetupReq"] = new V2G_Message(Header(),
            new BodyType(new SessionSetupReqType(EVCCID: new byte[] { 0xAB, 0xCD, 0xEF, 0x01, 0x02, 0x03 }))),

        ["SessionSetupRes_ts"] = new V2G_Message(Header(),
            new BodyType(new SessionSetupResType(ResponseCode.OK_NewSessionEstablished, "DE*ABC*E12345*1", 1_600_000_000L))),

        ["SessionSetupRes_nots"] = new V2G_Message(Header(),
            new BodyType(new SessionSetupResType(ResponseCode.OK, "EVSE1", EVSETimeStamp: null))),

        ["ServiceDiscoveryReq_absent"] = new V2G_Message(Header(),
            new BodyType(new ServiceDiscoveryReqType(ServiceScope: null, ServiceCategory: null))),

        ["ServiceDiscoveryReq_present"] = new V2G_Message(Header(),
            new BodyType(new ServiceDiscoveryReqType("urn:scope:test", ServiceCategory.EVCharging))),

        ["ServiceDiscoveryRes"] = new V2G_Message(Header(),
            new BodyType(new ServiceDiscoveryResType(
                ResponseCode.OK,
                new PaymentOptionListType(new[] { PaymentOption.Contract, PaymentOption.ExternalPayment }),
                new ChargeServiceType(ServiceID: 1, ServiceName: "AC", ServiceCategory.EVCharging,
                    ServiceScope: null, FreeService: true,
                    new SupportedEnergyTransferModeType(new[] { EnergyTransferMode.AC_single_phase_core, EnergyTransferMode.AC_three_phase_core })),
                ServiceList: null))),

        ["SessionStopReq"] = new V2G_Message(Header(),
            new BodyType(new SessionStopReqType(ChargingSession.Terminate))),

        ["SessionStopRes"] = new V2G_Message(Header(),
            new BodyType(new SessionStopResType(ResponseCode.OK))),

        ["CableCheckReq"] = new V2G_Message(Header(),
            new BodyType(new CableCheckReqType(DcEvStatus()))),

        ["CableCheckRes"] = new V2G_Message(Header(),
            new BodyType(new CableCheckResType(ResponseCode.OK, DcEvseStatus(), EVSEProcessing.Ongoing))),

        ["PreChargeReq"] = new V2G_Message(Header(),
            new BodyType(new PreChargeReqType(DcEvStatus(),
                new PhysicalValueType(0, UnitSymbol.V, 400),
                new PhysicalValueType(0, UnitSymbol.A, 10)))),

        ["PreChargeRes"] = new V2G_Message(Header(),
            new BodyType(new PreChargeResType(ResponseCode.OK, DcEvseStatus(),
                new PhysicalValueType(0, UnitSymbol.V, 395)))),

        ["WeldingDetectionReq"] = new V2G_Message(Header(),
            new BodyType(new WeldingDetectionReqType(DcEvStatus()))),

        ["WeldingDetectionRes"] = new V2G_Message(Header(),
            new BodyType(new WeldingDetectionResType(ResponseCode.OK, DcEvseStatus(),
                new PhysicalValueType(0, UnitSymbol.V, 400)))),

        ["PowerDeliveryReq"] = new V2G_Message(Header(),
            new BodyType(new PowerDeliveryReqType(ChargeProgress.Start, SAScheduleTupleID: 1,
                ChargingProfile: null, EVPowerDeliveryParameter: null))),

        ["PowerDeliveryRes"] = new V2G_Message(Header(),
            new BodyType(new PowerDeliveryResType(ResponseCode.OK, DcEvseStatus()))),

        ["ChargingStatusRes"] = new V2G_Message(Header(),
            new BodyType(new ChargingStatusResType(ResponseCode.OK, "EVSE1", SAScheduleTupleID: 1,
                EVSEMaxCurrent: null, MeterInfo: null, ReceiptRequired: null,
                new AC_EVSEStatusType(NotificationMaxDelay: 0, EVSENotification.None, RCD: false)))),

        ["CurrentDemandReq"] = new V2G_Message(Header(),
            new BodyType(new CurrentDemandReqType(DcEvStatus(),
                EVTargetCurrent: new PhysicalValueType(0, UnitSymbol.A, 10),
                EVMaximumVoltageLimit: null, EVMaximumCurrentLimit: null, EVMaximumPowerLimit: null,
                BulkChargingComplete: null, ChargingComplete: false,
                RemainingTimeToFullSoC: null, RemainingTimeToBulkSoC: null,
                EVTargetVoltage: new PhysicalValueType(0, UnitSymbol.V, 400)))),

        ["CurrentDemandRes"] = new V2G_Message(Header(),
            new BodyType(new CurrentDemandResType(ResponseCode.OK, DcEvseStatus(),
                EVSEPresentVoltage: new PhysicalValueType(0, UnitSymbol.V, 395),
                EVSEPresentCurrent: new PhysicalValueType(0, UnitSymbol.A, 10),
                EVSECurrentLimitAchieved: false, EVSEVoltageLimitAchieved: false, EVSEPowerLimitAchieved: false,
                EVSEMaximumVoltageLimit: null, EVSEMaximumCurrentLimit: null, EVSEMaximumPowerLimit: null,
                EVSEID: "EVSE1", SAScheduleTupleID: 1, MeterInfo: null, ReceiptRequired: null))),

        ["ChargeParameterDiscoveryReq"] = new V2G_Message(Header(),
            new BodyType(new ChargeParameterDiscoveryReqType(
                MaxEntriesSAScheduleTuple: null,
                EnergyTransferMode.AC_single_phase_core,
                new AC_EVChargeParameterType(DepartureTime: null,
                    EAmount:      new PhysicalValueType(0, UnitSymbol.Wh, 1000),
                    EVMaxVoltage: new PhysicalValueType(0, UnitSymbol.V, 400),
                    EVMaxCurrent: new PhysicalValueType(0, UnitSymbol.A, 16),
                    EVMinCurrent: new PhysicalValueType(0, UnitSymbol.A, 2))))),

        ["ChargeParameterDiscoveryRes"] = new V2G_Message(Header(),
            new BodyType(new ChargeParameterDiscoveryResType(
                ResponseCode.OK, EVSEProcessing.Finished, SASchedules: null,
                new AC_EVSEChargeParameterType(
                    new AC_EVSEStatusType(NotificationMaxDelay: 0, EVSENotification.None, RCD: false),
                    EVSENominalVoltage: new PhysicalValueType(0, UnitSymbol.V, 230),
                    EVSEMaxCurrent:     new PhysicalValueType(0, UnitSymbol.A, 32))))),
    };

    private static DC_EVStatusType DcEvStatus() =>
        new(EVReady: true, DC_EVErrorCode.NO_ERROR, EVRESSSOC: 50);

    private static DC_EVSEStatusType DcEvseStatus() =>
        new(NotificationMaxDelay: 0, EVSENotification.None, EVSEIsolationStatus: null, DC_EVSEStatusCode.EVSE_Ready);
}
