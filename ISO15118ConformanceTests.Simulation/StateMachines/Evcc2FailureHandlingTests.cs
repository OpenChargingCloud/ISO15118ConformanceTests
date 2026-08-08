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
using System.Reflection;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118_2.Generated;
using cloud.charging.open.protocols.ISO15118.Framing;
using cloud.charging.open.protocols.ISO15118.Session;
using cloud.charging.open.protocols.ISO15118.Sap;
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso2;
using ISO15118ConformanceTests.Simulation.Timing;
using cloud.charging.open.protocols.ISO15118.Transport;
using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;

namespace ISO15118ConformanceTests.Simulation.StateMachines
{

    /// <summary>
    /// What our -2 EVCC does when the station answers with a <c>FAILED</c> code.
    /// </summary>
    /// <remarks>
    /// The -2 half of the gap eVDriveFlow exposed in the -20 EVCC
    /// (<c>docs/interop-runs/2026-08-01-edf-iso20-dc-notls/</c>, finding 3). Same hole, same reason it
    /// was invisible: our own SECC never answers FAILED, so the trace corpus contains no such response
    /// and nothing that replays it can notice.
    /// </remarks>
    [TestFixture]
    public class Evcc2FailureHandlingTests
    {

        /// <summary>
        /// The claim <c>Evcc2.RefuseOnFailure</c> rests on: every -2 response type carries a readable
        /// <c>ResponseCode</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The check reads the property by name because -2 has no common response base. A hand-written
        /// switch over the response types would have been fail-open — whichever one is forgotten, or added
        /// later, goes unchecked — so the reflective read is deliberate and this is what makes it
        /// trustworthy: the generated assembly is enumerated, and every <c>*ResType</c> in it must be one
        /// the checker can read a code out of.
        /// </para>
        /// <para>
        /// If the generator ever emits a response without a <c>ResponseCode</c>, this fails loudly here
        /// rather than silently in a session.
        /// </para>
        /// </remarks>
        [Test]
        public void EveryResponseTypeIsCheckable()
        {

            var responses = typeof(SessionSetupResType).Assembly
                                .GetTypes()
                                .Where(t => t is { IsAbstract: false, IsClass: true } &&
                                            t.Name.EndsWith("ResType", StringComparison.Ordinal) &&
                                            typeof(BodyBaseType).IsAssignableFrom(t))
                                .ToList();

            Assert.That(responses, Has.Count.GreaterThan(10),
                        "the -2 message set should have more response types than this — is the filter right?");

            foreach (var type in responses)
            {
                var property = type.GetProperty("ResponseCode");
                Assert.That(property, Is.Not.Null, $"{type.Name} has no ResponseCode; the check would skip it");
                Assert.That(property!.PropertyType, Is.EqualTo(typeof(ResponseCode)), type.Name);
            }

        }


        /// <summary>
        /// The ordering the <c>&gt;= FAILED</c> comparison rests on.
        /// </summary>
        /// <remarks>
        /// -2 has only two families — four <c>OK*</c> values and then <c>FAILED</c> onwards; there are no
        /// <c>WARNING</c> codes, unlike -20. A regenerated enum that interleaved them would turn failures
        /// into successes without a word.
        /// </remarks>
        [Test]
        public void TheResponseCodeFamiliesAreContiguousAndOrdered()
        {
            foreach (ResponseCode code in Enum.GetValues<ResponseCode>())
            {
                var name = code.ToString();

                if (name.StartsWith("FAILED", StringComparison.Ordinal))
                    Assert.That(code, Is.GreaterThanOrEqualTo(ResponseCode.FAILED), $"{name} sorts below FAILED");
                else
                    Assert.That(code, Is.LessThan(ResponseCode.FAILED),
                                $"{name} is not a failure but sorts at or above FAILED");
            }
        }


        /// <summary>
        /// A station that fails the charge-parameter discovery — our own SECC, answering normally until
        /// that one message.
        /// </summary>
        /// <remarks>
        /// Hand-rolled rather than a subclass: <c>Secc2</c> is sealed, and a -2 session is a single
        /// message type over one codec, so driving it here costs ten lines and leaves production code
        /// alone. (The -20 equivalent made <c>Secc20Base.Handle</c> virtual because its loop is not this
        /// simple.)
        /// </remarks>
        private static async Task RunFailingStationAsync(Stream stream, CancellationToken ct)
        {
            var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System);
            var buffer = new byte[8192];

            while (!ct.IsCancellationRequested)
            {
                var (_, message) = await V2GTPStream.ReadFrameAsync(stream, ct);

                var reply = secc.Handle((V2G_Message) message);

                if (reply.Body.BodyElement is ChargeParameterDiscoveryResType res)
                    reply = reply with { Body = new BodyType(res with { ResponseCode = ResponseCode.FAILED }) };

                if (!reply.TryEncode(buffer, out var length))
                    throw new InvalidOperationException("encode failed");

                await V2GTPStream.WriteFrameAsync(stream, MessageSet.Iso15118_2, buffer.AsMemory(0, length), ct);
            }
        }


        /// <summary>
        /// The finding, in -2: a FAILED response ends the session instead of being charged through.
        /// </summary>
        [Test]
        public async Task AFailedResponseEndsTheSession()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_2, cts.Token, PowerMode.Ac);
                try { await RunFailingStationAsync(seccStream, cts.Token); }
                catch { /* the EV hangs up on us; that is the point */ }
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port,
                                                                   (TlsOptions?) null, cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_2, cts.Token, PowerMode.Ac);

            var evcc = new Evcc2(evccStream, PowerMode.Ac, TimeProvider.System, new ImmediateAsyncDelay(),
                                 LoopbackTimeouts.PerMessage);

            var aborted = Assert.ThrowsAsync<SessionAborted>(async () => await evcc.RunAsync(cts.Token));

            Assert.Multiple(() =>
            {
                Assert.That(aborted!.Message, Does.Contain("ChargeParameterDiscoveryResType"));
                Assert.That(aborted!.Message, Does.Contain("FAILED"));
            });

            evccStream.Dispose();
            await seccTask;
        }


        /// <summary>An ordinary session is untouched — the check must not fire on the OK family.</summary>
        [Test]
        public async Task AnOrdinarySessionStillRunsToCompletion()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_2, cts.Token, PowerMode.Ac);
                var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(seccStream, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port,
                                                                   (TlsOptions?) null, cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_2, cts.Token, PowerMode.Ac);

            var evcc = new Evcc2(evccStream, PowerMode.Ac, TimeProvider.System, new ImmediateAsyncDelay(),
                                 LoopbackTimeouts.PerMessage);
            await Task.WhenAll(evcc.RunAsync(cts.Token), seccTask);

            var secc = await seccTask;

            Assert.Multiple(() =>
            {
                Assert.That(secc.IsDone, Is.True);
                Assert.That(evcc.SessionSetupCode, Is.EqualTo(ResponseCode.OK_NewSessionEstablished));
            });
        }

    }

}
