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

using cloud.charging.open.protocols.ISO15118.AppProtocol;
using cloud.charging.open.protocols.ISO15118_2.Generated;
using cloud.charging.open.protocols.ISO15118.Framing;
using cloud.charging.open.protocols.ISO15118.Sap;
using cloud.charging.open.protocols.ISO15118.Session;
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso2;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso20;
using ISO15118ConformanceTests.Simulation.Timing;
using cloud.charging.open.protocols.ISO15118.Transport;
using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;

using D20 = cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated;

namespace ISO15118ConformanceTests.Simulation.StateMachines
{

    /// <summary>
    /// Values the station supplies and our EVCCs used to supply themselves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written after the third live finding of that shape in three days — the unread response code, the
    /// unbounded <c>Ongoing</c> poll and the assumed energy transfer mode — when the roadmap asked for a
    /// sweep for the fourth. The sweep found four, all cheap, none of which any oracle here could have
    /// caught: our own SECC supplies exactly what our own EVCC assumes, so a constant and a field agree
    /// until a foreign station disagrees.
    /// </para>
    /// <para>
    /// Each test therefore builds a station that answers differently from ours — a different service id, a
    /// catalogue without our power mode, an authorization offer without EIM, a handshake that accepts a
    /// schema we never offered — because that is the only place these can be seen from.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class EvccReadsTheOfferTests
    {

        // ── ISO 15118-2: the ChargeService id ──────────────────────────────────────────────────────

        /// <summary><see cref="Secc2"/> with one field of one response rewritten. Hand-rolled because
        /// <see cref="Secc2"/> is sealed; see <c>Evcc2EnergyTransferModeTests</c> for the same reasoning.</summary>
        private static async Task RunSecc2WithServiceIdAsync(Stream stream, ushort serviceId,
                                                             List<BodyBaseType> received, CancellationToken ct)
        {
            var secc   = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System);
            var buffer = new byte[8192];

            while (!ct.IsCancellationRequested)
            {
                var (_, message) = await V2GTPStream.ReadFrameAsync(stream, ct);
                var request      = (V2G_Message) message;
                received.Add(request.Body.BodyElement!);

                var reply = secc.Handle(request);

                if (reply.Body.BodyElement is ServiceDiscoveryResType discovery)
                    reply = new V2G_Message(reply.Header, new BodyType(discovery with
                    {
                        ChargeService = discovery.ChargeService! with { ServiceID = serviceId },
                    }));

                if (!reply.TryEncode(buffer, out var length))
                    throw new InvalidOperationException("encode failed");

                await V2GTPStream.WriteFrameAsync(stream, MessageSet.Iso15118_2, buffer.AsMemory(0, length), ct);
            }
        }


