using Vanaheimr.V2G.Iso15118_20.CommonMessages.Generated;
using Vanaheimr.V2G.Simulation.Framing;
using Vanaheimr.V2G.Simulation.Session;
using Vanaheimr.V2G.Tp;

namespace Vanaheimr.V2G.Simulation.StateMachines.Iso20;

/// <summary>
/// The SECC side of an ISO 15118-20 session, shared between AC and DC: the CommonMessages phases
/// (SessionSetup..ServiceSelection, PowerDelivery, SessionStop — EIM only, no PnC in this slice) live
/// here; the diverging middle (charge-parameter discovery, the DC-only CableCheck/PreCharge sequence,
/// one charge-loop iteration, the DC-only WeldingDetection) is delegated to <see cref="Secc20Dc"/>/
/// <see cref="Secc20Ac"/> via the <c>protected virtual</c> hooks below. Unlike -2, -20's messages
/// interleave <em>three</em> distinct codecs (CommonMessages/DC/AC) within one session — each self
/// contained per <c>Vanaheimr.V2G.Exi.Iso15118_20.*.csproj</c> (no cross-references), so
/// <see cref="Session.SessionContext"/> renders the header type each phase actually needs.
/// </summary>
public abstract class Secc20Base(TimeSpan sequenceTimeout, TimeProvider clock)
{
    protected enum Phase20
    {
        SessionSetup, AuthorizationSetup, Authorization, ServiceDiscovery, ServiceDetail, ServiceSelection,
        ChargeParams, ScheduleExchange, CableCheck, PreCharge, PowerOn, Charging, WeldingDetection, SessionStop, Done,
    }

    protected Phase20 Phase { get; private set; } = Phase20.SessionSetup;
    protected readonly SessionContext SessionCtx = new(clock);
    private DateTimeOffset _lastSeen = clock.GetUtcNow();

    public bool IsDone => Phase == Phase20.Done;

    /// <summary>DC: CableCheck+PreCharge run between ScheduleExchange and PowerDelivery(Start). AC: skipped.</summary>
    protected abstract bool HasPreChargeSequence { get; }
    /// <summary>DC: WeldingDetection runs between PowerDelivery(Stop) and SessionStop. AC: skipped.</summary>
    protected abstract bool HasPostChargeSequence { get; }

    protected abstract (MessageSet Set, object Response) HandleChargeParameterDiscovery(object request);
    protected virtual (MessageSet Set, object Response) HandleCableCheck(object request) =>
        throw new NotSupportedException("CableCheck has no handler for this energy-transfer mode.");
    protected virtual (MessageSet Set, object Response) HandlePreCharge(object request) =>
        throw new NotSupportedException("PreCharge has no handler for this energy-transfer mode.");
    protected abstract (MessageSet Set, object Response) HandleChargeLoop(object request);
    protected virtual (MessageSet Set, object Response) HandleWeldingDetection(object request) =>
        throw new NotSupportedException("WeldingDetection has no handler for this energy-transfer mode.");

    /// <summary>One request in, one response out, and the next phase — the -20 analogue of <see cref="Iso2.Secc2.Handle"/>.</summary>
    public (MessageSet Set, object Response) Handle(MessageSet set, object request)
    {
        var now = clock.GetUtcNow();
        if (Phase is not Phase20.SessionSetup && now - _lastSeen > sequenceTimeout)
            throw new SessionAborted($"SECC sequence timeout: EV silent for > {sequenceTimeout.TotalSeconds:0}s");
        _lastSeen = now;

        var (respSet, response, next) = (Phase, set, request) switch
        {
            (Phase20.SessionSetup, MessageSet.Iso20CommonMessages, SessionSetupReq r) =>
                Step(MessageSet.Iso20CommonMessages, SessionSetup(r), Phase20.AuthorizationSetup),

            (Phase20.AuthorizationSetup, MessageSet.Iso20CommonMessages, AuthorizationSetupReq r) =>
                Step(MessageSet.Iso20CommonMessages, AuthSetup(r), Phase20.Authorization),

            (Phase20.Authorization, MessageSet.Iso20CommonMessages, AuthorizationReq r) =>
                Step(MessageSet.Iso20CommonMessages, Auth(r), Phase20.ServiceDiscovery),

            (Phase20.ServiceDiscovery, MessageSet.Iso20CommonMessages, ServiceDiscoveryReq r) =>
                Step(MessageSet.Iso20CommonMessages, SvcDiscovery(r), Phase20.ServiceDetail),

            (Phase20.ServiceDetail, MessageSet.Iso20CommonMessages, ServiceDetailReq r) =>
                Step(MessageSet.Iso20CommonMessages, SvcDetail(r), Phase20.ServiceSelection),

            (Phase20.ServiceSelection, MessageSet.Iso20CommonMessages, ServiceSelectionReq r) =>
                Step(MessageSet.Iso20CommonMessages, SvcSelection(r), Phase20.ChargeParams),

            (Phase20.ChargeParams, _, _) =>
                Append(HandleChargeParameterDiscovery(request), Phase20.ScheduleExchange),

            (Phase20.ScheduleExchange, MessageSet.Iso20CommonMessages, ScheduleExchangeReq r) =>
                Step(MessageSet.Iso20CommonMessages, ScheduleExchange(r), HasPreChargeSequence ? Phase20.CableCheck : Phase20.PowerOn),

            (Phase20.CableCheck, _, _) when HasPreChargeSequence =>
                Append(HandleCableCheck(request), Phase20.PreCharge),

            (Phase20.PreCharge, _, _) when HasPreChargeSequence =>
                Append(HandlePreCharge(request), Phase20.PowerOn),

            (Phase20.PowerOn, MessageSet.Iso20CommonMessages, PowerDeliveryReq { ChargeProgress: ChargeProgress.Start } r) =>
                Step(MessageSet.Iso20CommonMessages, PowerDelivery(r), Phase20.Charging),

            (Phase20.Charging, MessageSet.Iso20CommonMessages, PowerDeliveryReq { ChargeProgress: ChargeProgress.Stop } r) =>
                Step(MessageSet.Iso20CommonMessages, PowerDelivery(r), HasPostChargeSequence ? Phase20.WeldingDetection : Phase20.SessionStop),

            (Phase20.Charging, _, _) =>
                Append(HandleChargeLoop(request), Phase20.Charging),

            (Phase20.WeldingDetection, _, _) when HasPostChargeSequence =>
                Append(HandleWeldingDetection(request), Phase20.SessionStop),

            (Phase20.SessionStop, MessageSet.Iso20CommonMessages, SessionStopReq r) =>
                Step(MessageSet.Iso20CommonMessages, SessionStop(r), Phase20.Done),

            _ => throw new SessionAborted(
                $"SECC sequence guard: {request.GetType().Name} not allowed in phase {Phase} " +
                "(would be ResponseCode.FAILED_SequenceError)"),
        };

        Phase = next;
        return (respSet, response);
    }

