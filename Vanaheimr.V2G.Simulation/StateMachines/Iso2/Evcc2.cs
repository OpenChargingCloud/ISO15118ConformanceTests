using Vanaheimr.V2G.Iso15118_2;
using Vanaheimr.V2G.Iso15118_2.Generated;
using Vanaheimr.V2G.Simulation.Framing;
using Vanaheimr.V2G.Simulation.Session;
using Vanaheimr.V2G.Simulation.Timing;
using Vanaheimr.V2G.Tp;

namespace Vanaheimr.V2G.Simulation.StateMachines.Iso2
{
    /// <summary>
    /// The vehicle (EVCC) side of an ISO 15118-2 session — it drives the session over an already-connected
    /// (and, for -20, already-SAP-negotiated) <see cref="Stream"/>. Each step is one request/response
    /// exchange framed as V2GTP/EXI via <see cref="V2GTPStream"/>; the poll loops (Authorization,
    /// ChargeParameterDiscovery) back off through <see cref="IAsyncDelay"/> instead of a hardcoded
    /// <c>Task.Delay</c>, and every exchange is checked against <paramref name="perMessageTimeout"/> using
    /// <paramref name="clock"/> — mirroring the ISO 15118-2 EV-side performance timeout, simplified.
    /// </summary>
    public sealed class Evcc2(
        Stream stream, PowerMode mode, TimeProvider clock, IAsyncDelay pollDelay, TimeSpan perMessageTimeout)
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

        private readonly byte[] _buf = new byte[512];
        private byte[] _sid = new byte[8];   // 0 until SessionSetupRes assigns one

        public int Exchanges { get; private set; }
        public long BytesOnWire { get; private set; }

        public async Task RunAsync(CancellationToken ct = default)
        {
            // ── SETUP ──────────────────────────────────────────────────────────
            await Send<SessionSetupResType>(new SessionSetupReqType(EVCCID: new byte[] { 0xAB, 0xCD, 0xEF, 0x01, 0x02, 0x03 }), ct);
            await Send<ServiceDiscoveryResType>(new ServiceDiscoveryReqType(ServiceScope: null, ServiceCategory: null), ct);
            await Send<PaymentServiceSelectionResType>(new PaymentServiceSelectionReqType(PaymentOption.ExternalPayment,
                new SelectedServiceListType(new[] { new SelectedServiceType(ServiceID: 1, ParameterSetID: null) })), ct);

            // ── AUTH (loop until authorised) ───────────────────────────────────
            while ((await Send<AuthorizationResType>(new AuthorizationReqType(Id: null, GenChallenge: null), ct))
                       .EVSEProcessing != EVSEProcessing.Finished)
                await pollDelay.Wait(PollInterval, ct);

            // ── CHARGE PARAMETERS (+ DC cable check / pre-charge) ──────────────
            while ((await Send<ChargeParameterDiscoveryResType>(ChargeParameterDiscovery(), ct))
                       .EVSEProcessing != EVSEProcessing.Finished)
                await pollDelay.Wait(PollInterval, ct);

            if (mode == PowerMode.Dc)
            {
                while ((await Send<CableCheckResType>(new CableCheckReqType(EvStatus()), ct))
                           .EVSEProcessing != EVSEProcessing.Finished)
                    await pollDelay.Wait(PollInterval, ct);
                await Send<PreChargeResType>(new PreChargeReqType(EvStatus(),
                    EVTargetVoltage: Volt(400), EVTargetCurrent: Amp(2)), ct);
            }

            // ── CHARGE ─────────────────────────────────────────────────────────
            await Send<PowerDeliveryResType>(PowerDelivery(ChargeProgress.Start), ct);

            for (int cycle = 0; cycle < 3; cycle++)                    // a few charging-loop iterations
            {
                if (mode == PowerMode.Dc)
                    await Send<CurrentDemandResType>(CurrentDemand(), ct);
                else
                    await Send<ChargingStatusResType>(new ChargingStatusReqType(), ct);
                await pollDelay.Wait(PollInterval, ct);
            }

            await Send<PowerDeliveryResType>(PowerDelivery(ChargeProgress.Stop), ct);

            // ── STOP ───────────────────────────────────────────────────────────
            if (mode == PowerMode.Dc)
                await Send<WeldingDetectionResType>(new WeldingDetectionReqType(EvStatus()), ct);
            await Send<SessionStopResType>(new SessionStopReqType(ChargingSession.Terminate), ct);
        }

