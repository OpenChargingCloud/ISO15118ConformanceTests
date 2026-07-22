using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using Vanaheimr.V2G.Iso15118_2;
using Vanaheimr.V2G.Iso15118_2.Generated;
using Vanaheimr.V2G.Simulation.Framing;
using Vanaheimr.V2G.Simulation.Session;
using Vanaheimr.V2G.Tp;

namespace Vanaheimr.V2G.Simulation.StateMachines.Iso2
{
    /// <summary>Outcome of validating a -2 Plug &amp; Charge signed <c>AuthorizationReq</c>: GenChallenge echo,
    /// reference digest over the body-element fragment, and the ECDSA signature against the contract leaf
    /// stored at PaymentDetails — with the <c>SignedInfo</c> grammar it verified under
    /// (<c>iso2-msgdef</c> = our/cbV2G combined form, <c>xmldsig-standalone</c> = the Josev form).</summary>
    public sealed record Iso2PnCResult(bool ChallengeOk, bool DigestOk, bool SignatureOk,
                                       string SignatureGrammar, string ContractSubject);

    /// <summary>Outcome of validating one signed -2 <c>MeteringReceiptReq</c> (same dual-grammar dance).</summary>
    public sealed record Iso2ReceiptResult(bool DigestOk, bool SignatureOk, string SignatureGrammar);

    /// <summary>
    /// The charge point (SECC) side of an ISO 15118-2 session — a <b>sequence-guarded</b> responder. It
    /// advances through the charging state machine and only accepts the request expected next; anything
    /// out of order raises <see cref="SessionAborted"/> (a real SECC would answer
    /// <c>ResponseCode.FAILED_SequenceError</c> and close). It also enforces the SECC
    /// <i>sequence timeout</i>: if the EV goes quiet mid-session for too long, the session is torn down.
    /// Payment: both <c>ExternalPayment</c> (EIM) and <c>Contract</c> (Plug &amp; Charge) are offered — a
    /// Contract EV runs PaymentDetails (contract chain in, GenChallenge out), a <b>signed</b>
    /// AuthorizationReq (verified dual-grammar, see <see cref="Iso2PnCResult"/>), and gets
    /// <c>ReceiptRequired</c> in its charging-status responses, so each loop cycle carries a <b>signed</b>
    /// MeteringReceiptReq (verified the same way).
    /// <see cref="Handle"/> is a pure, synchronous state transition — directly unit-testable without a
    /// socket; <see cref="RunAsync"/> is the thin loop that drives it from a real <see cref="Stream"/>.
    /// </summary>
    public sealed class Secc2(PowerMode mode, TimeSpan sequenceTimeout, TimeProvider clock)
    {
        private enum Phase
        {
            SessionSetup, ServiceDiscovery, PaymentSelection, PaymentDetails, Authorization, ChargeParams,
            CableCheck, PreCharge, PowerOn, Charging, WeldingDetection, SessionStop, Done,
        }

        private Phase _phase = Phase.SessionSetup;
        private byte[] _sessionId = new byte[8];
        private DateTimeOffset _lastSeen = clock.GetUtcNow();

        // ── Plug & Charge session state (set by PaymentServiceSelection/PaymentDetails) ─
        private bool _contract;
        private bool _receiptRequested;   // demand exactly ONE receipt per session (see ChargingStatus)
        private byte[]? _genChallenge;
        private ECDsa? _contractKey;
        private string _contractSubject = "?";
        private MessageHeaderType? _requestHeader;   // the header of the request currently in Dispatch

        /// <summary>The signed-AuthorizationReq verdict, if the EV paid via Contract (null for EIM).</summary>
        public Iso2PnCResult? PnCAuth { get; private set; }

        /// <summary>One verdict per signed MeteringReceiptReq received (Contract sessions only).</summary>
        public List<Iso2ReceiptResult> MeteringReceipts { get; } = new();

        /// <summary>True once the session has reached its terminal (post-SessionStop) phase.</summary>
        public bool IsDone => _phase == Phase.Done;

        /// <summary>True when the session ended with <c>ChargingSession.Pause</c> rather than Terminate —
        /// the caller should keep <see cref="SessionId"/> and offer it as <see cref="ResumeSessionId"/> to
        /// the next <see cref="Secc2"/> instance so the EV can rejoin ([V2G2-740]).</summary>
        public bool Paused { get; private set; }

        /// <summary>The session id this SECC assigned (or rejoined).</summary>
        public byte[] SessionId => _sessionId;