        [Test]
        public async Task Iso2_TheSelectedServiceIsTheOneTheStationAdvertised()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var received = new List<BodyBaseType>();
            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_2, cts.Token, PowerMode.Ac);
                try { await RunSecc2WithServiceIdAsync(seccStream, 7, received, cts.Token); }
                catch { /* the EV hangs up when it is done */ }
            }, cts.Token);

            using (var evccStream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port,
                                                                     (TlsOptions?) null, cts.Token))
            {
                await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_2, cts.Token, PowerMode.Ac);
                await new Evcc2(evccStream, PowerMode.Ac, TimeProvider.System, new ImmediateAsyncDelay(),
                                LoopbackTimeouts.PerMessage).RunAsync(cts.Token);
            }
            await seccTask;

            var selection = received.OfType<PaymentServiceSelectionReqType>().Single();

            Assert.That(selection.SelectedServiceList.SelectedService.Single().ServiceID, Is.EqualTo(7),
                        "the EV selected the station's ChargeService id, not the 1 it used to hard-code");
        }


        // ── ISO 15118-20: the energy-transfer service ──────────────────────────────────────────────

        /// <summary>A -20 station whose catalogue holds one AC service and nothing else.</summary>
        private sealed class AcOnlySecc20(TimeSpan sequenceTimeout, TimeProvider clock)
            : Secc20Dc(sequenceTimeout, clock)
        {
            public override (MessageSet Set, object Response) Handle(MessageSet set, object request)
            {
                var (resSet, response) = base.Handle(set, request);

                if (response is D20.ServiceDiscoveryRes discovery)
                    return (resSet, discovery with
                    {
                        EnergyTransferServiceList = new D20.ServiceListType(
                            [new D20.ServiceType(ServiceID: 1, FreeService: true)]),
                    });

                return (resSet, response);
            }
        }

        /// <summary>A -20 station that offers Plug &amp; Charge and nothing else.</summary>
        private sealed class PncOnlySecc20(TimeSpan sequenceTimeout, TimeProvider clock)
            : Secc20Dc(sequenceTimeout, clock)
        {
            public override (MessageSet Set, object Response) Handle(MessageSet set, object request)
            {
                var (resSet, response) = base.Handle(set, request);

                if (response is D20.AuthorizationSetupRes setup)
                    return (resSet, setup with { AuthorizationServices = [D20.Authorization.PnC] });

                return (resSet, response);
            }
        }


        private static async Task RunIso20SessionAsync(Func<TimeSpan, TimeProvider, Secc20Base> makeSecc,
                                                       CancellationToken ct)
        {
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(ct);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, ct);
                try { await makeSecc(TimeSpan.FromSeconds(60), TimeProvider.System).RunAsync(seccStream, ct); }
                catch { /* the EV refuses and hangs up; that is the assertion */ }
            }, ct);

            using (var evccStream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port,
                                                                     (TlsOptions?) null, ct))
            {
                await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, ct);
                await new Evcc20Dc(evccStream, TimeProvider.System, new ImmediateAsyncDelay(),
                                   LoopbackTimeouts.PerMessage).RunAsync(ct);
            }

            await seccTask;
        }


        [Test]
        public void Iso20_ADcCarAtAnAcOnlyStationIsRefusedByName()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var thrown = Assert.ThrowsAsync<SessionAborted>(async () =>
                await RunIso20SessionAsync((t, c) => new AcOnlySecc20(t, c), cts.Token));

            Assert.Multiple(() =>
            {
                Assert.That(thrown!.Message, Does.Contain("DC"));
                Assert.That(thrown!.Message, Does.Contain("offered 1"),
                            "the refusal names the catalogue — the old code silently took service 1 and then "
                          + "sent DC messages against it");
            });
        }


        [Test]
        public void Iso20_AnEimCarAtAPncOnlyStationIsRefusedByName()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var thrown = Assert.ThrowsAsync<SessionAborted>(async () =>
                await RunIso20SessionAsync((t, c) => new PncOnlySecc20(t, c), cts.Token));

            Assert.Multiple(() =>
            {
                Assert.That(thrown!.Message, Does.Contain("EIM"));
                Assert.That(thrown!.Message, Does.Contain("PnC"), "…and says what was on offer instead");
            });
        }


        // ── SupportedAppProtocol: the accepted SchemaID ────────────────────────────────────────────

        /// <summary>A station that says OK to a schema the EV never offered. Latent today, because our
        /// handshake offers exactly one entry — and precisely the thing nobody would re-check on the day a
        /// second entry is added.</summary>
        [Test]
        public async Task Sap_AnAcceptedSchemaWeNeverOfferedIsRefused()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                var (_, _) = await V2GTPStream.ReadRawFrameAsync(seccStream, cts.Token);

                var buffer = new byte[16];
                var res    = new SupportedAppProtocolRes(cloud.charging.open.protocols.ISO15118.AppProtocol.ResponseCode.OK_SuccessfulNegotiation,
                                                             SchemaID: 7);
                if (!SupportedAppProtocolCodec.TryEncodeResponse(res, buffer, out int n))
                    throw new InvalidOperationException("encode failed");

                await V2GTPStream.WriteRawFrameAsync(seccStream, V2GTPCodec.PayloadType_AppProtocol,
                                                      buffer.AsMemory(0, n), cts.Token);
                await Task.Delay(200, cts.Token);
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port,
                                                                    (TlsOptions?) null, cts.Token);

            var thrown = Assert.ThrowsAsync<SessionAborted>(async () =>
                await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_2, cts.Token, PowerMode.Ac));

            Assert.That(thrown!.Message, Does.Contain("SchemaID 7"));

            try { await seccTask; } catch { /* the EV hung up */ }
        }

    }

}
