/*
 * Copyright (c) 2021-2025 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of WWCP ISO/IEC 15118 <https://github.com/OpenChargingCloud/WWCP_ISO15118>
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Linq;
using System.Net;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated;
using cloud.charging.open.protocols.ISO15118.Sap;
using cloud.charging.open.protocols.ISO15118.Session;
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso20;
using ISO15118ConformanceTests.Simulation.Timing;
using cloud.charging.open.protocols.ISO15118.Transport;
using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;

using Ac20 = cloud.charging.open.protocols.ISO15118_20.AC.Generated;
using Dc20 = cloud.charging.open.protocols.ISO15118_20.DC.Generated;

// System.Net (for IPEndPoint) brings its own Authorization; name the one we mean.
using Authorization = cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated.Authorization;

namespace ISO15118ConformanceTests.Simulation.StateMachines
{

    /// <summary>
    /// Our EVCC driving a <b>bidirectional</b> (BPT) session, and our SECC refusing one that only claims to be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The counterpart of <see cref="Secc20AcBptTests"/>, and — exactly like
    /// <see cref="Evcc20DynamicModeTests"/> before it — it arrived long after the station side for a reason
    /// worth recording. Our station has answered BPT charge-parameter requests in kind since 2026-07-22, but
    /// no <c>Evcc20*</c> ever <i>sent</i> one: the bidirectional work had been done from the end where the
    /// direction is driven by what the EV says, and what our EV said was always charge-only. The gap was
    /// invisible in loopback because our station never asked the EV to be consistent either.
    /// </para>
    /// <para>
    /// It took a second implementation to show it. Against everest-core 2026.02.1 our EVCC selected MCS_BPT
    /// (service 9), sent charge-only parameters under it, and was answered
    /// <c>FAILED_WrongChargeParameter</c> after eight exchanges — correctly, because ISO 15118-20 carries the
    /// direction in the polymorphic type and the selected service binds every message that follows
    /// (<c>docs/interop-runs/2026-08-05-everest-mcs-bpt/</c>).
    /// </para>
    /// <para>
    /// So the assertions come in two halves: that the EV now asks in kind on the direction axis, per message
    /// and per control mode; and that our station now refuses the session EVerest refused, in both
    /// directions of mismatch. The negatives matter as much as the positives — a
    /// <c>BidirectionalService</c> read as "always true" would pass every positive here.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class Evcc20BidirectionalTests
    {

        #region Stations

        /// <summary>A station that keeps every request it was handed, so the test can assert on what the EV
        /// really sent rather than on the session merely completing.</summary>
        private class RecordingSecc20Dc(TimeSpan sequenceTimeout, TimeProvider clock)
            : Secc20Dc(sequenceTimeout, clock)
        {
            public List<object> Requests { get; } = [];

            public override (MessageSet Set, object Response) Handle(MessageSet set, object request)
            {
                Requests.Add(request);
                return base.Handle(set, request);
            }
        }

        private class RecordingSecc20Ac(TimeSpan sequenceTimeout, TimeProvider clock)
            : Secc20Ac(sequenceTimeout, clock)
        {
            public List<object> Requests { get; } = [];

            public override (MessageSet Set, object Response) Handle(MessageSet set, object request)
            {
                Requests.Add(request);
                return base.Handle(set, request);
            }
        }

        /// <summary>A station advertising a single, bidirectional service — so the EV lands on it whatever its
        /// own ranking says. Deliberately not done by reordering a two-entry catalogue: which of two offered
        /// ids an EVCC picks is a separate question with its own history, and these tests are about what it
        /// sends once it has picked, not about how it picked.</summary>
        private sealed class DcBptOnlySecc(TimeSpan sequenceTimeout, TimeProvider clock)
            : RecordingSecc20Dc(sequenceTimeout, clock)
        {
            protected override IReadOnlyList<ushort> EnergyServiceIds => [EnergyTransferService.DC_BPT];
        }

        /// <summary>Likewise for MCS_BPT (9). A DC station by message set — which is all MCS is — so that the
        /// megawatt EV meets service 9 and nothing else.</summary>
        private sealed class McsBptOnlySecc(TimeSpan sequenceTimeout, TimeProvider clock)
            : RecordingSecc20Dc(sequenceTimeout, clock)
        {
            protected override IReadOnlyList<ushort> EnergyServiceIds => [EnergyTransferService.MCS_BPT];
        }

        private sealed class AcBptOnlySecc(TimeSpan sequenceTimeout, TimeProvider clock)
            : RecordingSecc20Ac(sequenceTimeout, clock)
        {
            protected override IReadOnlyList<ushort> EnergyServiceIds => [EnergyTransferService.AC_BPT];
        }

        #endregion

        private static async Task<T> RunSessionAsync<T>(Func<TimeSpan, TimeProvider, T> makeSecc,
                                                        Func<Stream, Evcc20Base> makeEvcc,
                                                        CancellationToken ct)
            where T : Secc20Base
        {
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var secc = makeSecc(TimeSpan.FromSeconds(60), TimeProvider.System);

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(ct);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, ct);
                try { await secc.RunAsync(seccStream, ct); }
                catch { /* the EV may hang up first; the assertions are on what was received */ }
            }, ct);

            using (var evccStream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port,
                                                                     (TlsOptions?) null, ct))
            {
                await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, ct);
                await makeEvcc(evccStream).RunAsync(ct);
            }

            await seccTask;
            return secc;
        }


        #region The EV asks in kind

        /// <summary>Scheduled mode under DC_BPT: both the charge-parameter request and every charge-loop
        /// request carry the BPT subtype, and the discharge envelope is actually filled in. The type alone
        /// would not be enough — <c>BPT_Scheduled</c>'s discharge fields are all optional, so a BPT request
        /// naming none of them tells the station nothing the charge-only type would not have.</summary>
        [Test]
        public async Task Dc_BptService_Scheduled_AsksInKindWithDischargeLimits()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var secc = await RunSessionAsync(
                (t, c) => new DcBptOnlySecc(t, c),
                stream => new Evcc20Dc(stream, TimeProvider.System, new ImmediateAsyncDelay(),
                                       LoopbackTimeouts.PerMessage),
                cts.Token);

            var selection = secc.Requests.OfType<ServiceSelectionReq>().Single();
            var cpd       = secc.Requests.OfType<Dc20.DC_ChargeParameterDiscoveryReq>().Single();
            var loop      = secc.Requests.OfType<Dc20.DC_ChargeLoopReq>().First();

            Assert.Multiple(() =>
            {
                Assert.That(secc.IsDone, Is.True, "the session reached its terminal state");
                Assert.That(selection.SelectedEnergyTransferService.ServiceID, Is.EqualTo(EnergyTransferService.DC_BPT));

                Assert.That(cpd.DC_CPDReqEnergyTransferMode, Is.InstanceOf<Dc20.BPT_DC_CPDReqEnergyTransferModeType>(),
                            "charge-parameter discovery asked in the bidirectional type");
                Assert.That(((Dc20.BPT_DC_CPDReqEnergyTransferModeType) cpd.DC_CPDReqEnergyTransferMode).EVMaximumDischargePower,
                            Is.Not.Null, "…and named a discharge envelope");

                Assert.That(loop.CLReqControlMode, Is.InstanceOf<Dc20.BPT_Scheduled_DC_CLReqControlModeType>(),
                            "the charge loop asked in Scheduled *and* bidirectional");
                Assert.That(((Dc20.BPT_Scheduled_DC_CLReqControlModeType) loop.CLReqControlMode).EVMaximumDischargePower,
                            Is.Not.Null, "…and named a discharge limit, which BPT_Scheduled leaves optional");
            });
        }


        /// <summary>Dynamic mode under DC_BPT — the other control mode, because the two axes are independent
        /// and a switch that got three of four arms right would still complete a session.</summary>
        [Test]
        public async Task Dc_BptService_Dynamic_AsksInKind()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var secc = await RunSessionAsync(
                (t, c) => new DcBptOnlySecc(t, c),
                stream => new Evcc20Dc(stream, TimeProvider.System, new ImmediateAsyncDelay(),
                                       LoopbackTimeouts.PerMessage) { PreferDynamicControlMode = true },
                cts.Token);

            var cpd  = secc.Requests.OfType<Dc20.DC_ChargeParameterDiscoveryReq>().Single();
            var loop = secc.Requests.OfType<Dc20.DC_ChargeLoopReq>().First();

            Assert.Multiple(() =>
            {
                Assert.That(secc.IsDone, Is.True);
                Assert.That(cpd.DC_CPDReqEnergyTransferMode, Is.InstanceOf<Dc20.BPT_DC_CPDReqEnergyTransferModeType>());
                Assert.That(loop.CLReqControlMode, Is.InstanceOf<Dc20.BPT_Dynamic_DC_CLReqControlModeType>(),
                            "the charge loop asked in Dynamic *and* bidirectional");
            });
        }


        /// <summary>MCS_BPT (service 9) end to end — the session everest-core 2026.02.1 refused at exchange 8,
        /// now run to completion in loopback. <see cref="Evcc20Mcs"/> inherits the DC hooks unchanged, so what
        /// this adds over the DC cases is that a megawatt EV landing on service 9 reads it as bidirectional.</summary>
        [Test]
        public async Task Mcs_BptService_RunsToCompletion()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var secc = await RunSessionAsync(
                (t, c) => new McsBptOnlySecc(t, c),
                stream => new Evcc20Mcs(stream, TimeProvider.System, new ImmediateAsyncDelay(),
                                        LoopbackTimeouts.PerMessage),
                cts.Token);

            var selection = secc.Requests.OfType<ServiceSelectionReq>().Single();
            var cpd       = secc.Requests.OfType<Dc20.DC_ChargeParameterDiscoveryReq>().Single();

            Assert.Multiple(() =>
            {
                Assert.That(secc.IsDone, Is.True, "the session completed rather than stopping at charge parameters");
                Assert.That(selection.SelectedEnergyTransferService.ServiceID, Is.EqualTo(EnergyTransferService.MCS_BPT));
                Assert.That(cpd.DC_CPDReqEnergyTransferMode, Is.InstanceOf<Dc20.BPT_DC_CPDReqEnergyTransferModeType>());
            });
        }


        /// <summary>The AC counterpart, under AC_BPT (service 5).</summary>
        [Test]
        public async Task Ac_BptService_AsksInKind()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var secc = await RunSessionAsync(
                (t, c) => new AcBptOnlySecc(t, c),
                stream => new Evcc20Ac(stream, TimeProvider.System, new ImmediateAsyncDelay(),
                                       LoopbackTimeouts.PerMessage),
                cts.Token);

            var selection = secc.Requests.OfType<ServiceSelectionReq>().Single();
            var cpd       = secc.Requests.OfType<Ac20.AC_ChargeParameterDiscoveryReq>().Single();
            var loop      = secc.Requests.OfType<Ac20.AC_ChargeLoopReq>().First();

            Assert.Multiple(() =>
            {
                Assert.That(secc.IsDone, Is.True);
                Assert.That(selection.SelectedEnergyTransferService.ServiceID, Is.EqualTo(EnergyTransferService.AC_BPT));
                Assert.That(cpd.AC_CPDReqEnergyTransferMode, Is.InstanceOf<Ac20.BPT_AC_CPDReqEnergyTransferModeType>());
                Assert.That(loop.CLReqControlMode, Is.InstanceOf<Ac20.BPT_Scheduled_AC_CLReqControlModeType>());
            });
        }


        /// <summary>The negative the four above need: against an ordinary station the EV lands on plain DC and
        /// stays unidirectional, end to end. Without this, a <c>BidirectionalService</c> stuck at <c>true</c>
        /// would pass every positive assertion in this fixture.</summary>
        [Test]
        public async Task Dc_PlainService_StaysUnidirectional()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var secc = await RunSessionAsync(
                (t, c) => new RecordingSecc20Dc(t, c),
                stream => new Evcc20Dc(stream, TimeProvider.System, new ImmediateAsyncDelay(),
                                       LoopbackTimeouts.PerMessage),
                cts.Token);

            var selection = secc.Requests.OfType<ServiceSelectionReq>().Single();
            var cpd       = secc.Requests.OfType<Dc20.DC_ChargeParameterDiscoveryReq>().Single();
            var loop      = secc.Requests.OfType<Dc20.DC_ChargeLoopReq>().First();

            Assert.Multiple(() =>
            {
                Assert.That(secc.IsDone, Is.True);
                Assert.That(selection.SelectedEnergyTransferService.ServiceID, Is.EqualTo(EnergyTransferService.DC));
                Assert.That(cpd.DC_CPDReqEnergyTransferMode, Is.Not.InstanceOf<Dc20.BPT_DC_CPDReqEnergyTransferModeType>(),
                            "a plain DC session must not declare a discharge envelope");
                Assert.That(loop.CLReqControlMode, Is.Not.InstanceOf<Dc20.BPT_Scheduled_DC_CLReqControlModeType>());
            });
        }

        #endregion

        #region The station refuses a direction that contradicts the selected service

        private readonly SessionContext _ctx = new(TimeProvider.System);
        private MessageHeaderType Common => _ctx.ToCommonHeader();
        private Dc20.MessageHeaderType Dc => _ctx.ToDcHeader();
        private static Dc20.RationalNumberType Rat(short value, sbyte exponent = 0) => new(exponent, value);

        /// <summary>Drives a station through the CommonMessages phases and selects <paramref name="serviceId"/>,
        /// leaving it at charge-parameter discovery.</summary>
        private Secc20Dc StationWithServiceSelected(ushort serviceId)
        {
            var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
            secc.Handle(MessageSet.Iso20CommonMessages, new SessionSetupReq(Common, "EVCC01"));
            secc.Handle(MessageSet.Iso20CommonMessages, new AuthorizationSetupReq(Common));
            secc.Handle(MessageSet.Iso20CommonMessages, new AuthorizationReq(Common, Authorization.EIM, new EIM_AReqAuthorizationModeType(), null));
            secc.Handle(MessageSet.Iso20CommonMessages, new ServiceDiscoveryReq(Common, null));
            secc.Handle(MessageSet.Iso20CommonMessages, new ServiceDetailReq(Common, serviceId));
            secc.Handle(MessageSet.Iso20CommonMessages, new ServiceSelectionReq(Common, new SelectedServiceType(serviceId, 1), null));
            return secc;
        }

        private Dc20.DC_CPDReqEnergyTransferModeType ChargeOnlyParameters()
            => new(EVMaximumChargePower: Rat(5_000, 1), EVMinimumChargePower: Rat(0),
                   EVMaximumChargeCurrent: Rat(200), EVMinimumChargeCurrent: Rat(0),
                   EVMaximumVoltage: Rat(500), EVMinimumVoltage: Rat(50), TargetSOC: 80);

        private Dc20.BPT_DC_CPDReqEnergyTransferModeType BidirectionalParameters()
            => new(EVMaximumChargePower: Rat(5_000, 1), EVMinimumChargePower: Rat(0),
                   EVMaximumChargeCurrent: Rat(200), EVMinimumChargeCurrent: Rat(0),
                   EVMaximumVoltage: Rat(500), EVMinimumVoltage: Rat(50), TargetSOC: 80,
                   EVMaximumDischargePower: Rat(5_000, 1), EVMinimumDischargePower: Rat(0),
                   EVMaximumDischargeCurrent: Rat(200), EVMinimumDischargeCurrent: Rat(0));

        /// <summary>The exact exchange everest-core 2026.02.1 refused: DC_BPT negotiated, charge-only
        /// parameters sent under it. Until this check existed our station served that session to completion.
        /// The session ends here, as it does on their side — a FAILED response is the station saying it is
        /// done.</summary>
        [Test]
        public void ChargeOnlyParametersUnderABptService_AreRefusedByName()
        {
            var secc = StationWithServiceSelected(EnergyTransferService.DC_BPT);

            var res = (Dc20.DC_ChargeParameterDiscoveryRes) secc.Handle(MessageSet.Iso20DC,
                new Dc20.DC_ChargeParameterDiscoveryReq(Dc, ChargeOnlyParameters())).Response;

            Assert.Multiple(() =>
            {
                Assert.That(res.ResponseCode, Is.EqualTo(Dc20.ResponseCode.FAILED_WrongChargeParameter));
                Assert.That(secc.IsDone, Is.True, "a FAILED response ends the session; there is no phase to advance to");
            });
        }

        /// <summary>The mismatch the other way round — bidirectional parameters under a plain DC service. Same
        /// rule, and worth its own case: a check written as "BPT service requires BPT parameters" passes this
        /// one by accident.</summary>
        [Test]
        public void BidirectionalParametersUnderAPlainService_AreRefusedByName()
        {
            var secc = StationWithServiceSelected(EnergyTransferService.DC);

            var res = (Dc20.DC_ChargeParameterDiscoveryRes) secc.Handle(MessageSet.Iso20DC,
                new Dc20.DC_ChargeParameterDiscoveryReq(Dc, BidirectionalParameters())).Response;

            Assert.Multiple(() =>
            {
                Assert.That(res.ResponseCode, Is.EqualTo(Dc20.ResponseCode.FAILED_WrongChargeParameter));
                Assert.That(secc.IsDone, Is.True);
            });
        }

        /// <summary>And the agreeing case still passes, so the check refuses mismatches rather than BPT.</summary>
        [Test]
        public void BidirectionalParametersUnderABptService_AreAccepted()
        {
            var secc = StationWithServiceSelected(EnergyTransferService.DC_BPT);

            var res = (Dc20.DC_ChargeParameterDiscoveryRes) secc.Handle(MessageSet.Iso20DC,
                new Dc20.DC_ChargeParameterDiscoveryReq(Dc, BidirectionalParameters())).Response;

            Assert.Multiple(() =>
            {
                Assert.That(res.ResponseCode, Is.EqualTo(Dc20.ResponseCode.OK));
                Assert.That(res.DC_CPDResEnergyTransferMode, Is.InstanceOf<Dc20.BPT_DC_CPDResEnergyTransferModeType>(),
                            "answered in kind, with discharge limits");
                Assert.That(secc.IsDone, Is.False, "the session goes on");
            });
        }

        #endregion

    }
}
