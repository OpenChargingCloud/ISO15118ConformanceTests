using System.Linq;

using Vanaheimr.V2G.Iso15118_20.CommonMessages.Generated;
using Vanaheimr.V2G.Simulation.Framing;
using Vanaheimr.V2G.Simulation.Session;
using Vanaheimr.V2G.Simulation.Timing;
using Vanaheimr.V2G.Tp;

namespace Vanaheimr.V2G.Simulation.StateMachines.Iso20
{
    /// <summary>
    /// The EVCC side of an ISO 15118-20 session, shared between AC and DC: drives the CommonMessages
    /// phases directly (EIM only), and calls the <c>protected abstract</c> hooks below for the diverging
    /// middle — implemented by <see cref="Evcc20Dc"/>/<see cref="Evcc20Ac"/>, which know which DC/AC codec
    /// and concrete request/response types their energy-transfer mode actually uses.
    /// </summary>
    public abstract class Evcc20Base(
        Stream stream, TimeProvider clock, IAsyncDelay pollDelay, TimeSpan perMessageTimeout)
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

        protected readonly SessionContext SessionCtx = new(clock);
        protected IAsyncDelay PollDelay => pollDelay;
        private readonly byte[] _buf = new byte[1024];

        public int Exchanges { get; private set; }
        public long BytesOnWire { get; private set; }

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

            await Exchange<AuthorizationSetupRes>(MessageSet.Iso20CommonMessages,
                dest => new AuthorizationSetupReq(SessionCtx.ToCommonHeader()).TryEncode(dest, out int n) ? n : throw EncodeFailed(), ct);

            while ((await Exchange<AuthorizationRes>(MessageSet.Iso20CommonMessages,
                       dest => new AuthorizationReq(SessionCtx.ToCommonHeader(), Authorization.EIM,
                           new EIM_AReqAuthorizationModeType(), null).TryEncode(dest, out int n) ? n : throw EncodeFailed(), ct))
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
            while ((await Exchange<ScheduleExchangeRes>(MessageSet.Iso20CommonMessages,
                       dest => new ScheduleExchangeReq(SessionCtx.ToCommonHeader(), MaximumSupportingPoints: 12,
                           Dynamic_SEReqControlMode: null,
                           Scheduled_SEReqControlMode: new Scheduled_SEReqControlModeType(null, null, null, null, null))
                           .TryEncode(dest, out int n) ? n : throw EncodeFailed(), ct))
                   .EVSEProcessing != Processing.Finished)
                await pollDelay.Wait(PollInterval, ct);

            await RunPreChargeSequenceAsync(ct);

            await Exchange<PowerDeliveryRes>(MessageSet.Iso20CommonMessages,
                dest => new PowerDeliveryReq(SessionCtx.ToCommonHeader(), Processing.Finished, ChargeProgress.Start, null, null)
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

        private static InvalidOperationException EncodeFailed() => new("EXI encode failed (buffer too small?).");
    }
}
