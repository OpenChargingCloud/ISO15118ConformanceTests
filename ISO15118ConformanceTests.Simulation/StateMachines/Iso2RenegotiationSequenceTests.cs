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

using cloud.charging.open.protocols.ISO15118.Sap;
using cloud.charging.open.protocols.ISO15118.Session;
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso2;
using cloud.charging.open.protocols.ISO15118.Transport;
using ISO15118ConformanceTests.Simulation.Timing;

namespace ISO15118ConformanceTests.Simulation.StateMachines
{

    /// <summary>
    /// A <b>DC</b> renegotiation returns through <c>CableCheck</c> and <c>PreCharge</c> — both sides.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ISO 15118-2's SECC state table for DC gives <c>Process ChargeParameterDiscoveryReq</c> exactly one
    /// successor, *Wait for CableCheckReq* (`[V2G2-565]`, `[V2G2-582]`), and carries no renegotiation
    /// exception. Until 2026-08-15 <see cref="Evcc2"/> went straight from the renegotiated
    /// <c>ChargeParameterDiscovery</c> to <c>PowerDelivery(Start)</c> and <see cref="Secc2"/> expected
    /// exactly that, so the loopback agreed with itself and neither side was ever asked.
    /// </para>
    /// <para>
    /// <b>It took a counterparty being right to find it.</b> EVerest's <c>EvseV2G</c> answered our short
    /// sequence <c>FAILED_SequenceError</c> on 2026-08-11; that was filed as their defect and
    /// <b>withdrawn</b> four days later, when the filing's own document gate was worked and the DC state
    /// table settled it. See <c>docs/reports/everest-evsev2g-renegotiation-cablecheck.md</c> and
    /// <c>docs/normative-basis.md</c>. AC is untouched: it has no isolation phase at all, and the annex
    /// sequence the withdrawn report leaned on was the AC one.
    /// </para>
    /// <para>
    /// <b>Which of these fail on the pre-fix code</b>, since that is the only thing that makes a fixture
    /// worth reading: the first three. <see cref="Ac_Renegotiation_HasNoIsolationSequence"/> passes either
    /// way and is here so that a fix aimed at DC cannot quietly grow an AC CableCheck — which is the exact
    /// mistake the withdrawn report made in the other direction.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class Iso2RenegotiationSequenceTests
    {

        /// <summary>Both sides conformant: one isolation sequence on the way in, one on the way back, and
        /// the session completes.</summary>
        [Test]
        public async Task Dc_Renegotiation_ReturnsThroughCableCheckAndPreCharge()
        {

            var (secc, evccError) = await RunAsync(PowerMode.Dc);

            Assert.Multiple(() =>
            {
                Assert.That(secc.IsolationSequences, Is.EqualTo(2),
                            "one CableCheck on the way in and one after the renegotiation — the second is "
                          + "the whole fix, and on the pre-fix code it is 1");
                Assert.That(secc.Renegotiations, Is.EqualTo(1));
                Assert.That(secc.SequenceErrorAt, Is.Null, "nothing was out of order");
                Assert.That(secc.IsDone, Is.True, "the session still finishes normally");
                Assert.That(evccError, Is.Null);
            });

        }


        /// <summary>A car that skips it — the car we shipped until 2026-08-15, and the one EVerest refused
        /// — is refused by our station too, in the response that pairs with its request.</summary>
        [Test]
        public async Task Dc_ACarThatSkipsTheIsolationSequence_IsRefused()
        {

            var (secc, evccError) = await RunAsync(PowerMode.Dc, carSkipsIsolation: true);

            Assert.Multiple(() =>
            {
                Assert.That(secc.SequenceErrorAt, Is.EqualTo("PowerDeliveryReq"),
                            "the station is waiting for CableCheckReq and the car sent PowerDeliveryReq");
                Assert.That(secc.IsolationSequences, Is.EqualTo(1), "only the one on the way in");
                Assert.That(evccError, Is.Not.Null, "the car sees a FAILED code and ends the session");
                Assert.That(evccError!.Message, Does.Contain("FAILED_SequenceError"),
                            "the same answer EVerest's EvseV2G gave us on 2026-08-11, from our own station");
            });

        }


        /// <summary>And with the station's guard put back to what it was, a <b>conformant</b> car is the one
        /// that fails — which is what makes this a fix of two halves rather than one.</summary>
        [Test]
        public async Task Dc_TheOldStation_CannotServeAConformantCar()
        {

            var (secc, evccError) = await RunAsync(PowerMode.Dc, stationSkipsIsolation: true);

            Assert.Multiple(() =>
            {
                Assert.That(secc.SequenceErrorAt, Is.EqualTo("CableCheckReq"),
                            "the pre-fix station hands a renegotiated session to PowerOn, where a "
                          + "CableCheckReq is out of sequence");
                Assert.That(evccError, Is.Not.Null);
            });

        }


        /// <summary>AC has no isolation phase, and a renegotiation there is unchanged.</summary>
        [Test]
        public async Task Ac_Renegotiation_HasNoIsolationSequence()
        {

            var (secc, evccError) = await RunAsync(PowerMode.Ac);

            Assert.Multiple(() =>
            {
                Assert.That(secc.IsolationSequences, Is.Zero, "AC never runs CableCheck, renegotiation or not");
                Assert.That(secc.Renegotiations, Is.EqualTo(1));
                Assert.That(secc.SequenceErrorAt, Is.Null);
                Assert.That(secc.IsDone, Is.True);
                Assert.That(evccError, Is.Null);
            });

        }


        /// <summary>
        /// One loopback session in which the car opens a renegotiation. Returns the station and whatever
        /// ended the car — a refusal ends the car and then the station's next read fails on a closed
        /// stream, so both are expected outcomes here rather than faults.
        /// </summary>
        private static async Task<(Secc2 Secc, SessionAborted? EvccError)> RunAsync(PowerMode mode,
                                                                                     Boolean carSkipsIsolation = false,
                                                                                     Boolean stationSkipsIsolation = false)
        {

            using var cts      = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var secc = new Secc2(mode, TimeSpan.FromSeconds(60), TimeProvider.System)
                           {
                               RenegotiationNeedsIsolationSequence = !stationSkipsIsolation
                           };

            var seccTask = Task.Run(async () =>
            {
                try
                {
                    using var seccStream = await listener.AcceptAsync(cts.Token);
                    await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_2, cts.Token, mode);
                    await secc.RunAsync(seccStream, cts.Token);
                }
                catch (Exception)
                {
                    // Expected in the refusal arms: the car hangs up on a FAILED code. The station's own
                    // verdict is in SequenceErrorAt, which is what the tests assert on.
                }
            }, cts.Token);

            SessionAborted? evccError = null;

            using (var stream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port,
                                                                (TlsOptions?) null, cts.Token))
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_2, cts.Token, mode);

                var evcc = new Evcc2(stream, mode, TimeProvider.System, new ImmediateAsyncDelay(),
                                     LoopbackTimeouts.PerMessage)
                               {
                                   Renegotiate                        = true,
                                   RenegotiationSkipsIsolationSequence = carSkipsIsolation
                               };

                try
                {
                    await evcc.RunAsync(cts.Token);
                }
                catch (SessionAborted e)
                {
                    evccError = e;
                }
            }

            await seccTask;

            return (secc, evccError);

        }

    }

}
