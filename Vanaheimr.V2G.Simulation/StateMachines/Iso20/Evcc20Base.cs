using System.Linq;

using Vanaheimr.V2G.Iso15118_20.CommonMessages;
using Vanaheimr.V2G.Iso15118_20.CommonMessages.Generated;
using Vanaheimr.V2G.Simulation.Framing;
using Vanaheimr.V2G.Simulation.Session;
using Vanaheimr.V2G.Simulation.Timing;
using Vanaheimr.V2G.Tp;

namespace Vanaheimr.V2G.Simulation.StateMachines.Iso20
{
    /// <summary>
    /// The EVCC side of an ISO 15118-20 session, shared between AC and DC: drives the CommonMessages
    /// phases directly (EIM by default; Plug &amp; Charge with a signed AuthorizationReq when
    /// <see cref="Pnc"/> is set and the SECC offers it), and calls the <c>protected abstract</c> hooks below
    /// for the diverging middle — implemented by <see cref="Evcc20Dc"/>/<see cref="Evcc20Ac"/>, which know
    /// which DC/AC codec and concrete request/response types their energy-transfer mode actually uses.
    /// </summary>
    public abstract class Evcc20Base(
        Stream stream, TimeProvider clock, IAsyncDelay pollDelay, TimeSpan perMessageTimeout)
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

        protected readonly SessionContext SessionCtx = new(clock);
        protected IAsyncDelay PollDelay => pollDelay;
        // 8 KiB: a signed PnC AuthorizationReq carries a 3-cert contract chain (~2 KiB) — 1 KiB is too small.
        private readonly byte[] _buf = new byte[8192];

        public int Exchanges { get; private set; }
        public long BytesOnWire { get; private set; }

        /// <summary>Contract credentials enabling Plug &amp; Charge; <c>null</c> (default) authorizes via EIM.</summary>
        public PncEvccOptions? Pnc { get; set; }

        /// <summary>How this session actually authorized: <c>"eim"</c>, or <c>"pnc-signed"</c> when a signed
        /// PnC AuthorizationReq was sent (requires <see cref="Pnc"/> set and the SECC offering PnC).</summary>
        public string AuthorizationMode { get; private set; } = "eim";

        /// <summary>Runs charge-parameter discovery exactly once (no polling — -20's DC/AC CPD response carries no EVSEProcessing field).</summary>
        protected abstract Task RunChargeParameterDiscoveryAsync(CancellationToken ct);
        /// <summary>DC: CableCheck+PreCharge. AC: no-op.</summary>
        protected abstract Task RunPreChargeSequenceAsync(CancellationToken ct);
        /// <summary>One charge-loop request/response (caller loops this a fixed number of times).</summary>
        protected abstract Task RunChargeLoopIterationAsync(CancellationToken ct);
        /// <summary>DC: WeldingDetection. AC: no-op.</summary>
        protected abstract Task RunPostChargeSequenceAsync(CancellationToken ct);

        /// <summary>The energy-transfer mode this EVCC drives — used to pick the matching service from the
        /// SECC's advertised catalog during service discovery.</summary>
        protected abstract PowerMode EnergyMode { get; }

