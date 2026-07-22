using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using Vanaheimr.V2G.Iso15118_20.CommonMessages;
using Vanaheimr.V2G.Iso15118_20.CommonMessages.Generated;
using Vanaheimr.V2G.Simulation.Framing;
using Vanaheimr.V2G.Simulation.Session;
using Vanaheimr.V2G.Tp;

namespace Vanaheimr.V2G.Simulation.StateMachines.Iso20
{
    /// <summary>Outcome of validating a live Plug &amp; Charge <c>AuthorizationReq</c>: whether the EV echoed our
    /// <c>GenChallenge</c>, whether the signed-element digest matched its reference, and whether the ECDSA
    /// signature verified against the contract leaf — plus what was observed (contract subject, signature method,
    /// and which <c>SignedInfo</c> grammar the signature verified under: <c>iso20-commonmessages</c> for our/cbV2G
    /// combined-schema form, <c>xmldsig-standalone</c> for the Josev-style standalone-xmldsig form, or
    /// <c>none</c>).</summary>
    public sealed record PnCAuthResult(bool ChallengeOk, bool DigestOk, bool SignatureOk, string ContractSubject,
                                       string SignatureMethod, string SignatureGrammar);

    /// <summary>
    /// The SECC side of an ISO 15118-20 session, shared between AC and DC: the CommonMessages phases
    /// (SessionSetup..ServiceSelection, PowerDelivery, SessionStop) live here — it offers both EIM and Plug &amp;
    /// Charge and, for a PnC EV, validates the signed AuthorizationReq (see <see cref="PnCAuth"/>). The
    /// diverging middle (charge-parameter discovery, the DC-only CableCheck/PreCharge sequence,
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

        /// <summary>The 16-byte PnC challenge we offered in AuthorizationSetupRes; the EV must echo it in a PnC AuthorizationReq.</summary>
        private byte[]? _genChallenge;

        /// <summary>The result of validating a PnC AuthorizationReq, if the EV authenticated via Plug &amp; Charge (null for EIM).</summary>
        public PnCAuthResult? PnCAuth { get; private set; }

        /// <summary>Advertise the Dynamic (ControlMode=2) parameter set ahead of Scheduled in ServiceDetailRes.
        /// Both modes are always offered ([V2G20-2656]: the SECC shall support Scheduled and Dynamic); the order
        /// only decides which one an EV that simply takes the first offered set (e.g. Josev) actually runs —
        /// the SECC itself answers whatever control mode the EV's requests carry, in kind.</summary>
        public bool PreferDynamicControlMode { get; set; }

        public bool IsDone => Phase == Phase20.Done;

        /// <summary>DC: CableCheck+PreCharge run between ScheduleExchange and PowerDelivery(Start). AC: skipped.</summary>
        protected abstract bool HasPreChargeSequence { get; }
        /// <summary>DC: WeldingDetection runs between PowerDelivery(Stop) and SessionStop. AC: skipped.</summary>
        protected abstract bool HasPostChargeSequence { get; }

        protected abstract (MessageSet Set, object Response) HandleChargeParameterDiscovery(object request);

        /// <summary>Is <paramref name="request"/> another poll of the self-looping <paramref name="phase"/> — so the
        /// SECC answers it and stays put — rather than the next-phase message that ends the loop? The
        /// <see cref="Phase20.PowerOn"/> poll (a real EV, e.g. Josev, repeats <c>PowerDeliveryReq(Start)</c> with
        /// <c>EVProcessing=Ongoing</c> until it starts the charge loop) is a CommonMessages request the base can
        /// name; <see cref="Secc20Dc"/> additionally classifies the DC-only poll phases
        /// (CableCheck/PreCharge/WeldingDetection), whose request types live in a separate, colliding namespace.</summary>
        protected virtual bool IsPollFor(Phase20 phase, object request) =>
            phase == Phase20.PowerOn && request is PowerDeliveryReq { ChargeProgress: ChargeProgress.Start };

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

            // A SessionStopReq is legal in *any* phase (ISO 15118-20 §7.9.2.4): the EV may abort the session at
            // any time, and the SECC answers gracefully and ends the session rather than raising the sequence
            // guard. Handled ahead of the phase switch so it wins over the wildcard poll / charge-loop arms
            // (which would otherwise mis-cast it to a DC/AC request). A live Josev reverse run showed an early
            // abort logging FAILED_SequenceError instead of a clean stop.
            if (set == MessageSet.Iso20CommonMessages && request is SessionStopReq stopReq)
            {
                Phase = Phase20.Done;
                return (MessageSet.Iso20CommonMessages, SessionStop(stopReq));
            }

