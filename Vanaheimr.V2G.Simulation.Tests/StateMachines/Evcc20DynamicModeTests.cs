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

using System.Net;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated;
using Vanaheimr.V2G.Simulation.Sap;
using Vanaheimr.V2G.Simulation.Session;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using Vanaheimr.V2G.Simulation.Tests.Timing;
using Vanaheimr.V2G.Simulation.Transport;
using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;

using Ac20 = cloud.charging.open.protocols.ISO15118_20.AC.Generated;
using Dc20 = cloud.charging.open.protocols.ISO15118_20.DC.Generated;

namespace Vanaheimr.V2G.Simulation.Tests.StateMachines
{

    /// <summary>
    /// Our EVCC driving a session in <b>Dynamic</b> control mode.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The counterpart of <see cref="Secc20DynamicModeTests"/>, and it arrived two weeks later for a reason
    /// worth recording: our <i>station</i> has answered Dynamic-mode EVs since 2026-07-22, and our
    /// <i>car</i> could not be one until 2026-08-03. Every Dynamic run in `docs/interop-runs/` had Josev's
    /// EVCC on the other side, so the mode was live-validated in exactly one direction while the roadmap
    /// said "Scheduled and Dynamic" without qualification.
    /// </para>
    /// <para>
    /// What these tests pin is that the mode is a property of the whole session rather than of one message:
    /// the parameter set selected out of ServiceDetailRes, the ScheduleExchange arm, the EVPowerProfile in
    /// PowerDelivery(Start) and the charge-loop arm all have to agree, and the station answers in kind
    /// ([V2G20-1600]). A flag that reached three of the four would produce a session that still completes
    /// against a lenient SECC — so each is asserted separately, on the messages the station actually saw.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class Evcc20DynamicModeTests
    {

        /// <summary>A station that keeps every request it was handed, so the test can assert on what the EV
        /// really sent rather than on the session merely completing.</summary>
        private sealed class RecordingSecc20Dc(TimeSpan sequenceTimeout, TimeProvider clock)
            : Secc20Dc(sequenceTimeout, clock)
        {
            public List<object> Requests { get; } = [];

            public override (MessageSet Set, object Response) Handle(MessageSet set, object request)
            {
                Requests.Add(request);
                return base.Handle(set, request);
            }
        }

        private sealed class RecordingSecc20Ac(TimeSpan sequenceTimeout, TimeProvider clock)
            : Secc20Ac(sequenceTimeout, clock)
        {
            public List<object> Requests { get; } = [];

            public override (MessageSet Set, object Response) Handle(MessageSet set, object request)
            {
                Requests.Add(request);
                return base.Handle(set, request);
            }
        }

        /// <summary>A station that offers Scheduled only — which is legal for an EVSE that does not
        /// implement Dynamic, and the case the EV has to notice rather than charge through.</summary>
        private sealed class ScheduledOnlySecc20Dc(TimeSpan sequenceTimeout, TimeProvider clock)
            : Secc20Dc(sequenceTimeout, clock)
        {
            public override (MessageSet Set, object Response) Handle(MessageSet set, object request)
            {
                var (resSet, response) = base.Handle(set, request);

                if (response is ServiceDetailRes detail)
                {
                    var scheduledOnly = detail.ServiceParameterList.ParameterSet
                        .Where(p => p.Parameter.Any(x => x.Name == "ControlMode" && x.IntValue == 1))
                        .ToArray();
                    return (resSet, detail with { ServiceParameterList = new ServiceParameterListType(scheduledOnly) });
                }

                return (resSet, response);
            }
        }


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


        [Test]
        public async Task Dc_DynamicMode_EveryPhaseAsksInKind()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var secc = await RunSessionAsync(
                (t, c) => new RecordingSecc20Dc(t, c),
                stream => new Evcc20Dc(stream, TimeProvider.System, new ImmediateAsyncDelay(),
                                       LoopbackTimeouts.PerMessage) { PreferDynamicControlMode = true },
                cts.Token);

            var selection = secc.Requests.OfType<ServiceSelectionReq>().Single();
            var schedule  = secc.Requests.OfType<ScheduleExchangeReq>().First();
            var start     = secc.Requests.OfType<PowerDeliveryReq>().First(r => r.ChargeProgress == ChargeProgress.Start);
            var loop      = secc.Requests.OfType<Dc20.DC_ChargeLoopReq>().First();

            Assert.Multiple(() =>
            {
                Assert.That(secc.IsDone, Is.True, "the session reached its terminal state");

                Assert.That(selection.SelectedEnergyTransferService.ParameterSetID, Is.EqualTo(2),
                            "the EV selected the ControlMode = 2 parameter set");

                Assert.That(schedule.Dynamic_SEReqControlMode, Is.Not.Null, "ScheduleExchange asked in Dynamic");
                Assert.That(schedule.Scheduled_SEReqControlMode, Is.Null);
                Assert.That(schedule.Dynamic_SEReqControlMode!.EVTargetEnergyRequest, Is.Not.Null,
                            "the energy triple is mandatory in the Dynamic arm — it is what the station steers against");

                Assert.That(start.EVPowerProfile?.Dynamic_EVPPTControlMode, Is.Not.Null,
                            "PowerDelivery(Start) carried the Dynamic power-profile arm");
                Assert.That(start.EVPowerProfile?.Scheduled_EVPPTControlMode, Is.Null,
                            "…and not a schedule tuple, of which Dynamic mode has none");

                Assert.That(loop.CLReqControlMode, Is.InstanceOf<Dc20.Dynamic_DC_CLReqControlModeType>(),
                            "the charge loop asked in Dynamic");
            });
        }