        public async Task RunAsync(CancellationToken ct = default)
        {
            var setupRes = await Exchange<SessionSetupRes>(MessageSet.Iso20CommonMessages,
                dest => new SessionSetupReq(SessionCtx.ToCommonHeader(), "EVCC01").TryEncode(dest, out int n) ? n : throw EncodeFailed(), ct);

            // Adopt the SECC-assigned SessionID: every subsequent request header must carry it, not the
            // all-zero id the EVCC opens SessionSetup with (ISO 15118-20 §7.9.2.4). A live Josev interop run
            // caught this — Josev's SECC strictly rejects a mismatched session id (our loopback SECC did not).
            SessionCtx.SessionId = setupRes.Header.SessionID;

            var authSetup = await Exchange<AuthorizationSetupRes>(MessageSet.Iso20CommonMessages,
                dest => new AuthorizationSetupReq(SessionCtx.ToCommonHeader()).TryEncode(dest, out int n) ? n : throw EncodeFailed(), ct);

            var encodeAuthReq = BuildAuthorizationReqEncoder(authSetup);
            while ((await Exchange<AuthorizationRes>(MessageSet.Iso20CommonMessages, encodeAuthReq, ct))
                   .EVSEProcessing != Processing.Finished)
                await pollDelay.Wait(PollInterval, ct);

            // Service negotiation is dynamic: select the energy-transfer service and parameter set the SECC
            // actually advertises, rather than assuming fixed ids. A live Josev interop run caught the old
            // hardcoded ServiceID=1/ParameterSetID=1 (Josev's DC catalog offers neither) — our loopback SECC
            // happened to advertise exactly those, which masked it.
            var discovery = await Exchange<ServiceDiscoveryRes>(MessageSet.Iso20CommonMessages,
                dest => new ServiceDiscoveryReq(SessionCtx.ToCommonHeader(), null).TryEncode(dest, out int n) ? n : throw EncodeFailed(), ct);
            ushort serviceId = SelectEnergyTransferService(discovery);

            var detail = await Exchange<ServiceDetailRes>(MessageSet.Iso20CommonMessages,
                dest => new ServiceDetailReq(SessionCtx.ToCommonHeader(), serviceId).TryEncode(dest, out int n) ? n : throw EncodeFailed(), ct);
            ushort parameterSetId = SelectParameterSet(detail);

            await Exchange<ServiceSelectionRes>(MessageSet.Iso20CommonMessages,
                dest => new ServiceSelectionReq(SessionCtx.ToCommonHeader(),
                    new SelectedServiceType(serviceId, parameterSetId), null).TryEncode(dest, out int n) ? n : throw EncodeFailed(), ct);

            await RunChargeParameterDiscoveryAsync(ct);

            // MaximumSupportingPoints is schema-bounded to [12, 1024] (the encoder biases by 12); a smaller
            // value underflows on the wire. A live Josev run rejected the earlier 1 (our lenient SECC didn't).
            ScheduleExchangeRes scheduleRes;
            do
            {
                scheduleRes = await Exchange<ScheduleExchangeRes>(MessageSet.Iso20CommonMessages,
                    dest => new ScheduleExchangeReq(SessionCtx.ToCommonHeader(), MaximumSupportingPoints: 12,
                        Dynamic_SEReqControlMode: null,
                        Scheduled_SEReqControlMode: new Scheduled_SEReqControlModeType(null, null, null, null, null))
                        .TryEncode(dest, out int n) ? n : throw EncodeFailed(), ct);
                if (scheduleRes.EVSEProcessing != Processing.Finished)
                    await pollDelay.Wait(PollInterval, ct);
            }
            while (scheduleRes.EVSEProcessing != Processing.Finished);

            await RunPreChargeSequenceAsync(ct);

            // PowerDelivery(Start) must carry an EVPowerProfile referencing a schedule tuple the SECC offered
            // (ISO 15118-20 §7.9.2.4): pick the first tuple from the ScheduleExchangeRes and echo a single
            // power-schedule entry. A live Josev run rejected the earlier absent profile (our SECC didn't).
            var evPowerProfile = BuildEvPowerProfile(scheduleRes);
            await Exchange<PowerDeliveryRes>(MessageSet.Iso20CommonMessages,
                dest => new PowerDeliveryReq(SessionCtx.ToCommonHeader(), Processing.Finished, ChargeProgress.Start, evPowerProfile, null)
                    .TryEncode(dest, out int n) ? n : throw EncodeFailed(), ct);

            for (int cycle = 0; cycle < 3; cycle++)
            {
                await RunChargeLoopIterationAsync(ct);
                await pollDelay.Wait(PollInterval, ct);
            }

            await Exchange<PowerDeliveryRes>(MessageSet.Iso20CommonMessages,
                dest => new PowerDeliveryReq(SessionCtx.ToCommonHeader(), Processing.Finished, ChargeProgress.Stop, null, null)
                    .TryEncode(dest, out int n) ? n : throw EncodeFailed(), ct);

            await RunPostChargeSequenceAsync(ct);

            await Exchange<SessionStopRes>(MessageSet.Iso20CommonMessages,
                dest => new SessionStopReq(SessionCtx.ToCommonHeader(), ChargingSession.Terminate, null, null)
                    .TryEncode(dest, out int n) ? n : throw EncodeFailed(), ct);
        }

