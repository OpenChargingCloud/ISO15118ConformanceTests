using System.Security.Cryptography.X509Certificates;

using Vanaheimr.V2G.Iso15118_2;
using Vanaheimr.V2G.Iso15118_2.Generated;
using Vanaheimr.V2G.Simulation.Framing;
using Vanaheimr.V2G.Simulation.Session;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
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
    /// Payment: EIM (<c>ExternalPayment</c>) by default; with <see cref="Pnc"/> set and the SECC offering
    /// <c>Contract</c>, the session runs -2 Plug &amp; Charge — PaymentDetails (contract chain in,
    /// GenChallenge out), a <b>signed</b> AuthorizationReq, and a <b>signed</b> MeteringReceiptReq whenever
    /// a charging-status response demands one (all in Josev's signature form, <see cref="XmlDsigInterop2"/>).
    /// </summary>
    public sealed class Evcc2(
        Stream stream, PowerMode mode, TimeProvider clock, IAsyncDelay pollDelay, TimeSpan perMessageTimeout)
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

        // 8 KiB: a PaymentDetailsReq carries the full 3-cert contract chain (~2 KiB).
        private readonly byte[] _buf = new byte[8192];
        private byte[] _sid = new byte[8];   // 0 until SessionSetupRes assigns one

        public int Exchanges { get; private set; }
        public long BytesOnWire { get; private set; }

        /// <summary>Contract credentials (same shape as the -20 EVCC's); <c>null</c> (default) pays via EIM.</summary>
        public PncEvccOptions? Pnc { get; set; }

        /// <summary>How this session authorized: <c>"eim"</c>, or <c>"pnc-signed"</c> after a Contract
        /// PaymentDetails + signed AuthorizationReq.</summary>
        public string AuthorizationMode { get; private set; } = "eim";

        /// <summary>How many signed MeteringReceiptReq this session sent (Contract only).</summary>
        public int MeteringReceiptsSent { get; private set; }

        /// <summary>How to end the session: <c>Terminate</c> (default) or <c>Pause</c> — after a pause the
        /// caller reconnects and resumes via <see cref="ResumeSessionId"/> ([V2G2-740]).</summary>
        public ChargingSession StopMode { get; set; } = ChargingSession.Terminate;

        /// <summary>A paused predecessor's session id: the opening SessionSetupReq carries it (instead of
        /// the all-zero id) so the SECC rejoins the old session.</summary>
        public byte[]? ResumeSessionId { get; set; }

        /// <summary>The SECC's SessionSetup verdict: <c>OK_NewSessionEstablished</c> or, on a successful
        /// resume, <c>OK_OldSessionJoined</c>.</summary>
        public ResponseCode SessionSetupCode { get; private set; }

        /// <summary>The session id in effect (SECC-assigned, or the rejoined one) — keep it for a resume.</summary>
        public byte[] SessionId => _sid;

        /// <summary>When set, the EV initiates one renegotiation on its own after the first charging-status
        /// cycle (<c>PowerDeliveryReq(Renegotiate)</c> → new ChargeParameterDiscovery → PowerDelivery(Start)).
        /// Independent of that, the EV always reacts to a SECC-side <c>EVSENotification.ReNegotiation</c>.</summary>
        public bool Renegotiate { get; set; }

        /// <summary>How many renegotiation cycles this session ran (own + SECC-requested).</summary>
        public int Renegotiations { get; private set; }

        public async Task RunAsync(CancellationToken ct = default)
        {
            // ── SETUP ──────────────────────────────────────────────────────────
            if (ResumeSessionId is not null)
                _sid = ResumeSessionId;   // rejoin: the SessionSetupReq header carries the paused session's id
            var setup = await Send<SessionSetupResType>(new SessionSetupReqType(EVCCID: new byte[] { 0xAB, 0xCD, 0xEF, 0x01, 0x02, 0x03 }), ct);
            SessionSetupCode = setup.ResponseCode;
            var discovery = await Send<ServiceDiscoveryResType>(new ServiceDiscoveryReqType(ServiceScope: null, ServiceCategory: null), ct);

            bool contract = Pnc is not null && discovery.PaymentOptionList.PaymentOption.Contains(PaymentOption.Contract);
            await Send<PaymentServiceSelectionResType>(new PaymentServiceSelectionReqType(
                contract ? PaymentOption.Contract : PaymentOption.ExternalPayment,
                new SelectedServiceListType(new[] { new SelectedServiceType(ServiceID: 1, ParameterSetID: null) })), ct);

            // ── AUTH (loop until authorised) ───────────────────────────────────
            // Contract: PaymentDetails first (contract chain → GenChallenge), then a signed AuthorizationReq
            // (Id "id1", challenge echo; body-element fragment digested, Josev-form signature). Signed once —
            // the challenge does not change across polls. EIM: the plain unsigned request.
            AuthorizationReqType authReq;
            SignatureType? authSignature = null;
            if (contract)
            {
                var details = await Send<PaymentDetailsResType>(new PaymentDetailsReqType(
                    ContractEmaid(), new CertificateChainType(Id: null, Pnc!.ContractCertificate,
                        new SubCertificatesType(Pnc.SubCertificates.ToArray()))), ct);

                authReq = new AuthorizationReqType("id1", details.GenChallenge);
                var fragment = new byte[1024];
                if (!Iso2Codec.EncodeFragment_AuthorizationReq(authReq, fragment, out int n))
                    throw new InvalidOperationException("AuthorizationReq fragment encode failed.");
                authSignature = XmlDsigInterop2.Sign("id1", fragment.AsSpan(0, n), Pnc.ContractKey);
                AuthorizationMode = "pnc-signed";
            }
            else
                authReq = new AuthorizationReqType(Id: null, GenChallenge: null);

            while ((await Send<AuthorizationResType>(authReq, ct, authSignature))
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

            bool renegotiated = false;
            for (int cycle = 0; cycle < 3; cycle++)                    // a few charging-loop iterations
            {
                // A Contract SECC may demand a receipt (ReceiptRequired) in its status response — answer with
                // a signed MeteringReceiptReq echoing its MeterInfo (as a real EV, e.g. Josev, does).
                EVSENotification notification;
                if (mode == PowerMode.Dc)
                {
                    var cd = await Send<CurrentDemandResType>(CurrentDemand(), ct);
                    notification = cd.DC_EVSEStatus.EVSENotification;
                    if (cd.ReceiptRequired == true && cd.MeterInfo is not null)
                        await SendMeteringReceipt(cd.MeterInfo, cd.SAScheduleTupleID, ct);
                }
                else
                {
                    var cs = await Send<ChargingStatusResType>(new ChargingStatusReqType(), ct);
                    notification = cs.AC_EVSEStatus.EVSENotification;
                    if (cs.ReceiptRequired == true && cs.MeterInfo is not null)
                        await SendMeteringReceipt(cs.MeterInfo, cs.SAScheduleTupleID, ct);
                }

                // Renegotiation ([V2G2-841]) — reactive (the SECC notified ReNegotiation) or proactive
                // (Renegotiate option, once): PowerDelivery(Renegotiate) → fresh ChargeParameterDiscovery →
                // PowerDelivery(Start), then the charging loop continues.
                if (!renegotiated && (notification == EVSENotification.ReNegotiation || Renegotiate))
                {
                    renegotiated = true;
                    Renegotiations++;
                    await Send<PowerDeliveryResType>(PowerDelivery(ChargeProgress.Renegotiate), ct);
                    while ((await Send<ChargeParameterDiscoveryResType>(ChargeParameterDiscovery(), ct))
                               .EVSEProcessing != EVSEProcessing.Finished)
                        await pollDelay.Wait(PollInterval, ct);
                    await Send<PowerDeliveryResType>(PowerDelivery(ChargeProgress.Start), ct);
                }
                await pollDelay.Wait(PollInterval, ct);
            }

            await Send<PowerDeliveryResType>(PowerDelivery(ChargeProgress.Stop), ct);

            // ── STOP ───────────────────────────────────────────────────────────
            if (mode == PowerMode.Dc)
                await Send<WeldingDetectionResType>(new WeldingDetectionReqType(EvStatus()), ct);
            await Send<SessionStopResType>(new SessionStopReqType(StopMode), ct);
        }

        /// <summary>Signs and sends one MeteringReceiptReq for the SECC's MeterInfo, in the Josev form.</summary>
        private async Task SendMeteringReceipt(MeterInfoType meterInfo, byte? saScheduleTupleId, CancellationToken ct)
        {
            var receipt = new MeteringReceiptReqType("id2", _sid, saScheduleTupleId, meterInfo);
            var fragment = new byte[1024];
            if (!Iso2Codec.EncodeFragment_MeteringReceiptReq(receipt, fragment, out int n))
                throw new InvalidOperationException("MeteringReceiptReq fragment encode failed.");
            var signature = XmlDsigInterop2.Sign("id2", fragment.AsSpan(0, n), Pnc!.ContractKey);

            await Send<MeteringReceiptResType>(receipt, ct, signature);
            MeteringReceiptsSent++;
        }

        /// <summary>The eMAID for PaymentDetails — the contract certificate's CN (e.g. <c>UKSWI123456791A</c>).</summary>
        private string ContractEmaid()
        {
            using var contract = X509CertificateLoader.LoadCertificate(Pnc!.ContractCertificate);
            return contract.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        }

        private async Task<T> Send<T>(BodyBaseType requestBody, CancellationToken ct, SignatureType? signature = null) where T : BodyBaseType
        {
            var header = new MessageHeaderType(_sid, Notification: null, Signature: signature);
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
