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
using cloud.charging.open.protocols.ISO15118.Sap;
using cloud.charging.open.protocols.ISO15118.Session;
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso20;
using ISO15118ConformanceTests.Simulation.Timing;
using cloud.charging.open.protocols.ISO15118.Transport;
using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;

// 'Authorization' also exists in System.Net, which this file needs for IPEndPoint.
using Auth = cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated.Authorization;

namespace ISO15118ConformanceTests.Simulation.StateMachines
{

    /// <summary>
    /// What the station advertises in <c>AuthorizationSetupRes</c>, and the switch that narrows it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both forms are legal — a station may offer Plug &amp; Charge alongside EIM or not — so this is not a
    /// conformance question but a deployment one, and the tests pin both shapes rather than blessing one.
    /// </para>
    /// <para>
    /// The switch exists because of a live counterparty. eVDriveFlow's EVCC walks the offered list and
    /// raises on the first entry it does not support, even when the EIM it *does* support is the next
    /// entry, which ends the session at authorization —
    /// <c>docs/interop-runs/2026-08-01-edf-iso20-dc-dynamic-reverse/</c>. Nothing behind authorization is
    /// reachable with such a peer unless the station offers less.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class Secc20AuthorizationOfferTests
    {

        /// <summary>The EV's header state, which takes up the station's SessionID after SessionSetup — as a
        /// real car does. A fresh <c>SessionContext</c> per call, which is what this was, opens every
        /// request with the all-zero id that <c>[V2G20-460]</c> now refuses.</summary>
        private readonly SessionContext _ctx = new(TimeProvider.System);
        private MessageHeaderType Header() => _ctx.ToCommonHeader();

        private AuthorizationSetupRes Setup(bool offerPnc)
        {
            var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System) { OfferPlugAndCharge = offerPnc };

            var setup = (SessionSetupRes) secc.Handle(MessageSet.Iso20CommonMessages,
                                                      new SessionSetupReq(Header(), "EVCC01")).Response;
            _ctx.SessionId = setup.Header.SessionID;

            var (_, response) = secc.Handle(MessageSet.Iso20CommonMessages, new AuthorizationSetupReq(Header()));
            return (AuthorizationSetupRes) response;
        }


        /// <summary>The default, which is what every recorded session and the whole corpus contain.</summary>
        [Test]
        public void ByDefaultTheStationOffersPlugAndChargeAndEim()
        {
            var res = Setup(offerPnc: true);

            Assert.Multiple(() =>
            {
                Assert.That(res.AuthorizationServices, Is.EqualTo(new[] { Auth.PnC, Auth.EIM }));
                Assert.That(res.CertificateInstallationService, Is.True);

                // The mode block is a choice, and PnC is the one that carries the challenge the EV signs.
                Assert.That(res.PnC_ASResAuthorizationMode, Is.Not.Null);
                Assert.That(res.PnC_ASResAuthorizationMode!.GenChallenge, Has.Length.EqualTo(16));
                Assert.That(res.EIM_ASResAuthorizationMode, Is.Null);
            });
        }


        [Test]
        public void WithPlugAndChargeOffTheOfferIsEimAndNothingElse()
        {
            var res = Setup(offerPnc: false);

            Assert.Multiple(() =>
            {
                Assert.That(res.AuthorizationServices, Is.EqualTo(new[] { Auth.EIM }));

                // The other half of the choice, and no challenge: there is nothing to sign.
                Assert.That(res.EIM_ASResAuthorizationMode, Is.Not.Null);
                Assert.That(res.PnC_ASResAuthorizationMode, Is.Null);

                // And no contract provisioning. Installing a certificate for an authorization method the
                // station has just said it does not do would be an offer with nothing behind it.
                Assert.That(res.CertificateInstallationService, Is.False);
            });
        }


        /// <summary>
        /// The behaviour the switch is for: a whole session still runs.
        /// </summary>
        /// <remarks>
        /// The two tests above pin one message. This one is the reason to care — an EIM-only offer must
        /// still carry a session from the handshake to SessionStop, because a narrowed offer that broke
        /// charging would trade one stuck counterparty for a broken station.
        /// </remarks>
        [Test]
        public async Task AnEimOnlySessionRunsToCompletion()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);
                var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System) { OfferPlugAndCharge = false };
                await secc.RunAsync(seccStream, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port,
                                                                   (TlsOptions?) null, cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

            var evcc = new Evcc20Dc(evccStream, TimeProvider.System, new ImmediateAsyncDelay(),
                                    LoopbackTimeouts.PerMessage);
            await Task.WhenAll(evcc.RunAsync(cts.Token), seccTask);

            var secc = await seccTask;

            Assert.Multiple(() =>
            {
                Assert.That(secc.IsDone,      Is.True);
                Assert.That(evcc.Exchanges,   Is.GreaterThan(10), "a whole DC session, not a truncated one");
                Assert.That(secc.PnCAuth,     Is.Null, "EIM was used, so there is no Plug & Charge verdict");
            });
        }

    }

}
