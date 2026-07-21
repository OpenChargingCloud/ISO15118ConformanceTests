using System.Security.Cryptography;

using Vanaheimr.V2G.Iso15118_2;
using Vanaheimr.V2G.Iso15118_2.Generated;
using Vanaheimr.V2G.Simulation.Framing;
using Vanaheimr.V2G.Simulation.Session;
using Vanaheimr.V2G.Tp;

namespace Vanaheimr.V2G.Simulation.StateMachines.Iso2
{
    /// <summary>
    /// The charge point (SECC) side of an ISO 15118-2 session — a <b>sequence-guarded</b> responder. It
    /// advances through the charging state machine and only accepts the request expected next; anything
    /// out of order raises <see cref="SessionAborted"/> (a real SECC would answer
    /// <c>ResponseCode.FAILED_SequenceError</c> and close). It also enforces the SECC
    /// <i>sequence timeout</i>: if the EV goes quiet mid-session for too long, the session is torn down.
    /// <see cref="Handle"/> is a pure, synchronous state transition — directly unit-testable without a
    /// socket; <see cref="RunAsync"/> is the thin loop that drives it from a real <see cref="Stream"/>.
    /// </summary>
    public sealed class Secc2(PowerMode mode, TimeSpan sequenceTimeout, TimeProvider clock)
    {
        private enum Phase
        {
            SessionSetup, ServiceDiscovery, PaymentSelection, Authorization, ChargeParams,
            CableCheck, PreCharge, PowerOn, Charging, WeldingDetection, SessionStop, Done,
        }

        private Phase _phase = Phase.SessionSetup;
        private byte[] _sessionId = new byte[8];
        private DateTimeOffset _lastSeen = clock.GetUtcNow();

        /// <summary>True once the session has reached its terminal (post-SessionStop) phase.</summary>
        public bool IsDone => _phase == Phase.Done;

        public V2G_Message Handle(V2G_Message request)
        {
            var now = clock.GetUtcNow();
            if (_phase is not Phase.SessionSetup && now - _lastSeen > sequenceTimeout)
                throw new SessionAborted($"SECC sequence timeout: EV silent for > {sequenceTimeout.TotalSeconds:0}s");
            _lastSeen = now;

            var (body, next) = Dispatch(request.Body.BodyElement!);
            _phase = next;
            return new V2G_Message(new MessageHeaderType(_sessionId, Notification: null, Signature: null), new BodyType(body));
        }

        /// <summary>Reads/handles/replies over <paramref name="stream"/> until the session reaches <see cref="Phase.Done"/>.</summary>
        public async Task RunAsync(Stream stream, CancellationToken ct = default)
        {
            var buf = new byte[512];
            while (!IsDone)
            {
                var (set, message) = await V2GTPStream.ReadFrameAsync(stream, ct).ConfigureAwait(false);
                if (set != MessageSet.Iso15118_2 || message is not V2G_Message request)
                    throw new SessionAborted($"SECC: expected an ISO 15118-2 frame, got {set}.");

                var reply = Handle(request);
                if (!reply.TryEncode(buf, out int n))
                    throw new InvalidOperationException("EXI encode failed (buffer too small?).");
                await V2GTPStream.WriteFrameAsync(stream, MessageSet.Iso15118_2, buf.AsMemory(0, n), ct).ConfigureAwait(false);
            }
        }

        // The guard: only the (phase, request) pairs below are legal. The wildcard arm rejects the rest.
        private (BodyBaseType Body, Phase Next) Dispatch(BodyBaseType req) => (_phase, req) switch
        {
            (Phase.SessionSetup, SessionSetupReqType) =>
                (NewSession(), Phase.ServiceDiscovery),

            (Phase.ServiceDiscovery, ServiceDiscoveryReqType) =>
                (Discovery(), Phase.PaymentSelection),

            (Phase.PaymentSelection, PaymentServiceSelectionReqType) =>
                (new PaymentServiceSelectionResType(ResponseCode.OK), Phase.Authorization),

            (Phase.Authorization, AuthorizationReqType) =>
                (new AuthorizationResType(ResponseCode.OK, EVSEProcessing.Finished), Phase.ChargeParams),

            (Phase.ChargeParams, ChargeParameterDiscoveryReqType) =>
                (ChargeParams(), mode == PowerMode.Dc ? Phase.CableCheck : Phase.PowerOn),

            // ── DC-only pre-charge sequence ──
            (Phase.CableCheck, CableCheckReqType) =>
                (new CableCheckResType(ResponseCode.OK, DcEvseStatus(), EVSEProcessing.Finished), Phase.PreCharge),
            (Phase.PreCharge, PreChargeReqType) =>
                (new PreChargeResType(ResponseCode.OK, DcEvseStatus(), Volt(390)), Phase.PowerOn),

            (Phase.PowerOn, PowerDeliveryReqType { ChargeProgress: ChargeProgress.Start }) =>
                (PowerOnOrOff(), Phase.Charging),

            // ── charging loop (mode-specific request) ──
            (Phase.Charging, CurrentDemandReqType) when mode == PowerMode.Dc =>
                (CurrentDemand(), Phase.Charging),
            (Phase.Charging, ChargingStatusReqType) when mode == PowerMode.Ac =>
                (ChargingStatus(), Phase.Charging),
            (Phase.Charging, PowerDeliveryReqType { ChargeProgress: ChargeProgress.Stop }) =>
                (PowerOnOrOff(), mode == PowerMode.Dc ? Phase.WeldingDetection : Phase.SessionStop),

            (Phase.WeldingDetection, WeldingDetectionReqType) =>
                (new WeldingDetectionResType(ResponseCode.OK, DcEvseStatus(), Volt(5)), Phase.SessionStop),

            // A SessionStopReq is legal in *any* phase (ISO 15118-2 §8.4): the EV may abort the session at any
            // time, and the SECC answers gracefully and ends the session rather than raising the sequence
            // guard. Typed on the request, so it only ever matches a SessionStopReq (never the normal flow).
            (_, SessionStopReqType) =>
                (new SessionStopResType(ResponseCode.OK), Phase.Done),

            _ => throw new SessionAborted(
                $"SECC sequence guard: {req.GetType().Name.Replace("Type", "")} not allowed in phase {_phase} " +
                "(would be ResponseCode.FAILED_SequenceError)"),
        };

