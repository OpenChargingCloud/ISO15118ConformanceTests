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
    };
}