    /// <summary>Reads/handles/replies over <paramref name="stream"/> until the session reaches <see cref="Phase20.Done"/>.</summary>
    public async Task RunAsync(Stream stream, CancellationToken ct = default)
    {
        var buf = new byte[1024];
        while (!IsDone)
        {
            var (set, message) = await V2GTPStream.ReadFrameAsync(stream, ct).ConfigureAwait(false);
            var (replySet, reply) = Handle(set, message);

            int n = EncodeAny(replySet, reply, buf);
            await V2GTPStream.WriteFrameAsync(stream, replySet, buf.AsMemory(0, n), ct).ConfigureAwait(false);
        }
    }

    /// <summary>Encodes a reply of any of the three message sets this project drives — implemented per concrete subclass since only it knows which DC/AC types it produces.</summary>
    protected abstract int EncodeAny(MessageSet set, object message, byte[] dest);

    private static (MessageSet, object, Phase20) Step(MessageSet set, object response, Phase20 next) => (set, response, next);
    private static (MessageSet, object, Phase20) Append((MessageSet Set, object Response) result, Phase20 next) =>
        (result.Set, result.Response, next);

    // ── CommonMessages phase handlers (identical for AC and DC — EIM only) ─
    private SessionSetupRes SessionSetup(SessionSetupReq req)
    {
        SessionCtx.SessionId = System.Security.Cryptography.RandomNumberGenerator.GetBytes(8);
        return new SessionSetupRes(SessionCtx.ToCommonHeader(), ResponseCode.OK_NewSessionEstablished, "DE*ABC*E1");
    }

    private AuthorizationSetupRes AuthSetup(AuthorizationSetupReq req) =>
        new(SessionCtx.ToCommonHeader(), ResponseCode.OK, new[] { Authorization.EIM },
            CertificateInstallationService: false, new EIM_ASResAuthorizationModeType(), PnC_ASResAuthorizationMode: null);

    private AuthorizationRes Auth(AuthorizationReq req) =>
        new(SessionCtx.ToCommonHeader(), ResponseCode.OK, Processing.Finished);

    private ServiceDiscoveryRes SvcDiscovery(ServiceDiscoveryReq req) =>
        new(SessionCtx.ToCommonHeader(), ResponseCode.OK, ServiceRenegotiationSupported: false,
            new ServiceListType(new[] { new ServiceType(ServiceID: 1, FreeService: true) }), VASList: null);

    private ServiceDetailRes SvcDetail(ServiceDetailReq req) =>
        new(SessionCtx.ToCommonHeader(), ResponseCode.OK, req.ServiceID,
            new ServiceParameterListType(new[]
            {
                new ParameterSetType(1, new[]
                {
                    new ParameterType("Connector", null, null, null, IntValue: 1, null, null),
                }),
            }));

    private ServiceSelectionRes SvcSelection(ServiceSelectionReq req) =>
        new(SessionCtx.ToCommonHeader(), ResponseCode.OK);

    private ScheduleExchangeRes ScheduleExchange(ScheduleExchangeReq req)
    {
        var powerSchedule = new PowerScheduleType(TimeAnchor: 0, AvailableEnergy: null, PowerTolerance: null,
            new PowerScheduleEntryListType(new[] { new PowerScheduleEntryType(Duration: 3600, Power: new RationalNumberType(0, 100), null, null) }));
        var scheduleTuple = new ScheduleTupleType(ScheduleTupleID: 1,
            ChargingSchedule: new ChargingScheduleType(powerSchedule, AbsolutePriceSchedule: null, PriceLevelSchedule: null),
            DischargingSchedule: null);

        return new(SessionCtx.ToCommonHeader(), ResponseCode.OK, Processing.Finished, GoToPause: false,
            Dynamic_SEResControlMode: null,
            Scheduled_SEResControlMode: new Scheduled_SEResControlModeType(new[] { scheduleTuple }));
    }

    private PowerDeliveryRes PowerDelivery(PowerDeliveryReq req) =>
        new(SessionCtx.ToCommonHeader(), ResponseCode.OK, EVSEStatus: null);

    private SessionStopRes SessionStop(SessionStopReq req) =>
        new(SessionCtx.ToCommonHeader(), ResponseCode.OK);
}