        /// <summary>
        /// Picks the authorization mode for this session and returns the AuthorizationReq encoder the poll
        /// loop reuses. Plug &amp; Charge — when <see cref="Pnc"/> is set AND the SECC both offers PnC and sent
        /// a GenChallenge — builds and signs the request <b>once</b> (the challenge does not change across
        /// polls): challenge echo + contract chain in <c>PnC_AReqAuthorizationMode</c> (Id "id1"), and the
        /// header signature over its EXI fragment in Josev's interop form (<see cref="XmlDsigInteropSign"/>).
        /// Everything else falls back to EIM.
        /// </summary>
        private Func<byte[], int> BuildAuthorizationReqEncoder(AuthorizationSetupRes authSetup)
        {
            if (Pnc is { } pnc
                && authSetup.AuthorizationServices.Contains(Authorization.PnC)
                && authSetup.PnC_ASResAuthorizationMode is { } pncSetup)
            {
                var pncMode = new PnC_AReqAuthorizationModeType("id1", pncSetup.GenChallenge,
                    new ContractCertificateChainType(pnc.ContractCertificate,
                        new SubCertificatesType(pnc.SubCertificates.ToArray())));

                var fragment = new byte[8192];
                if (!CommonMessagesCodec.EncodeFragment_PnC_AReqAuthorizationMode(pncMode, fragment, out int fragmentLength))
                    throw EncodeFailed();
                var signature = XmlDsigInteropSign.Sign("id1", fragment.AsSpan(0, fragmentLength), pnc.ContractKey);

                AuthorizationMode = "pnc-signed";
                return dest => new AuthorizationReq(SessionCtx.ToCommonHeader() with { Signature = signature },
                    Authorization.PnC, null, pncMode).TryEncode(dest, out int n) ? n : throw EncodeFailed();
            }

            return dest => new AuthorizationReq(SessionCtx.ToCommonHeader(), Authorization.EIM,
                new EIM_AReqAuthorizationModeType(), null).TryEncode(dest, out int n) ? n : throw EncodeFailed();
        }

        /// <summary>
        /// Sends one already-framed request and awaits its reply, enforcing <paramref name="expectedSet"/>
        /// and <see cref="perMessageTimeout"/>. Used directly by the CommonMessages phases above; DC/AC
        /// subclasses call <see cref="ExchangeRaw"/> instead since they need a different result type.
        /// </summary>
        private async Task<TRes> Exchange<TRes>(MessageSet expectedSet, Func<byte[], int> encode, CancellationToken ct)
        {
            var (set, message) = await ExchangeRaw(expectedSet, encode, ct).ConfigureAwait(false);
            if (message is not TRes reply)
                throw new SessionAborted($"expected a {typeof(TRes).Name} on {expectedSet}, got {message.GetType().Name} on {set}.");
            return reply;
        }

