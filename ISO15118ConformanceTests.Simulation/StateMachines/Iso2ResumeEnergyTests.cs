/*
 * Copyright (c) 2021-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of ISO15118ConformanceTests
 *
 * Licensed under the Affero GPL license, Version 3.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.gnu.org/licenses/agpl.html
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Net;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118.Sap;
using cloud.charging.open.protocols.ISO15118.Simulation;
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso2;
using cloud.charging.open.protocols.ISO15118.Transport;
using cloud.charging.open.protocols.ISO15118_2;
using cloud.charging.open.protocols.ISO15118_2.Generated;

using ISO15118ConformanceTests.Simulation.Timing;

namespace ISO15118ConformanceTests.Simulation.StateMachines
{

    /// <summary>
    /// <c>[V2G2-743]</c>: an ISO 15118-2 EVCC resuming a paused session sends <c>EAmount</c> reduced by the
    /// energy it already charged. It asks for the remainder, not for the whole thing again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// There are two ways to be wrong about this and we were both. The car's own answer comes from its
    /// pack — charging moves the state of charge, so <c>EnergyNeededWh</c> <i>is</i> the remainder — but the
    /// CLI built a <b>fresh battery per connection</b>, so the resumed session met a pack that had forgotten
    /// the first one. And with no pack at all the request was a constant 22 kWh, twice.
    /// </para>
    /// <para>
    /// <b>Only one of these three tests catches a change to the state machine</b>, and it is worth saying
    /// which rather than leaving the count to imply more than it does. Restore the constant literal and
    /// <see cref="AResumedSession_ReducesTheLiteral_WhenNothingElseRemembers"/> fails; the other two pass
    /// either way. <see cref="AResumedSession_AsksForTheRemainder_WhenThePackIsCarried"/> passes because a
    /// carried pack was always right — it pins the invariant the CLI fix now leans on, and it would
    /// <i>not</i> have caught the CLI rebuilding the pack, which was the real defect. Nothing here covers
    /// that: it lives in <c>WWCP_ISO15118_EVCC/Program.cs</c>, which this suite does not drive.
    /// </para>
    /// <para>
    /// Carries the <c>-2</c> document caveat in <c>docs/normative-basis.md</c>: the text to hand is the 2022
    /// DIS revision and our <c>-2</c> stack targets ISO 15118-2:2014. Low risk here — this is a resume rule
    /// that reads the same in the 2019 manual's account of pause/resume — but it is an argument from one
    /// draft, unlike the <c>-20</c> work beside it.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class Iso2ResumeEnergyTests
    {

        /// <summary>
        /// The car that keeps its pack. Session 1 charges, session 2 rejoins and asks for what is left —
        /// and what is left is what the pack says, which is the whole point of not rebuilding it.
        /// </summary>
        [Test]
        public async Task AResumedSession_AsksForTheRemainder_WhenThePackIsCarried()
        {
            // 60 kWh at 20 %, wanted 30 %: 6 000 Wh on the first ask.
            var pack = new EvBattery(60, 20) { TargetSoC = 30 };

            var (first,  _) = await RunAsync(pack, pause: true);
            var remaining   = pack.EnergyNeededWh;
            var (second, _) = await RunAsync(pack, pause: false, resumeFrom: first.SessionId);

            Assert.Multiple(() =>
            {
                Assert.That(EAmountOf(first),  Is.EqualTo(6_000).Within(1.0),
                            "20 % to 30 % of a 60 kWh pack");
                Assert.That(EAmountOf(second), Is.LessThan(EAmountOf(first)),
                            "the resumed session asked for the full original amount again ([V2G2-743])");
                Assert.That(EAmountOf(second), Is.EqualTo(remaining).Within(1.0),
                            "the ask is the pack's remainder, not a number computed twice");
            });
        }

        /// <summary>
        /// The car with no pack, which cannot be a real car and is how most of this suite runs. Nothing
        /// remembers, so the caller hands the energy across in <c>ResumableSession.DeliveredWh</c> and
        /// <see cref="Evcc2.AlreadyChargedWh"/> takes it off the literal.
        /// </summary>
        [Test]
        public async Task AResumedSession_ReducesTheLiteral_WhenNothingElseRemembers()
        {
            var (first, charged) = await RunAsync(battery: null, pause: true);

            Assert.That(charged, Is.GreaterThan(0), "the paused session metered nothing, so there is nothing to prove");

            var (second, _) = await RunAsync(battery: null, pause: false,
                                             resumeFrom: first.SessionId, alreadyCharged: charged);

            Assert.Multiple(() =>
            {
                Assert.That(EAmountOf(first),  Is.EqualTo(22_000).Within(1.0), "the literal every recorded run carries");
                Assert.That(EAmountOf(second), Is.EqualTo(22_000 - charged).Within(1.0));
            });
        }

        /// <summary>
        /// And a first session is not a resume: with nothing handed over the literal stays exactly what
        /// every recorded <c>-2</c> AC trace in <c>Vectors/</c> carries. The reduction must not leak into
        /// the ordinary case.
        /// </summary>
        [Test]
        public async Task AFirstSession_IsUnchanged()
        {
            var (secc, _) = await RunAsync(battery: null, pause: false);

            Assert.That(EAmountOf(secc), Is.EqualTo(22_000).Within(1.0));
        }

        #region Plumbing

        private static double EAmountOf(RecordingSecc2 secc)
            => (double) secc.Requests.OfType<ChargeParameterDiscoveryReqType>()
                            .Select(r => r.EVChargeParameter).OfType<AC_EVChargeParameterType>()
                            .First().EAmount.ToDecimal();

        /// <summary>One -2 AC connection against a fresh recording station; returns what it received and
        /// what the car's own meter counted.</summary>
        private static async Task<(RecordingSecc2 Secc, double Charged)> RunAsync(EvBattery? battery, bool pause,
                                                                                   byte[]? resumeFrom = null,
                                                                                   double alreadyCharged = 0)
        {
            using var cts      = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var secc = new RecordingSecc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System);

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_2, cts.Token, PowerMode.Ac);
                try { await secc.RunAsync(seccStream, cts.Token); }
                catch { /* the assertions are on what was received */ }
            }, cts.Token);

            Evcc2 evcc;
            using (var stream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port,
                                                                (TlsOptions?) null, cts.Token))
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_2, cts.Token, PowerMode.Ac);
                evcc = new Evcc2(stream, PowerMode.Ac, TimeProvider.System, new ImmediateAsyncDelay(),
                                 LoopbackTimeouts.PerMessage)
                {
                    Battery          = battery,
                    StopMode         = pause ? ChargingSession.Pause : ChargingSession.Terminate,
                    ResumeSessionId  = resumeFrom,
                    AlreadyChargedWh = alreadyCharged,
                };
                await evcc.RunAsync(cts.Token);
            }
            await seccTask;

            return (secc, evcc.Meter.Energy);
        }

        private sealed class RecordingSecc2(PowerMode mode, TimeSpan sequenceTimeout, TimeProvider clock)
            : Secc2(mode, sequenceTimeout, clock)
        {
            public List<BodyBaseType> Requests { get; } = [];

            public override V2G_Message Handle(V2G_Message request)
            {
                if (request.Body.BodyElement is { } body)
                    Requests.Add(body);
                return base.Handle(request);
            }
        }

        #endregion

    }

}