        /// <summary>A paused predecessor's session id: a SessionSetupReq carrying it rejoins the old session
        /// (<c>ResponseCode.OK_OldSessionJoined</c>); anything else starts a fresh one.</summary>
        public byte[]? ResumeSessionId { get; set; }

        public V2G_Message Handle(V2G_Message request)
        {
            var now = clock.GetUtcNow();
            if (_phase is not Phase.SessionSetup && now - _lastSeen > sequenceTimeout)
                throw new SessionAborted($"SECC sequence timeout: EV silent for > {sequenceTimeout.TotalSeconds:0}s");
            _lastSeen = now;

            _requestHeader = request.Header;   // the PnC verify paths need the header signature
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

            // Contract (Plug & Charge) inserts the PaymentDetails exchange before Authorization;
            // ExternalPayment (EIM) goes straight to Authorization.
            (Phase.PaymentSelection, PaymentServiceSelectionReqType r) =>
                (new PaymentServiceSelectionResType(ResponseCode.OK),
                 (_contract = r.SelectedPaymentOption == PaymentOption.Contract) ? Phase.PaymentDetails : Phase.Authorization),

            (Phase.PaymentDetails, PaymentDetailsReqType r) =>
                (PaymentDetails(r), Phase.Authorization),

            (Phase.Authorization, AuthorizationReqType r) =>
                (Authorize(r), Phase.ChargeParams),

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
            // Contract sessions: our charging-status responses set ReceiptRequired, so each loop cycle the
            // EV answers with a signed MeteringReceiptReq — verify it and stay in the charging loop.
            (Phase.Charging, MeteringReceiptReqType r) when _contract =>
                (MeteringReceipt(r), Phase.Charging),

            (Phase.Charging, PowerDeliveryReqType { ChargeProgress: ChargeProgress.Stop }) =>
                (PowerOnOrOff(), mode == PowerMode.Dc ? Phase.WeldingDetection : Phase.SessionStop),

            (Phase.WeldingDetection, WeldingDetectionReqType) =>
                (new WeldingDetectionResType(ResponseCode.OK, DcEvseStatus(), Volt(5)), Phase.SessionStop),

            // A SessionStopReq is legal in *any* phase (ISO 15118-2 §8.4): the EV may abort the session at any
            // time, and the SECC answers gracefully and ends the session rather than raising the sequence
            // guard. Typed on the request, so it only ever matches a SessionStopReq (never the normal flow).
            // ChargingSession=Pause parks the session instead of terminating it (Paused + SessionId let the
            // caller resume it on the next connection).
            (_, SessionStopReqType r) =>
                (SessionStop(r), Phase.Done),

            _ => throw new SessionAborted(
                $"SECC sequence guard: {req.GetType().Name.Replace("Type", "")} not allowed in phase {_phase} " +
                "(would be ResponseCode.FAILED_SequenceError)"),
        };

        // ── response builders ─────────────────────────────────────────────────
        private BodyBaseType NewSession()
        {
            // Resume ([V2G2-740]): a SessionSetupReq whose header carries a paused predecessor's session id
            // rejoins that session; any other id (normally all-zero) starts a fresh one.
            if (ResumeSessionId is not null && _requestHeader!.SessionID.AsSpan().SequenceEqual(ResumeSessionId))
            {
                _sessionId = ResumeSessionId;
                return new SessionSetupResType(ResponseCode.OK_OldSessionJoined, "DE*ABC*E1", 1_600_000_000L);
            }

            _sessionId = RandomNumberGenerator.GetBytes(8);
            return new SessionSetupResType(ResponseCode.OK_NewSessionEstablished, "DE*ABC*E1", 1_600_000_000L);
        }

        private BodyBaseType SessionStop(SessionStopReqType req)
        {
            Paused = req.ChargingSession == ChargingSession.Pause;
            return new SessionStopResType(ResponseCode.OK);
        }

        private BodyBaseType Discovery() =>
            new ServiceDiscoveryResType(ResponseCode.OK,
                // Contract first: a Josev EVCC picks Plug & Charge whenever Contract is offered AND the
                // session runs over TLS ([V2G2-828]); an EIM EV simply selects ExternalPayment.
                new PaymentOptionListType(new[] { PaymentOption.Contract, PaymentOption.ExternalPayment }),
                new ChargeServiceType(ServiceID: 1, ServiceName: mode == PowerMode.Dc ? "DC" : "AC",
                    ServiceCategory.EVCharging, ServiceScope: null, FreeService: true,
                    new SupportedEnergyTransferModeType(new[]
                    {
                        mode == PowerMode.Dc ? EnergyTransferMode.DC_extended : EnergyTransferMode.AC_three_phase_core,
                    })),
                ServiceList: null);

