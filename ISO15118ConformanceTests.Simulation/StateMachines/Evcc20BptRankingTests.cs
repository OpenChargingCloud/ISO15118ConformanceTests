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

using ISO15118ConformanceTests.Simulation.Timing;

using cloud.charging.open.protocols.ISO15118.Sap;
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso20;
using cloud.charging.open.protocols.ISO15118.Transport;

namespace ISO15118ConformanceTests.Simulation.StateMachines
{

    /// <summary>
    /// <c>Evcc20Base.PreferBidirectionalService</c> across all three catalogues — AC_BPT (5), DC_BPT (6),
    /// MCS_BPT (9).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What this replaces, and why it is wider.</b> Ranking the bidirectional entry first used to be a
    /// harness-local <c>McsBptFirstEvcc</c> subclass of <c>Evcc20Mcs</c> with the list written out
    /// reversed. It reached MCS_BPT and nothing else — the AC and DC rankings live on <c>Evcc20Base</c>,
    /// and <c>Evcc20Ac</c> is sealed — so services <b>5 and 6 were unreachable from this repository
    /// altogether</b>. That is not a hypothetical gap: EVerest's <c>EvseManager</c> adds the <c>*_BPT</c>
    /// entry whenever its power supply reports itself bidirectional, and their <c>DCSupplySimulator</c>
    /// defaults to exactly that, so their SIL had been advertising DC_BPT at every run this project ever
    /// made against it while our EV took service 2 every time.
    /// </para>
    /// <para>
    /// <b>Loopback, and honest about what that is worth.</b> Both peers are ours, so this proves the
    /// selection and not the session: our SECC answers a charge-parameter request in kind rather than
    /// checking it against the selected service, where EVerest's refuses the mismatch outright
    /// (<c>FAILED_WrongChargeParameter</c>, <c>docs/interop-runs/2026-08-05-everest-mcs-bpt/</c>). What is
    /// asserted here is exactly the id, which is the part a station's catalogue order can silently decide —
    /// and once did.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class Evcc20BptRankingTests
    {

        /// <summary>DC: <c>{ 2, 6 }</c> ranked the other way, against a station offering both.</summary>
        [Test]
        public async Task DcEvcc_WithBptFirst_SelectsDcBpt()
            => Assert.That(await SelectedIdAsync(PowerMode.Dc, bptFirst: true), Is.EqualTo(6),
                           "DC_BPT (6) was ranked first and Secc20Dc offers it, so anything else means the "
                         + "flag never reached the ranking");


        /// <summary>AC: the catalogue that no subclass could reach, because <c>Evcc20Ac</c> is sealed.</summary>
        [Test]
        public async Task AcEvcc_WithBptFirst_SelectsAcBpt()
            => Assert.That(await SelectedIdAsync(PowerMode.Ac, bptFirst: true), Is.EqualTo(5),
                           "AC_BPT (5) was ranked first and Secc20Ac offers it");


        /// <summary>Off by default, in both catalogues — the flag must not quietly change what every other
        /// run in this suite negotiates.</summary>
        [TestCase(PowerMode.Dc, (UInt16) 2)]
        [TestCase(PowerMode.Ac, (UInt16) 1)]
        public async Task WithoutTheFlag_TheUnidirectionalServiceIsStillTaken(PowerMode mode, UInt16 expected)
            => Assert.That(await SelectedIdAsync(mode, bptFirst: false), Is.EqualTo(expected));


        /// <summary>
        /// Runs one loopback session and hands back the id the EV selected.
        /// </summary>
        private static async Task<UInt16> SelectedIdAsync(PowerMode mode, Boolean bptFirst)
        {

            using var cts      = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token, mode);
                Secc20Base secc = mode == PowerMode.Dc
                                      ? new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System)
                                      : new Secc20Ac(TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(seccStream, cts.Token);
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port,
                                                                   (TlsOptions?) null, cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token, mode);

            Evcc20Base evcc = mode == PowerMode.Dc
                                  ? new Evcc20Dc(evccStream, TimeProvider.System, new ImmediateAsyncDelay(),
                                                 LoopbackTimeouts.PerMessage)
                                  : new Evcc20Ac(evccStream, TimeProvider.System, new ImmediateAsyncDelay(),
                                                 LoopbackTimeouts.PerMessage);
            evcc.PreferBidirectionalService = bptFirst;

            await Task.WhenAll(evcc.RunAsync(cts.Token), seccTask);

            return evcc.SelectedEnergyServiceId;

        }

    }

}
