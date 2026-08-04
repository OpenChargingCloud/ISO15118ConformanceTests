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

using cloud.charging.open.protocols.ISO15118_2.Generated;
using Vanaheimr.V2G.Simulation.Framing;
using Vanaheimr.V2G.Simulation.Sap;
using Vanaheimr.V2G.Simulation.Session;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso2;
using ISO15118ConformanceTests.Simulation.Timing;
using Vanaheimr.V2G.Simulation.Transport;
using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;

namespace ISO15118ConformanceTests.Simulation.StateMachines
{

    /// <summary>
    /// Which energy transfer mode our -2 EVCC asks for, and where it gets the answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It used to be a constant: <c>AC_three_phase_core</c> for AC, <c>DC_extended</c> for DC. That passed
    /// against every station this project had met, because each of them offered the mode we happened to
    /// name — which is not the same as reading the list they sent. EVerest's AC SIL configuration
    /// advertises <c>AC_single_phase_core</c> and answers a three-phase request with
    /// <c>FAILED_WrongEnergyTransferMode</c>, correctly, seven messages in
    /// (<c>docs/interop-runs/2026-08-03-everest-iso2-ac/</c>).
    /// </para>
    /// <para>
    /// The station in these tests is hand-rolled around <see cref="Secc2"/>'s own <c>Handle</c> rather than
    /// a subclass, because <see cref="Secc2"/> is sealed and the alternative — unsealing it so a test can
    /// rewrite one field of one response — is the larger change. Same reasoning as
    /// <c>OngoingTimeoutTests</c>, which needed a station our SECC cannot be.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class Evcc2EnergyTransferModeTests
    {

        /// <summary>Runs a station whose ServiceDiscoveryRes advertises exactly <paramref name="offered"/>,
        /// and is <see cref="Secc2"/> in every other respect. Appends every request it saw to
        /// <paramref name="received"/> — the caller owns the list, because this method exits by exception
        /// when the EV hangs up and a return value would be lost exactly when the test needs it.</summary>
        private static async Task RunStationAsync(Stream stream, PowerMode mode,
                                                  EnergyTransferMode[] offered,
                                                  List<BodyBaseType> received,
                                                  CancellationToken ct)
        {
            var secc     = new Secc2(mode, TimeSpan.FromSeconds(60), TimeProvider.System);
            var buffer   = new byte[8192];

            while (!ct.IsCancellationRequested)
            {
                var (_, message) = await V2GTPStream.ReadFrameAsync(stream, ct);
                var request      = (V2G_Message) message;
                received.Add(request.Body.BodyElement!);

                var reply = secc.Handle(request);

                if (reply.Body.BodyElement is ServiceDiscoveryResType discovery)
                    reply = new V2G_Message(reply.Header, new BodyType(discovery with
                    {
                        ChargeService = discovery.ChargeService! with
                        {
                            SupportedEnergyTransferMode = new SupportedEnergyTransferModeType(offered),
                        },
                    }));

                if (!reply.TryEncode(buffer, out var length))
                    throw new InvalidOperationException("encode failed");

                await V2GTPStream.WriteFrameAsync(stream, MessageSet.Iso15118_2, buffer.AsMemory(0, length), ct);
            }
        }


        private static async Task<List<BodyBaseType>> RunSessionAsync(PowerMode mode,
                                                                      EnergyTransferMode[] offered,
                                                                      CancellationToken ct)
        {
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var received = new List<BodyBaseType>();
            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(ct);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_2, ct, mode);
                try { await RunStationAsync(seccStream, mode, offered, received, ct); }
                catch { /* the EV hangs up when it is done, or when it refuses */ }
            }, ct);

            using (var evccStream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port,
                                                                     (TlsOptions?) null, ct))
            {
                await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_2, ct, mode);
                await new Evcc2(evccStream, mode, TimeProvider.System, new ImmediateAsyncDelay(),
                                LoopbackTimeouts.PerMessage).RunAsync(ct);
            }

            await seccTask;
            return received;
        }


        [Test]
        public async Task Ac_ASinglePhaseStationGetsASinglePhaseRequest()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var received = await RunSessionAsync(PowerMode.Ac,
                                                 [EnergyTransferMode.AC_single_phase_core],
                                                 cts.Token);

            var cpd = received.OfType<ChargeParameterDiscoveryReqType>().First();

            Assert.That(cpd.RequestedEnergyTransferMode, Is.EqualTo(EnergyTransferMode.AC_single_phase_core),
                        "the EV asked for what the station advertised, not for what it prefers");
        }


        [Test]
        public async Task Ac_AThreePhaseStationStillGetsThreePhase()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var received = await RunSessionAsync(PowerMode.Ac,
                                                 [EnergyTransferMode.AC_single_phase_core,
                                                  EnergyTransferMode.AC_three_phase_core],
                                                 cts.Token);

            var cpd = received.OfType<ChargeParameterDiscoveryReqType>().First();

            Assert.That(cpd.RequestedEnergyTransferMode, Is.EqualTo(EnergyTransferMode.AC_three_phase_core),
                        "offered both, the EV takes the better one");
        }


        [Test]
        public void AnAcCarAgainstADcOnlyStation_IsRefusedByName()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var thrown = Assert.ThrowsAsync<SessionAborted>(async () =>
                await RunSessionAsync(PowerMode.Ac, [EnergyTransferMode.DC_extended], cts.Token));

            Assert.Multiple(() =>
            {
                Assert.That(thrown!.Message, Does.Contain("AC"));
                Assert.That(thrown!.Message, Does.Contain("DC_extended"),
                            "the error names what was offered — that is the line that turns "
                          + "\"the station refused\" into \"it is a DC charger\"");
            });
        }

    }

}