        private BodyBaseType ChargeParams() =>
            mode == PowerMode.Dc
                ? new ChargeParameterDiscoveryResType(ResponseCode.OK, EVSEProcessing.Finished, Schedules(),
                    new DC_EVSEChargeParameterType(DcEvseStatus(),
                        EVSEMaximumCurrentLimit: Amp(200), EVSEMaximumPowerLimit: Watt(150_000),
                        EVSEMaximumVoltageLimit: Volt(500), EVSEMinimumCurrentLimit: Amp(0),
                        EVSEMinimumVoltageLimit: Volt(200), EVSECurrentRegulationTolerance: null,
                        EVSEPeakCurrentRipple: Amp(1), EVSEEnergyToBeDelivered: null))
                : new ChargeParameterDiscoveryResType(ResponseCode.OK, EVSEProcessing.Finished, Schedules(),
                    new AC_EVSEChargeParameterType(AcEvseStatus(),
                        EVSENominalVoltage: Volt(230), EVSEMaxCurrent: Amp(32)));

        /// <summary>The SASchedule offer: with EVSEProcessing=Finished the response must carry a
        /// SAScheduleList ([V2G2-905]) — a live Josev EVCC crashes on its absence (found 2026-07-22; our
        /// loopback EVCC never read it, which masked the gap). One tuple, one 1-hour 11-kW PMax entry.</summary>
        private static SAScheduleListType Schedules() =>
            new(new[]
            {
                new SAScheduleTupleType(SAScheduleTupleID: 1,
                    new PMaxScheduleType(new[]
                    {
                        new PMaxScheduleEntryType(new RelativeTimeIntervalType(Start: 0, Duration: 3600), PMax: Watt(11_000)),
                    }),
                    SalesTariff: null),
            });

        private BodyBaseType PowerOnOrOff() =>
            mode == PowerMode.Dc
                ? new PowerDeliveryResType(ResponseCode.OK, DcEvseStatus())
                : new PowerDeliveryResType(ResponseCode.OK, AcEvseStatus());

        private BodyBaseType CurrentDemand()
        {
            bool receipt = DemandReceipt();
            return new CurrentDemandResType(ResponseCode.OK, DcEvseStatus(),
                EVSEPresentVoltage: Volt(400), EVSEPresentCurrent: Amp(120),
                EVSECurrentLimitAchieved: false, EVSEVoltageLimitAchieved: false, EVSEPowerLimitAchieved: false,
                EVSEMaximumVoltageLimit: null, EVSEMaximumCurrentLimit: null, EVSEMaximumPowerLimit: null,
                EVSEID: "DE*ABC*E1", SAScheduleTupleID: 1,
                MeterInfo: receipt ? Meter() : null, ReceiptRequired: receipt ? true : null);
        }

        private BodyBaseType ChargingStatus()
        {
            // A Contract session gets ReceiptRequired + the MeterInfo the EV echoes back inside its
            // signed MeteringReceiptReq (a Josev EVCC only honours this over TLS).
            bool receipt = DemandReceipt();
            return new ChargingStatusResType(ResponseCode.OK, "DE*ABC*E1", SAScheduleTupleID: 1,
                EVSEMaxCurrent: null,
                MeterInfo: receipt ? Meter() : null, ReceiptRequired: receipt ? true : null,
                AcEvseStatus());
        }

        /// <summary>Whether THIS status response demands a receipt: exactly once per Contract session. A
        /// live Josev EVCC re-enters ChargingStatus after every MeteringReceiptRes and only counts down its
        /// charge-loop cycles on receipt-free responses — demanding one every cycle loops the session
        /// forever (found live 2026-07-22: 1789 receipts before we pulled the plug).</summary>
        private bool DemandReceipt()
        {
            if (!_contract || _receiptRequested) return false;
            _receiptRequested = true;
            return true;
        }

        // ── Plug & Charge handlers ────────────────────────────────────────────