            // A real EV *polls* the DC self-looping phases (CableCheck/PreCharge/WeldingDetection) — sending
            // the same request until it decides the step is done, then sending the next-phase message. Answer
            // each poll and stay put (the switch cases below map these phases onto themselves); when a non-poll
            // message arrives, advance through the self-loop phases *without consuming it* and re-evaluate it in
            // the phase it belongs to. So e.g. the first DC_PreChargeReq ends the CableCheck loop and is handled
            // by the PreCharge phase, and PowerDeliveryReq(Start) ends PreCharge and is handled by PowerOn.
            while (IsSelfLoopPhase(Phase) && !IsPollFor(Phase, request))
                Phase = NextAfter(Phase);

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

                // Self-looping poll phases: stay put and answer each poll. The pre-switch loop above guarantees
                // that if we're still in one of these phases, the request IS a poll for it (a next-phase message
                // would already have advanced Phase past here), and advances out when the loop ends.
                (Phase20.CableCheck, _, _) when HasPreChargeSequence =>
                    Append(HandleCableCheck(request), Phase20.CableCheck),

                (Phase20.PreCharge, _, _) when HasPreChargeSequence =>
                    Append(HandlePreCharge(request), Phase20.PreCharge),

                // Self-looping poll phase: a real EV repeats PowerDeliveryReq(Start) (EVProcessing=Ongoing)
                // until it begins the charge loop; answer each and stay. The pre-switch loop advances to
                // Charging (without consuming) once the first charge-loop message arrives.
                (Phase20.PowerOn, MessageSet.Iso20CommonMessages, PowerDeliveryReq { ChargeProgress: ChargeProgress.Start } r) =>
                    Step(MessageSet.Iso20CommonMessages, PowerDelivery(r), Phase20.PowerOn),

                (Phase20.Charging, MessageSet.Iso20CommonMessages, PowerDeliveryReq { ChargeProgress: ChargeProgress.Stop } r) =>
                    Step(MessageSet.Iso20CommonMessages, PowerDelivery(r), HasPostChargeSequence ? Phase20.WeldingDetection : Phase20.SessionStop),

                (Phase20.Charging, _, _) =>
                    Append(HandleChargeLoop(request), Phase20.Charging),

                (Phase20.WeldingDetection, _, _) when HasPostChargeSequence =>
                    Append(HandleWeldingDetection(request), Phase20.WeldingDetection),

                // SessionStopReq (in the normal SessionStop phase *and* any early-abort phase) is handled
                // ahead of this switch — see the top of Handle.

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

        /// <summary>The phases an EV polls (repeats) until it decides the step is done. PowerOn (PowerDelivery
        /// start) applies to both AC and DC; CableCheck/PreCharge/WeldingDetection are DC-only.</summary>
        private bool IsSelfLoopPhase(Phase20 p) =>
            p is Phase20.PowerOn
            || ((p is Phase20.CableCheck or Phase20.PreCharge) && HasPreChargeSequence)
            || (p is Phase20.WeldingDetection && HasPostChargeSequence);

        /// <summary>Where a self-looping phase hands off once its poll loop ends (the next-phase message arrives).</summary>
        private static Phase20 NextAfter(Phase20 p) => p switch
        {
            Phase20.CableCheck       => Phase20.PreCharge,
            Phase20.PreCharge        => Phase20.PowerOn,
            Phase20.PowerOn          => Phase20.Charging,
            Phase20.WeldingDetection => Phase20.SessionStop,
            _                        => p,
        };

        // ── CommonMessages phase handlers (identical for AC and DC — EIM only) ─
        private SessionSetupRes SessionSetup(SessionSetupReq req)
        {
            SessionCtx.SessionId = System.Security.Cryptography.RandomNumberGenerator.GetBytes(8);
            return new SessionSetupRes(SessionCtx.ToCommonHeader(), ResponseCode.OK_NewSessionEstablished, "DE*ABC*E1");
        }

        private AuthorizationSetupRes AuthSetup(AuthorizationSetupReq req)
        {
            // Offer both EIM and Plug & Charge. A PnC-capable EV (e.g. a Josev EVCC with a contract cert) will
            // pick PnC and sign its AuthorizationReq over this GenChallenge; our own loopback EVCC uses EIM. The
            // challenge is a fresh 16 bytes the EV must echo back (ISO 15118-20 Table 62).
            _genChallenge = RandomNumberGenerator.GetBytes(16);
            // The response's authorization-mode params are a *choice* (exactly one of EIM/PnC), so to enable
            // PnC we send the PnC mode (with the challenge) and leave EIM null — while still advertising both
            // in AuthorizationServices. An EIM-only EV (our loopback EVCC) ignores the mode block and sends
            // EIM regardless; a PnC EV (Josev with a contract cert) reads the challenge and signs.
            return new(SessionCtx.ToCommonHeader(), ResponseCode.OK,
                new[] { Authorization.PnC, Authorization.EIM },
                CertificateInstallationService: false,
                EIM_ASResAuthorizationMode: null,
                new PnC_ASResAuthorizationModeType(_genChallenge, SupportedProviders: null));
        }

