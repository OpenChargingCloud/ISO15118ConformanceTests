/*
 * Copyright (c) 2021-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of WWCP ISO/IEC 15118 <https://github.com/OpenChargingCloud/WWCP_ISO15118>
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

using System.Diagnostics;
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
    /// <c>V2G_EVCC_Msg_Timeout</c> — how long our <b>car</b> waits for the station's answer. The mirror of
    /// <see cref="Iso20ChargeLoopTimeoutTests"/>, with the roles swapped, and the second half of the same
    /// reading of Tables 215–218.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Table 215 gives 2 s for ordinary messages, and Tables 216/217/218 tighten
    /// <c>{AC,DC,WPT}_ChargeLoopReq</c> to <b>0,5 s</b> — the phase in which the contactor is closed
    /// (<c>[V2G20-1499]</c>, <c>[V2G20-1501]</c>, <c>[V2G20-5069]</c>). <see cref="Evcc20Base"/> applied one
    /// flat <c>perMessageTimeout</c> to every exchange until 2026-08-11.
    /// </para>
    /// <para>
    /// <b>The second defect was the worse one, and it is what these tests are really about.</b>
    /// <c>ExchangeRaw</c> awaited the read with no budget and compared the elapsed time <em>afterwards</em>,
    /// so the timeout could only ever catch an answer that arrived <b>late</b>. A station that simply
    /// stopped answering held our car until the session-level token fired — minutes in a live run, and
    /// forever without one. The station side had the identical shape and was fixed the same morning; this
    /// is the other half of the wire.
    /// </para>
    /// <para>
    /// Measuring it needed an instrument that did not exist: a station that goes quiet while
    /// <b>holding the socket open</b>. A station that hangs up is an EOF, and an EOF ends the read whatever
    /// the timeout does — which is exactly why the old code looked fine.
    /// <see cref="Secc20Base.GoSilentInChargeLoop"/> is the mirror of the EVCC knob built for the station's
    /// timer the same day.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class Iso20MsgTimeoutTests
    {

        /// <summary>
        /// The station answers one charge loop and then stops, socket open. With the standard 0,5 s the car
        /// gives up well inside a second — and it gives up <i>at all</i>, which is the part that was broken.
        /// </summary>
        [Test]
        public async Task SilentStationInChargeLoop_CarGivesUpInUnderASecond()
        {
            using var cts      = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System)
            {
                // Answer the loop once, then hold the socket and say nothing for up to 10 s.
                GoSilentInChargeLoop = TimeSpan.FromSeconds(10),
            };

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);
                await secc.RunAsync(seccStream, cts.Token);
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync(
                IPAddress.Loopback.ToString(), listener.LocalEndpoint.Port, ct: cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

            // ChargeLoopMsgTimeout defaults to the standard 0,5 s. The baseline stays loopback-generous,
            // so nothing before the charge loop is under time pressure and only the tightened value is
            // under test.
            var evcc = new Evcc20Dc(evccStream, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage);

            var watch = Stopwatch.StartNew();
            var abort = Assert.ThrowsAsync<SessionAborted>(async () => await evcc.RunAsync(cts.Token));
            watch.Stop();

            Assert.Multiple(() =>
            {
                Assert.That(abort!.Message, Does.Contain("no response within"),
                            "the car must end on its own message timeout, not on the outer token");
                Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)),
                            "0,5 s per Tables 216/217/218 — anything near the 10 s silence budget means the "
                            + "car waited for the station to hang up instead of enforcing its own timer");
            });

            // A car that has given up closes the connection; here the fixture owns the socket, so it does
            // it explicitly. Without this the station sits out its whole silence budget and the test pays
            // for it in wall-clock — which is worth knowing rather than hiding behind a shorter budget.
            evccStream.Dispose();

            await seccTask;
            Assert.That(secc.SilenceEndedAfter, Is.Not.Null,
                        "the station saw the car close the connection, which is how a car gives up");
        }


        /// <summary>
        /// The neutered control: put the flat baseline back in the charge loop and the same silent station
        /// is <b>not</b> caught inside a second — the car waits out the station's whole silence budget. This
        /// is the behaviour the fix removes, and it must fail the assertion above.
        /// </summary>
        [Test]
        public async Task WithFlatMsgTimeout_TheCarWaitsForTheStationInstead()
        {
            using var cts      = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System)
            {
                GoSilentInChargeLoop = TimeSpan.FromSeconds(2),
            };

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);
                await secc.RunAsync(seccStream, cts.Token);
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync(
                IPAddress.Loopback.ToString(), listener.LocalEndpoint.Port, ct: cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

            var evcc = new Evcc20Dc(evccStream, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage)
            {
                // The pre-fix behaviour: the loopback's slack baseline everywhere, charge loop included.
                ChargeLoopMsgTimeout = LoopbackTimeouts.PerMessage,
            };

            var watch = Stopwatch.StartNew();
            var ended = Assert.CatchAsync(async () => await evcc.RunAsync(cts.Token));
            watch.Stop();

            Assert.Multiple(() =>
            {
                Assert.That(watch.Elapsed, Is.GreaterThan(TimeSpan.FromSeconds(1.5)),
                            "with a flat budget the car sits through the station's 2 s silence — the defect the fix removes");

                // And this is the shape of the defect, not merely its duration: what ends the session is
                // the station's socket closing when *its* budget expires, so the car's own timer never
                // decided anything. Hence the exception is a truncated-frame read rather than a timeout.
                Assert.That(ended, Is.Not.InstanceOf<SessionAborted>(),
                            "the car was ended by the peer's EOF, not by its own message timeout");
            });

            await seccTask;
        }


        /// <summary>
        /// The default path is untouched: an ordinary session still charges to completion, and the two
        /// deviating budgets are the documented ones. <b>This passes on the pre-fix car as well</b> — it
        /// pins that tightening the charge loop did not start aborting healthy sessions, which is the
        /// failure mode a per-message table has.
        /// </summary>
        [Test]
        public async Task AnOrdinarySession_IsUnaffected()
        {
            using var cts      = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);
                await new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System).RunAsync(seccStream, cts.Token);
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync(
                IPAddress.Loopback.ToString(), listener.LocalEndpoint.Port, ct: cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

            var evcc = new Evcc20Dc(evccStream, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage);

            Assert.DoesNotThrowAsync(async () => await evcc.RunAsync(cts.Token));

            Assert.Multiple(() =>
            {
                Assert.That(evcc.Exchanges,            Is.GreaterThan(10), "a complete DC session, not an early abort");
                Assert.That(evcc.ChargeLoopMsgTimeout, Is.EqualTo(TimeSpan.FromMilliseconds(500)),
                            "Tables 216/217/218");
                Assert.That(evcc.SlowMsgTimeout,       Is.EqualTo(TimeSpan.FromSeconds(5)),
                            "Table 215: CertificateInstallationReq and ServiceDetailReq get 5 s, not 2");
            });

            await seccTask;
        }

    }
}
