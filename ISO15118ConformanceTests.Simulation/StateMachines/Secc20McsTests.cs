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
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso20;
using ISO15118ConformanceTests.Simulation.Timing;
using ISO15118ConformanceTests.Simulation.Traces;
using cloud.charging.open.protocols.ISO15118.Transport;
using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;
using cloud.charging.open.protocols.ISO15118_20.DC;

using Dc20 = cloud.charging.open.protocols.ISO15118_20.DC.Generated;

namespace ISO15118ConformanceTests.Simulation.StateMachines
{
    /// <summary>
    /// ISO 15118-20 <b>MCS</b> (Megawatt Charging System) coverage.
    /// <para>
    /// MCS adds no message set and no schema: it is the DC message set advertised under service ids
    /// <b>8 (MCS)</b> / <b>9 (MCS_BPT)</b> with a megawatt power envelope. These tests therefore check the
    /// two things that actually distinguish it — the catalogue and the limits — and that a whole session
    /// still runs over the unchanged DC state machines.
    /// </para>
    /// <para>
    /// <b>The ids are validated, the envelope is not.</b> They come from EVerest's libiso15118, and on
    /// 2026-08-05 three complete sessions against everest-core 2026.02.1's MCS SIL config had their
    /// <c>Evse15118D20</c> read service id 8 back as MCS. The megawatt limits are still untested against a
    /// counterpart — their SIL clamps to its own 22 kW whatever is offered.
    /// </para>
    /// </summary>
    [TestFixture]
    public class Secc20McsTests
    {
        [Test]
        public async Task McsSession_RunsToCompletion_OverTheDcMessageSet()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);
                var secc = new Secc20Mcs(TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(seccStream, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port, (TlsOptions?) null, cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

            var evcc = new Evcc20Mcs(evccStream, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage);
            await Task.WhenAll(evcc.RunAsync(cts.Token), seccTask);

            var secc = await seccTask;
            Assert.Multiple(() =>
            {
                Assert.That(secc.IsDone, Is.True, "the MCS SECC did not reach its terminal phase");
                Assert.That(evcc.Exchanges, Is.GreaterThan(0));
                // The whole point: an MCS session negotiated an MCS service id, not a DC one.
                Assert.That(evcc.SelectedEnergyServiceId, Is.EqualTo(8).Or.EqualTo(9),
                            "the EVCC should have selected an MCS service (8 / 9), not DC's 2 / 6");
            });
        }

        /// <summary>
        /// The EV's ranking decides, not the catalogue's order.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A regression guard for a defect a live run found and this suite could not:
        /// <c>Evcc20Base.SelectEnergyTransferService</c> used to walk the <i>station's</i> list and take the
        /// first entry the EV accepted, so <c>PreferredEnergyServiceIds</c> — documented "best first" —
        /// never ranked anything. Our own <c>Secc20Mcs</c> advertises <c>{ 8, 9 }</c> in that order, which
        /// is exactly the shape EVerest's MCS catalogue has, so the bug reproduces offline: before the fix
        /// this test selects 8.
        /// See <c>docs/interop-runs/2026-08-05-everest-mcs-bpt/notes.md</c>.
        /// </para>
        /// <para>
        /// Note what this session does <i>not</i> prove: it completes, because our SECC answers the
        /// charge-parameter request in kind rather than checking it against the selected service. EVerest's
        /// station refuses the same exchange with <c>FAILED_WrongChargeParameter</c>. The difference is
        /// recorded in the run notes; only the selection is asserted here.
        /// </para>
        /// </remarks>
        [Test]
        public async Task Evcc_RankingDecides_NotTheStationsCatalogueOrder()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);
                var secc = new Secc20Mcs(TimeSpan.FromSeconds(60), TimeProvider.System);   // advertises 8, then 9
                await secc.RunAsync(seccStream, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port, (TlsOptions?) null, cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

            // The same thing the interop fixture asks for: Evcc20Mcs's { 8, 9 }, ranked the other way. This
            // used to be a harness-local `McsBptFirstEvcc` subclass with the list written out reversed;
            // `PreferBidirectionalService` does it for every catalogue, so the subclass is gone.
            var evcc = new Evcc20Mcs(evccStream, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage)
                           { PreferBidirectionalService = true };
            await Task.WhenAll(evcc.RunAsync(cts.Token), seccTask);

            Assert.That(evcc.SelectedEnergyServiceId, Is.EqualTo(9),
                        "the EVCC ranked MCS_BPT (9) first and the station offered it, so 9 is the only "
                      + "answer the EV's own ranking allows — 8 means the station's order won");
        }