        /// <summary>Stores the EV's contract leaf (its public key verifies the signatures that follow) and
        /// hands out the 16-byte GenChallenge the signed AuthorizationReq must echo ([V2G2-825]).</summary>
        private BodyBaseType PaymentDetails(PaymentDetailsReqType req)
        {
            _genChallenge = RandomNumberGenerator.GetBytes(16);
            try
            {
                using var contract = X509CertificateLoader.LoadCertificate(req.ContractSignatureCertChain.Certificate);
                _contractSubject = contract.Subject;
                _contractKey = contract.GetECDsaPublicKey();
            }
            catch (Exception ex) { _contractSubject = $"cert-error: {ex.Message}"; }

            return new PaymentDetailsResType(ResponseCode.OK, _genChallenge, clock.GetUtcNow().ToUnixTimeSeconds());
        }

        /// <summary>EIM: plain OK. Contract: validate the <b>signed</b> AuthorizationReq — challenge echo,
        /// reference digest over the re-encoded body-element fragment, and the ECDSA signature under our
        /// combined -2 grammar or (Josev) the standalone-xmldsig one.</summary>
        private BodyBaseType Authorize(AuthorizationReqType req)
        {
            if (_contract)
            {
                bool challengeOk = _genChallenge is not null && req.GenChallenge is not null
                    && req.GenChallenge.AsSpan().SequenceEqual(_genChallenge);

                var buf = new byte[1024];
                bool fragOk = Iso2Codec.EncodeFragment_AuthorizationReq(req, buf, out int n);
                var (digestOk, signatureOk, grammar) = VerifyBodySignature(fragOk ? buf.AsSpan(0, n) : default);
                PnCAuth = new Iso2PnCResult(challengeOk, digestOk, signatureOk, grammar, _contractSubject);
            }
            return new AuthorizationResType(ResponseCode.OK, EVSEProcessing.Finished);
        }

        /// <summary>Validates one signed MeteringReceiptReq (same digest + dual-grammar signature check,
        /// no challenge) and acknowledges with the mode's EVSE status.</summary>
        private BodyBaseType MeteringReceipt(MeteringReceiptReqType req)
        {
            var buf = new byte[1024];
            bool fragOk = Iso2Codec.EncodeFragment_MeteringReceiptReq(req, buf, out int n);
            var (digestOk, signatureOk, grammar) = VerifyBodySignature(fragOk ? buf.AsSpan(0, n) : default);
            MeteringReceipts.Add(new Iso2ReceiptResult(digestOk, signatureOk, grammar));

            return new MeteringReceiptResType(ResponseCode.OK,
                mode == PowerMode.Dc ? DcEvseStatus() : AcEvseStatus());
        }

        /// <summary>The shared verify half: reference digest of <paramref name="fragment"/> against the
        /// request header's signature, then ECDSA over the SignedInfo — first our combined -2 grammar
        /// (<c>V2GSignature</c>), then the Josev standalone-xmldsig fallback.</summary>
        private (bool DigestOk, bool SignatureOk, string Grammar) VerifyBodySignature(ReadOnlySpan<byte> fragment)
        {
            if (fragment.IsEmpty || _requestHeader?.Signature is not { } sig
                || sig.SignedInfo.Reference.Count == 0 || _contractKey is null)
                return (false, false, "none");

            bool digestOk = V2GSignature.VerifyReference(sig.SignedInfo.Reference[0], fragment);

            if (V2GSignature.Verify(sig.SignedInfo, sig.SignatureValue.Value, _contractKey))
                return (digestOk, true, "iso2-msgdef");
            if (XmlDsigInterop2.VerifyStandaloneXmldsig(sig.SignedInfo, sig.SignatureValue.Value, _contractKey))
                return (digestOk, true, "xmldsig-standalone");
            return (digestOk, false, "none");
        }

        private MeterInfoType Meter() =>
            new("VAN*M1", MeterReading: 42, SigMeterReading: null, MeterStatus: null,
                TMeter: clock.GetUtcNow().ToUnixTimeSeconds());

        private static DC_EVSEStatusType DcEvseStatus() =>
            new(NotificationMaxDelay: 0, EVSENotification.None, EVSEIsolationStatus: null, DC_EVSEStatusCode.EVSE_Ready);
        private static AC_EVSEStatusType AcEvseStatus() =>
            new(NotificationMaxDelay: 0, EVSENotification.None, RCD: false);

        private static PhysicalValueType Volt(short v) => new(0, UnitSymbol.V, v);
        private static PhysicalValueType Amp(short a)  => new(0, UnitSymbol.A, a);
        private static PhysicalValueType Watt(int w)   => PhysicalValue.Of(w, UnitSymbol.W);
    }
}