        // ── response builders ─────────────────────────────────────────────────
        private BodyBaseType NewSession()
        {
            _sessionId = RandomNumberGenerator.GetBytes(8);
            return new SessionSetupResType(ResponseCode.OK_NewSessionEstablished, "DE*ABC*E1", 1_600_000_000L);
        }

        private BodyBaseType Discovery() =>
            new ServiceDiscoveryResType(ResponseCode.OK,
                new PaymentOptionListType(new[] { PaymentOption.ExternalPayment }),
                new ChargeServiceType(ServiceID: 1, ServiceName: mode == PowerMode.Dc ? "DC" : "AC",
                    ServiceCategory.EVCharging, ServiceScope: null, FreeService: true,
                    new SupportedEnergyTransferModeType(new[]
                    {
                        mode == PowerMode.Dc ? EnergyTransferMode.DC_extended : EnergyTransferMode.AC_three_phase_core,
                    })),
                ServiceList: null);

        private BodyBaseType ChargeParams() =>
            mode == PowerMode.Dc
                ? new ChargeParameterDiscoveryResType(ResponseCode.OK, EVSEProcessing.Finished, SASchedules: null,
                    new DC_EVSEChargeParameterType(DcEvseStatus(),
                        EVSEMaximumCurrentLimit: Amp(200), EVSEMaximumPowerLimit: Watt(150_000),
                        EVSEMaximumVoltageLimit: Volt(500), EVSEMinimumCurrentLimit: Amp(0),
                        EVSEMinimumVoltageLimit: Volt(200), EVSECurrentRegulationTolerance: null,
                        EVSEPeakCurrentRipple: Amp(1), EVSEEnergyToBeDelivered: null))
                : new ChargeParameterDiscoveryResType(ResponseCode.OK, EVSEProcessing.Finished, SASchedules: null,
                    new AC_EVSEChargeParameterType(AcEvseStatus(),
                        EVSENominalVoltage: Volt(230), EVSEMaxCurrent: Amp(32)));

        private BodyBaseType PowerOnOrOff() =>
            mode == PowerMode.Dc
                ? new PowerDeliveryResType(ResponseCode.OK, DcEvseStatus())
                : new PowerDeliveryResType(ResponseCode.OK, AcEvseStatus());

        private BodyBaseType CurrentDemand() =>
            new CurrentDemandResType(ResponseCode.OK, DcEvseStatus(),
                EVSEPresentVoltage: Volt(400), EVSEPresentCurrent: Amp(120),
                EVSECurrentLimitAchieved: false, EVSEVoltageLimitAchieved: false, EVSEPowerLimitAchieved: false,
                EVSEMaximumVoltageLimit: null, EVSEMaximumCurrentLimit: null, EVSEMaximumPowerLimit: null,
                EVSEID: "DE*ABC*E1", SAScheduleTupleID: 1, MeterInfo: null, ReceiptRequired: null);

        private BodyBaseType ChargingStatus() =>
            new ChargingStatusResType(ResponseCode.OK, "DE*ABC*E1", SAScheduleTupleID: 1,
                EVSEMaxCurrent: null, MeterInfo: null, ReceiptRequired: null, AcEvseStatus());

        private static DC_EVSEStatusType DcEvseStatus() =>
            new(NotificationMaxDelay: 0, EVSENotification.None, EVSEIsolationStatus: null, DC_EVSEStatusCode.EVSE_Ready);
        private static AC_EVSEStatusType AcEvseStatus() =>
            new(NotificationMaxDelay: 0, EVSENotification.None, RCD: false);

        private static PhysicalValueType Volt(short v) => new(0, UnitSymbol.V, v);
        private static PhysicalValueType Amp(short a)  => new(0, UnitSymbol.A, a);
        private static PhysicalValueType Watt(int w)   => PhysicalValue.Of(w, UnitSymbol.W);
    }
}