        [Test]
        public async Task Ac_DynamicMode_EveryPhaseAsksInKind()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var secc = await RunSessionAsync(
                (t, c) => new RecordingSecc20Ac(t, c),
                stream => new Evcc20Ac(stream, TimeProvider.System, new ImmediateAsyncDelay(),
                                       LoopbackTimeouts.PerMessage) { PreferDynamicControlMode = true },
                cts.Token);

            var schedule = secc.Requests.OfType<ScheduleExchangeReq>().First();
            var loop     = secc.Requests.OfType<Ac20.AC_ChargeLoopReq>().First();

            Assert.Multiple(() =>
            {
                Assert.That(secc.IsDone, Is.True, "the session reached its terminal state");
                Assert.That(schedule.Dynamic_SEReqControlMode, Is.Not.Null);
                Assert.That(loop.CLReqControlMode, Is.InstanceOf<Ac20.Dynamic_AC_CLReqControlModeType>());
            });
        }


        /// <summary>The negative, without which the three assertions above would also pass on a flag that is
        /// read nowhere: the default is still Scheduled, end to end.</summary>
        [Test]
        public async Task Dc_WithoutTheFlag_TheSessionStaysScheduled()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var secc = await RunSessionAsync(
                (t, c) => new RecordingSecc20Dc(t, c),
                stream => new Evcc20Dc(stream, TimeProvider.System, new ImmediateAsyncDelay(),
                                       LoopbackTimeouts.PerMessage),
                cts.Token);

            var selection = secc.Requests.OfType<ServiceSelectionReq>().Single();
            var schedule  = secc.Requests.OfType<ScheduleExchangeReq>().First();
            var loop      = secc.Requests.OfType<Dc20.DC_ChargeLoopReq>().First();

            Assert.Multiple(() =>
            {
                Assert.That(secc.IsDone, Is.True);
                Assert.That(selection.SelectedEnergyTransferService.ParameterSetID, Is.EqualTo(1));
                Assert.That(schedule.Scheduled_SEReqControlMode, Is.Not.Null);
                Assert.That(schedule.Dynamic_SEReqControlMode, Is.Null);
                Assert.That(loop.CLReqControlMode, Is.InstanceOf<Dc20.Scheduled_DC_CLReqControlModeType>());
            });
        }


        /// <summary>A station that only offers Scheduled must produce a named refusal, not a session that
        /// negotiates one mode and then asks in the other. The EVCC cannot silently fall back: the parameter
        /// set it selects is what the station answers in kind against for the rest of the session.</summary>
        [Test]
        public void Dc_DynamicAgainstAScheduledOnlyStation_IsRefusedByName()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var thrown = Assert.ThrowsAsync<SessionAborted>(async () => await RunSessionAsync(
                (t, c) => new ScheduledOnlySecc20Dc(t, c),
                stream => new Evcc20Dc(stream, TimeProvider.System, new ImmediateAsyncDelay(),
                                       LoopbackTimeouts.PerMessage) { PreferDynamicControlMode = true },
                cts.Token));

            Assert.Multiple(() =>
            {
                Assert.That(thrown!.Message, Does.Contain("Dynamic"));
                Assert.That(thrown!.Message, Does.Contain("ControlMode"),
                            "the error names what was missing, because that is what a live run is read from");
            });
        }

    }

}