        private AuthorizationRes Auth(AuthorizationReq req)
        {
            // Plug & Charge: validate the EV's signed AuthorizationReq (challenge echo + reference digest +
            // ECDSA signature over the contract leaf). We record the outcome rather than aborting, so a live
            // interop session completes and the verdict is observable; EIM carries no signature.
            if (req.PnC_AReqAuthorizationMode is { } pnc)
                PnCAuth = VerifyPnc(req, pnc);
            return new(SessionCtx.ToCommonHeader(), ResponseCode.OK, Processing.Finished);
        }

        /// <summary>Validates a PnC <see cref="AuthorizationReq"/>: the EV must echo our GenChallenge, the header
        /// signature's reference digest must match the re-encoded <c>PnC_AReqAuthorizationMode</c> fragment, and
        /// the SignedInfo signature must verify against the contract leaf's public key. Hashes are chosen from the
        /// message's own SignatureMethod/DigestMethod URIs (SHA-256 or SHA-512), so it works whatever the peer's
        /// contract-cert curve is (a real Josev PKI is P-256, not the -20-nominal secp521r1).</summary>
        private PnCAuthResult VerifyPnc(AuthorizationReq req, PnC_AReqAuthorizationModeType pnc)
        {
            bool challengeOk = _genChallenge is not null && pnc.GenChallenge.AsSpan().SequenceEqual(_genChallenge);

            var buf = new byte[8192];
            if (!CommonMessagesCodec.EncodeFragment_PnC_AReqAuthorizationMode(pnc, buf, out int n))
                return new PnCAuthResult(challengeOk, DigestOk: false, SignatureOk: false, "?", "fragment-encode-failed", "none");
            var fragment = buf.AsSpan(0, n);

            if (req.Header.Signature is not { } sig || sig.SignedInfo.Reference.Count == 0)
                return new PnCAuthResult(challengeOk, false, false, "?", "no-signature", "none");

            var reference = sig.SignedInfo.Reference[0];
            bool digestOk = HashOf(reference.DigestMethod.Algorithm, fragment).AsSpan().SequenceEqual(reference.DigestValue);

            string subject = "?";
            bool signatureOk = false;
            string grammar = "none";
            try
            {
                using var contract = X509CertificateLoader.LoadCertificate(pnc.ContractCertificateChain.Certificate);
                subject = contract.Subject;
                using var ecdsa = contract.GetECDsaPublicKey();
                if (ecdsa is not null)
                {
                    var hashName = HashNameFor(sig.SignedInfo.SignatureMethod.Algorithm);

                    // 1. Our production grammar: SignedInfo as a fragment of the full CommonMessages schema set
                    //    (byte-exact vs cbV2G). This is what our own EVCC signs.
                    if (ecdsa.VerifyData(V2GSignature.SignedInfoFragment(sig.SignedInfo), sig.SignatureValue.Value,
                                         hashName, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                        (signatureOk, grammar) = (true, "iso20-commonmessages");

                    // 2. Interop fallback: SignedInfo over the standalone xmldsig grammar (what Josev's stack
                    //    signs — see XmlDsigInteropVerify). Verify-only; we never sign this form.
                    else if (XmlDsigInteropVerify.VerifyStandaloneXmldsig(sig.SignedInfo, sig.SignatureValue.Value, ecdsa, hashName))
                        (signatureOk, grammar) = (true, "xmldsig-standalone");
                }
            }
            catch (Exception ex) { subject = $"cert-error: {ex.Message}"; }

            return new PnCAuthResult(challengeOk, digestOk, signatureOk, subject,
                                     sig.SignedInfo.SignatureMethod.Algorithm, grammar);
        }

        private static byte[] HashOf(string algorithmUri, ReadOnlySpan<byte> data) =>
            algorithmUri.Contains("sha256") ? SHA256.HashData(data) : SHA512.HashData(data);

        private static HashAlgorithmName HashNameFor(string algorithmUri) =>
            algorithmUri.Contains("sha256") ? HashAlgorithmName.SHA256 : HashAlgorithmName.SHA512;

        /// <summary>The ISO 15118-20 energy-transfer service ids this SECC advertises (Table 204): DC=2, AC=1,
        /// plus their bidirectional (BPT) variants DC_BPT=6, AC_BPT=5. A live Josev EVCC rejects a session with
        /// <c>WrongServiceID</c> if the mode it wants (e.g. DC_BPT) is not offered, so each subclass advertises
        /// both its unidirectional and BPT service; the actual direction is driven per-message by whether the
        /// EV sends a BPT energy-transfer-mode / control-mode (see the DC/AC charge-parameter and charge-loop
        /// hooks).</summary>
        protected abstract IReadOnlyList<ushort> EnergyServiceIds { get; }

        private ServiceDiscoveryRes SvcDiscovery(ServiceDiscoveryReq req) =>
            new(SessionCtx.ToCommonHeader(), ResponseCode.OK, ServiceRenegotiationSupported: false,
                new ServiceListType(EnergyServiceIds.Select(id => new ServiceType(id, FreeService: true)).ToArray()), VASList: null);

        private ServiceDetailRes SvcDetail(ServiceDetailReq req)
        {
            // The standard -20 energy-transfer parameter sets. A live Josev EVCC requires at least the
            // ControlMode parameter ("Control mode parameter missing" otherwise). We offer both control
            // modes — set 1: Scheduled (ControlMode=1), set 2: Dynamic (ControlMode=2) — ordered by
            // PreferDynamicControlMode, since a Josev EVCC adopts the *first* offered set's ControlMode.
            // MobilityNeedsMode=1 (mobility needs provided by the EVCC) is legal for both modes
            // ([V2G20-2663] only restricts MobilityNeedsMode=2 to Dynamic).
            static ParameterSetType ParamSet(ushort id, int controlMode) => new(id, new[]
            {
                new ParameterType("Connector", null, null, null, IntValue: 1, null, null),
                new ParameterType("ControlMode", null, null, null, IntValue: controlMode, null, null),
                new ParameterType("MobilityNeedsMode", null, null, null, IntValue: 1, null, null),
                new ParameterType("Pricing", null, null, null, IntValue: 0, null, null),
            });
            var scheduled = ParamSet(1, controlMode: 1);
            var dynamic   = ParamSet(2, controlMode: 2);
            return new(SessionCtx.ToCommonHeader(), ResponseCode.OK, req.ServiceID,
                new ServiceParameterListType(PreferDynamicControlMode
                    ? new[] { dynamic, scheduled }
                    : new[] { scheduled, dynamic }));
        }

        private ServiceSelectionRes SvcSelection(ServiceSelectionReq req) =>
            new(SessionCtx.ToCommonHeader(), ResponseCode.OK);

        private ScheduleExchangeRes ScheduleExchange(ScheduleExchangeReq req)
        {
            // Answer in kind ([V2G20-1600]): a Dynamic-mode EV sends Dynamic_SEReqControlMode and must get a
            // Dynamic res (all fields optional — Processing=Finished is the actual signal); a Scheduled-mode
            // EV gets the schedule-tuple offer below.
            if (req.Dynamic_SEReqControlMode is not null)
                return new(SessionCtx.ToCommonHeader(), ResponseCode.OK, Processing.Finished, GoToPause: false,
                    Dynamic_SEResControlMode: new Dynamic_SEResControlModeType(
                        DepartureTime: req.Dynamic_SEReqControlMode.DepartureTime,
                        MinimumSOC: null, TargetSOC: null, AbsolutePriceSchedule: null, PriceLevelSchedule: null),
                    Scheduled_SEResControlMode: null);

            var powerSchedule = new PowerScheduleType(TimeAnchor: 0, AvailableEnergy: null, PowerTolerance: null,
                new PowerScheduleEntryListType(new[] { new PowerScheduleEntryType(Duration: 3600, Power: new RationalNumberType(0, 100), null, null) }));

            // A ChargingSchedule must carry a price schedule (either PriceLevel or AbsolutePrice) — a live
            // Josev EVCC rejects the tuple otherwise. PriceLevelSchedule is the compact form (one flat level).
            var priceLevelSchedule = new PriceLevelScheduleType(Id: null, TimeAnchor: 0, PriceScheduleID: 1,
                PriceScheduleDescription: null, NumberOfPriceLevels: 1,
                new PriceLevelScheduleEntryListType(new[] { new PriceLevelScheduleEntryType(Duration: 3600, PriceLevel: 0) }));

            var scheduleTuple = new ScheduleTupleType(ScheduleTupleID: 1,
                ChargingSchedule: new ChargingScheduleType(powerSchedule, AbsolutePriceSchedule: null, priceLevelSchedule),
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
}
