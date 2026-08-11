/*
 * Copyright (c) 2021-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
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

using cloud.charging.open.protocols.ISO15118.Sap;
using cloud.charging.open.protocols.ISO15118.Session;
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso20;
using cloud.charging.open.protocols.ISO15118.Transport;
using ISO15118ConformanceTests.Simulation.Timing;

namespace ISO15118ConformanceTests.Simulation.E2E
{
    /// <summary>
    /// The ISO 15118-20 charge-loop sequence timeout, on our own station. Tables 216 (AC) and 217 (DC),
    /// obliged by <c>[V2G20-1500]</c>/<c>[V2G20-1502]</c>, put <c>V2G_SECC_Sequence_Timeout</c> at
    /// <b>0,5 s</b> once a charge-loop response is out — the phase in which the contactor is closed —
    /// against Table 215's 60 s baseline for every other message. A silent EV must not hold a closed
    /// contactor for a minute.
    ///
    /// <para>
    /// This is the shape of the defect we filed against EVerest's <c>libiso15118</c> on 2026-08-11
    /// (<c>docs/reports/everest-d20-sequence-timeout.md</c>, measured at 60,0025 s) — and until
    /// <see cref="Secc20Base.ChargeLoopSequenceTimeout"/> our own <c>Secc20Base</c> flattened it too.
    /// The instrument is <see cref="Evcc20Base.GoSilentInChargeLoop"/>, built to measure theirs; here it
    /// measures ours over a loopback. Restoring the flat 60 s (the neutered control) makes the tight test
    /// fail, which is how the fix is checked.
    /// </para>
    /// </summary>
    [TestFixture]
    public class Iso20ChargeLoopTimeoutTests
    {

        /// <summary>
        /// With the standard 0,5 s in force, an EV that answers one charge-loop response and then goes
        /// silent is dropped by the station well inside a second — not held for the 60 s baseline.
        /// </summary>
        [Test]
        public async Task SilentEvInChargeLoop_StationGivesUpInUnderASecond()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);

                // ChargeLoopSequenceTimeout defaults to the standard 0,5 s.
                var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(seccStream, cts.Token);
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync(
                IPAddress.Loopback.ToString(), listener.LocalEndpoint.Port, ct: cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

            var evcc = new Evcc20Dc(evccStream, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage)
            {
                // One charge-loop iteration (so the station has answered a ChargeLoopRes and armed the
                // timer), then silence with the socket held open, for up to 5 s.
                GoSilentInChargeLoop = TimeSpan.FromSeconds(5),
            };
            await evcc.RunAsync(cts.Token);

            // The station gave up on its own; RunAsync surfaces that as the sequence-timeout abort.
            Assert.That(async () => await seccTask, Throws.TypeOf<SessionAborted>());

            Assert.That(evcc.SilenceEndedAfter, Is.Not.Null,
                "the station must end the session on its own, not wait out the 5 s silence budget");
            Assert.That(evcc.SilenceEndedAfter!.Value, Is.LessThan(TimeSpan.FromSeconds(2)),
                "0,5 s per Tables 216/217 — a value near 60 s would mean the timeout is still flat");
        }

        /// <summary>
        /// The neutered control: restore the flat 60 s in the charge loop and the same silent EV is
        /// <i>not</i> dropped — the station holds the session through the whole silence budget. This is the
        /// behaviour the fix removes, and it must fail the assertion above.
        /// </summary>
        [Test]
        public async Task WithFlatTimeout_StationHoldsThroughTheSilenceBudget()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);

                // Flat 60 s everywhere, including the charge loop — the pre-fix behaviour.
                var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System)
                {
                    ChargeLoopSequenceTimeout = TimeSpan.FromSeconds(60),
                };
                await secc.RunAsync(seccStream, cts.Token);
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync(
                IPAddress.Loopback.ToString(), listener.LocalEndpoint.Port, ct: cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

            var evcc = new Evcc20Dc(evccStream, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage)
            {
                GoSilentInChargeLoop = TimeSpan.FromSeconds(2),
            };
            await evcc.RunAsync(cts.Token);

            Assert.That(evcc.SilenceEndedAfter, Is.Null,
                "with a flat 60 s the station holds the session past the 2 s budget — the defect the fix removes");

            // Tear the still-blocked station read down; the cancellation is expected, not a failure.
            cts.Cancel();
            Assert.That(async () => await seccTask,
                Throws.InstanceOf<OperationCanceledException>().Or.TypeOf<SessionAborted>());
        }

    }
}