        private async Task<T> Send<T>(BodyBaseType requestBody, CancellationToken ct) where T : BodyBaseType
        {
            var header = new MessageHeaderType(_sid, Notification: null, Signature: null);
            var request = new V2G_Message(header, new BodyType(requestBody));
            if (!request.TryEncode(_buf, out int reqLen))
                throw new InvalidOperationException("EXI encode failed (buffer too small?).");

            var start = clock.GetUtcNow();
            await V2GTPStream.WriteFrameAsync(stream, MessageSet.Iso15118_2, _buf.AsMemory(0, reqLen), ct).ConfigureAwait(false);
            var (set, message) = await V2GTPStream.ReadFrameAsync(stream, ct).ConfigureAwait(false);
            var elapsed = clock.GetUtcNow() - start;

            if (elapsed > perMessageTimeout)
                throw new SessionAborted(
                    $"{typeof(T).Name.Replace("ResType", "")}: no response within {perMessageTimeout.TotalMilliseconds:0} ms " +
                    $"(took {elapsed.TotalMilliseconds:0} ms).");
            if (set != MessageSet.Iso15118_2 || message is not V2G_Message reply)
                throw new SessionAborted($"expected an ISO 15118-2 reply, got {set}.");

            Exchanges++;
            BytesOnWire += V2GTP.HeaderSize + reqLen; // request side; response side is the peer's own accounting

            _sid = reply.Header.SessionID;             // adopt the SECC-assigned session id
            return (T)reply.Body.BodyElement!;
        }

        // ── request builders ──────────────────────────────────────────────────
        private static PowerDeliveryReqType PowerDelivery(ChargeProgress progress) =>
            new(progress, SAScheduleTupleID: 1, ChargingProfile: null, EVPowerDeliveryParameter: null);

        private ChargeParameterDiscoveryReqType ChargeParameterDiscovery() =>
            mode == PowerMode.Dc
                ? new ChargeParameterDiscoveryReqType(MaxEntriesSAScheduleTuple: null, EnergyTransferMode.DC_extended,
                    new DC_EVChargeParameterType(DepartureTime: null, EvStatus(),
                        EVMaximumCurrentLimit: Amp(200), EVMaximumPowerLimit: null, EVMaximumVoltageLimit: Volt(500),
                        EVEnergyCapacity: null, EVEnergyRequest: null, FullSOC: 100, BulkSOC: 80))
                : new ChargeParameterDiscoveryReqType(MaxEntriesSAScheduleTuple: null, EnergyTransferMode.AC_three_phase_core,
                    new AC_EVChargeParameterType(DepartureTime: null,
                        EAmount: PhysicalValue.Of(22_000, UnitSymbol.Wh), EVMaxVoltage: Volt(400),
                        EVMaxCurrent: Amp(32), EVMinCurrent: Amp(6)));

        private CurrentDemandReqType CurrentDemand() =>
            new(EvStatus(), EVTargetCurrent: Amp(120),
                EVMaximumVoltageLimit: null, EVMaximumCurrentLimit: null, EVMaximumPowerLimit: null,
                BulkChargingComplete: null, ChargingComplete: false,
                RemainingTimeToFullSoC: null, RemainingTimeToBulkSoC: null,
                EVTargetVoltage: Volt(400));

        private static DC_EVStatusType EvStatus() => new(EVReady: true, DC_EVErrorCode.NO_ERROR, EVRESSSOC: 50);
        private static PhysicalValueType Volt(short v) => new(0, UnitSymbol.V, v);
        private static PhysicalValueType Amp(short a)  => new(0, UnitSymbol.A, a);
    }
}