        /// <summary>Same as <see cref="Exchange{TRes}"/> but returns the undiscriminated <see cref="MessageSet"/>/object pair — for DC/AC-specific exchanges.</summary>
        protected async Task<(MessageSet Set, object Message)> ExchangeRaw(MessageSet expectedSet, Func<byte[], int> encode, CancellationToken ct)
        {
            int reqLen = encode(_buf);
            var start = clock.GetUtcNow();
            await V2GTPStream.WriteFrameAsync(stream, expectedSet, _buf.AsMemory(0, reqLen), ct).ConfigureAwait(false);
            var (set, message) = await V2GTPStream.ReadFrameAsync(stream, ct).ConfigureAwait(false);
            var elapsed = clock.GetUtcNow() - start;

            if (elapsed > perMessageTimeout)
                throw new SessionAborted($"no response within {perMessageTimeout.TotalMilliseconds:0} ms (took {elapsed.TotalMilliseconds:0} ms).");

            Exchanges++;
            BytesOnWire += V2GTP.HeaderSize + reqLen;
            return (set, message);
        }

        // ISO 15118-20 energy-transfer service ids (Table 204): AC=1, DC=2, AC_BPT=5, DC_BPT=6.
        private static readonly ushort[] DcServiceIds = { 2, 6 };
        private static readonly ushort[] AcServiceIds = { 1, 5 };

        /// <summary>Picks the energy-transfer service to select from the SECC's advertised list: the first one
        /// whose id matches this EVCC's mode (DC → 2/6, AC → 1/5), else the first offered (a simplified SECC
        /// may advertise a single generic id).</summary>
        private ushort SelectEnergyTransferService(ServiceDiscoveryRes res)
        {
            var offered = res.EnergyTransferServiceList.Service;
            if (offered.Count == 0)
                throw new SessionAborted("ServiceDiscovery: the SECC advertised no energy-transfer service.");

            var preferred = EnergyMode == PowerMode.Dc ? DcServiceIds : AcServiceIds;
            var match = offered.FirstOrDefault(s => preferred.Contains(s.ServiceID));
            return (match ?? offered[0]).ServiceID;
        }

        /// <summary>Picks the parameter set to select from the SECC's ServiceDetail: preferring a Scheduled
        /// control-mode set (ControlMode=1, matching the Scheduled ScheduleExchange the EVCC drives), else the
        /// first offered set.</summary>
        private static ushort SelectParameterSet(ServiceDetailRes res)
        {
            var sets = res.ServiceParameterList.ParameterSet;
            if (sets.Count == 0)
                throw new SessionAborted("ServiceDetail: the SECC advertised no parameter set.");

            var scheduled = sets.FirstOrDefault(p => p.Parameter.Any(x => x.Name == "ControlMode" && x.IntValue == 1));
            return (scheduled ?? sets[0]).ParameterSetID;
        }

        /// <summary>Builds the Scheduled-mode EVPowerProfile that <c>PowerDelivery(Start)</c> must carry: it
        /// selects the first schedule tuple the SECC returned in <c>ScheduleExchangeRes</c> and echoes one
        /// power-schedule entry. Falls back to tuple id 1 if the SECC returned no Scheduled control mode.</summary>
        private static EVPowerProfileType BuildEvPowerProfile(ScheduleExchangeRes scheduleRes)
        {
            uint tupleId = scheduleRes.Scheduled_SEResControlMode?.ScheduleTuple.FirstOrDefault()?.ScheduleTupleID ?? 1;

            return new EVPowerProfileType(
                TimeAnchor: 0,
                Dynamic_EVPPTControlMode: null,
                // PowerToleranceAcceptance is schema-optional but Josev's model requires it (its SECC rejects
                // an absent one); a live run needed it set. PowerToleranceConfirmed = the EV accepts the tolerance.
                Scheduled_EVPPTControlMode: new Scheduled_EVPPTControlModeType(tupleId, PowerToleranceAcceptance.PowerToleranceConfirmed),
                EVPowerProfileEntries: new EVPowerProfileEntryListType(new[]
                {
                    // one 1-hour entry at 10 kW (Power = 10 × 10^3 W)
                    new PowerScheduleEntryType(Duration: 3600, Power: new RationalNumberType(3, 10), Power_L2: null, Power_L3: null),
                }));
        }

        private static InvalidOperationException EncodeFailed() => new("EXI encode failed (buffer too small?).");
    }
}