        [Test]
        public async Task McsEvcc_DeclaresAMegawattEnvelope_NotADcOne()
        {
            // The regression this exists for. Until 2026-08-05 `Evcc20Mcs` overrode only the service ids —
            // the EV-side limits in `Evcc20Dc` were literals where the station side's were already virtual —
            // so a megawatt truck selected service 8 and then declared an ordinary DC envelope. EVerest
            // 2026.02.1 read it back verbatim ("dc_ev_maximum_power_limit: 50000.0" under an MCS service)
            // and charged anyway, because their SIL clamps to its own 22 kW. Nothing failed, and no test
            // here noticed, because nothing here looked at what the EV actually declared. This does.
            using var cts      = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);
                var secc = new Secc20Mcs(TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(seccStream, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port, (TlsOptions?) null, cts.Token);

            // Wrapped from before the SAP handshake, so the recorded frames pair up the way SessionTrace
            // expects (SAP is exchange 0) and `Sent` is exactly what the vehicle put on the wire.
            var recorder = new RecordingStream(evccStream);
            await SapHandshake.RunEvccSideAsync(recorder, ProtocolVariant.Iso15118_20, cts.Token);

            var evcc = new Evcc20Mcs(recorder, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage);
            await Task.WhenAll(evcc.RunAsync(cts.Token), seccTask);

            var trace = SessionTrace.Build("mcs-envelope", "iso15118-20", "dc",
                                           "the envelope an MCS EVCC declares at charge-parameter discovery",
                                           recorder.Sent, recorder.Received);

            var discovery = trace.Exchanges.SingleOrDefault(e => e.Request.Message == "DC_ChargeParameterDiscoveryReq");
            Assert.That(discovery, Is.Not.Null, "the session sent no DC_ChargeParameterDiscoveryReq");

            Assert.That(V2GTPDispatcher.TryDecode(Convert.FromHexString(discovery!.Request.Frame),
                                                  out _, out var message, out _), Is.True,
                        "the recorded DC_ChargeParameterDiscoveryReq did not decode");

            var declared = ((Dc20.DC_ChargeParameterDiscoveryReq) message!).DC_CPDReqEnergyTransferMode;
            Assert.Multiple(() =>
            {
                // The same figures Secc20Mcs offers — 1250 V x 3000 A = 3.75 MW — asserted as decimals so
                // the check is about the value and not about which (exponent, value) pair encodes it.
                Assert.That(declared.EVMaximumChargePower.ToDecimal(),   Is.EqualTo(3_750_000m), "EVMaximumChargePower");
                Assert.That(declared.EVMaximumChargeCurrent.ToDecimal(), Is.EqualTo(    3_000m), "EVMaximumChargeCurrent");
                Assert.That(declared.EVMaximumVoltage.ToDecimal(),       Is.EqualTo(    1_250m), "EVMaximumVoltage");
                Assert.That(declared.EVMinimumVoltage.ToDecimal(),       Is.EqualTo(      150m), "EVMinimumVoltage");
            });
        }

        [Test]
        public async Task McsEvcc_FallsBackToWhateverAPlainDcSeccOffers()
        {
            // A megawatt truck at an ordinary DC charger: no MCS service in the catalogue, so the base
            // selection logic falls back rather than aborting. Documents that MCS support is additive.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);
                var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);   // plain DC charger
                await secc.RunAsync(seccStream, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port, (TlsOptions?) null, cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

            var evcc = new Evcc20Mcs(evccStream, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage);
            await Task.WhenAll(evcc.RunAsync(cts.Token), seccTask);

            var secc = await seccTask;
            Assert.Multiple(() =>
            {
                Assert.That(secc.IsDone, Is.True);
                Assert.That(evcc.SelectedEnergyServiceId, Is.EqualTo(2).Or.EqualTo(6),
                            "with no MCS service offered the EVCC should take the DC one");
            });
        }
    }
}
