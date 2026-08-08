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

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118.AppProtocol;
using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;
using cloud.charging.open.protocols.ISO15118.Framing;
using cloud.charging.open.protocols.ISO15118.Sap;
using cloud.charging.open.protocols.ISO15118.Session;
using cloud.charging.open.protocols.ISO15118.StateMachines;

namespace ISO15118ConformanceTests.Simulation.Interop;

/// <summary>
/// A real Tesla Model 3's SupportedAppProtocol handshake, from tux-evse's `tesla-3-din.pcap`.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a DIN capture is testable here at all.</b> Nothing in this project speaks DIN 70121, and
/// the rest of that capture is therefore out of reach. The handshake is the exception: the
/// SupportedAppProtocol schema (<c>urn:iso:15118:2:2010:AppProtocol</c>) is its own grammar and is
/// deliberately protocol-independent — it is how a car and a station agree <em>which</em> protocol to
/// speak, so it cannot presuppose one. Our codec reads it whatever the offer names, and that makes
/// the first two frames of a DIN session ordinary test material.
/// </para>
/// <para>
/// <b>What it contains that no synthetic offer would.</b> Decoded, the car's two entries are:
/// </para>
/// <code>
///   document order 1:  schemaId 1  priority 2  v2.0  urn:din:70121:2012:MsgDef
///   document order 2:  schemaId 2  priority 1  v0.7  urn:tesla:din:2018:MsgDef
/// </code>
/// <para>
/// Two things there are worth the whole capture. The first is <c>urn:tesla:din:2018:MsgDef</c>: a
/// **vendor-proprietary protocol**, at the highest priority, from a car in the field. Every offer this
/// project had seen or built named protocols from the standards, so the case of "the entry the car
/// wants most is one nobody else can speak" had never been on the wire here — and it is the case the
/// whole fallback exists for.
/// </para>
/// <para>
/// The second is quieter and closer to home. <b>Document order, SchemaID order and Priority order all
/// disagree.</b> Our own EVCC builds them to coincide — entry 0 gets SchemaID 1 and Priority 1, entry
/// 1 gets SchemaID 2 and Priority 2 (see <c>SapOffer</c>) — and that coincidence is exactly what let
/// our SECC answer a literal SchemaID 1 for months without anyone noticing (fixed 2026-08-03). This
/// car takes the three apart: its preferred entry is second in the document and carries SchemaID 2,
/// while SchemaID 1 is the one it would rather not use. Any station that conflates the three is wrong
/// here, and no offer this project can construct for itself would show it.
/// </para>
/// <para>
/// Frames lifted from the capture with <c>tools/interop-tux-evse/v2gtp-from-pcap.py</c>; the run
/// notes are <c>docs/interop-runs/2026-08-07-tesla-din-handshake/</c>.
/// </para>
/// </remarks>
[TestFixture]
public class TeslaDinHandshakeTests
{

    /// <summary>The car's first frame: V2GTP header + the EXI AppProtocol document.</summary>
    private const string TeslaOfferFrame =
        "01fe800100000042" +
        "8000dbab9371d3234b71d1b981899189d191818991d26b9b3a232b30020000040401b" +
        "75726e3a7465736c613a64696e3a323031383a4d736744656600001c0100080";

    /// <summary>The station's answer, four EXI bytes.</summary>
    private const string StationAnswerFrame = "01fe8001000000048040" + "0040";


    private static byte[] Payload(string frameHex)
        => Convert.FromHexString(Padded(frameHex))[8..];


    /// <summary>
    /// The offer decodes, and it names a protocol no standard defines.
    /// </summary>
    [Test]
    public void TheCarOffersAProprietaryNamespaceAtPriorityOne()
    {

        var decoded = SupportedAppProtocolCodec.DecodeAny(Payload(TeslaOfferFrame), out _);

        Assert.That(decoded, Is.InstanceOf<SupportedAppProtocolReq>());
        var offer = (SupportedAppProtocolReq) decoded;

        TestContext.Out.WriteLine("The Tesla's offer, as decoded by our codec:");
        foreach (var entry in offer.AppProtocols)
            TestContext.Out.WriteLine(
                $"  priority {entry.Priority}  schemaId {entry.SchemaID}  " +
                $"v{entry.VersionNumberMajor}.{entry.VersionNumberMinor}  {entry.ProtocolNamespace}");

        Assert.Multiple(() =>
        {
            Assert.That(offer.AppProtocols, Has.Count.EqualTo(2),
                        "the car offers two protocols, not one");

            var proprietary = offer.AppProtocols.Single(e => e.ProtocolNamespace.StartsWith("urn:tesla:"));

            Assert.That(proprietary.ProtocolNamespace, Is.EqualTo("urn:tesla:din:2018:MsgDef"),
                        "a vendor namespace, and the reason this handshake is worth keeping");
            Assert.That(proprietary.Priority, Is.EqualTo(1),
                        "…and it is what the car wants most, so the fallback is not decoration");

            // The three orderings come apart, which is the part our own EVCC cannot produce.
            Assert.That(offer.AppProtocols[0].ProtocolNamespace, Is.EqualTo("urn:din:70121:2012:MsgDef"),
                        "the standard entry is FIRST in the document …");
            Assert.That(offer.AppProtocols[0].Priority, Is.EqualTo(2),
                        "… while being SECOND by priority — document order is not preference order");
            Assert.That(offer.AppProtocols[0].SchemaID, Is.EqualTo(1),
                        "… and SchemaID 1 is the entry the car would rather not use, which is the " +
                        "assumption a station answering a literal 1 was living on");
        });

    }


    /// <summary>
    /// The station the car actually met refused the proprietary entry and took the standard one.
    /// </summary>
    /// <remarks>
    /// The evidence that this is an ordinary negotiation rather than an exotic capture: a real
    /// charge point in the field (<c>DE*PNX*E12345*1</c>) read the same two entries, declined the
    /// one it did not know, and answered with the SchemaID of the one it did. That is exactly the
    /// behaviour <c>SapHandshake</c> implements, checked here against somebody else's station rather
    /// than against our own.
    /// </remarks>
    [Test]
    public void TheStationAnsweredWithTheSchemaIdOfAnEntryTheCarOffered()
    {

        var offer = (SupportedAppProtocolReq) SupportedAppProtocolCodec.DecodeAny(Payload(TeslaOfferFrame), out _);
        var reply = SupportedAppProtocolCodec.DecodeAny(Payload(StationAnswerFrame), out _);

        Assert.That(reply, Is.InstanceOf<SupportedAppProtocolRes>());
        var answer = (SupportedAppProtocolRes) reply;

        TestContext.Out.WriteLine($"The station answered: {answer.Code}, SchemaID {answer.SchemaID}");

        Assert.Multiple(() =>
        {
            Assert.That(answer.Code, Is.EqualTo(ResponseCode.OK_SuccessfulNegotiation));
            Assert.That(answer.SchemaID, Is.Not.Null,
                        "a successful negotiation has to name which entry won");
            Assert.That(offer.AppProtocols.Select(e => e.SchemaID), Does.Contain(answer.SchemaID!.Value),
                        "the answered SchemaID must be one the car actually offered");
        });

    }


    /// <summary>
    /// Our station, given this car's real offer, refuses it <em>on the wire</em> rather than by
    /// closing the socket.
    /// </summary>
    /// <remarks>
    /// <para>
    /// We speak neither of the two protocols the Tesla names, so <c>Failed_NoNegotiation</c> is the
    /// correct and only answer. What is worth testing is that the answer is <em>sent</em>: the same
    /// distinction the sequence-guard fix turned on a day earlier
    /// (<c>docs/interop-runs/2026-08-06-tux-head-reverse</c>) — a refusal a car can read, versus a
    /// connection that simply dies and leaves the driver's screen to guess.
    /// </para>
    /// <para>
    /// This is the whole of what a DIN capture can currently exercise against this project, and it is
    /// not nothing: it is the first time our negotiation has been handed an offer from a real vehicle
    /// that it must turn down.
    /// </para>
    /// </remarks>
    [Test]
    public void OurStationRefusesTheRealOfferOnTheWire()
    {

        var request = Convert.FromHexString(Padded(TeslaOfferFrame));

        var wire = new MemoryStream();
        wire.Write(request, 0, request.Length);
        wire.Position = 0;

        Assert.ThrowsAsync<SessionAborted>(async () =>
            await SapHandshake.RunSeccSideAsync(wire,
                [new SapOffer(ProtocolVariant.Iso15118_2,  PowerMode.Dc),
                 new SapOffer(ProtocolVariant.Iso15118_20, PowerMode.Dc)]),
            "a station that speaks neither protocol has to end the session");

        var answered = wire.ToArray()[request.Length..];

        Assert.That(answered, Is.Not.Empty,
                    "the station closed without answering — the car cannot tell a refusal from a " +
                    "dead station");

        var reply = (SupportedAppProtocolRes) SupportedAppProtocolCodec.DecodeAny(
                        answered.AsSpan(V2GTPCodec.HeaderSize), out _);

        TestContext.Out.WriteLine($"Our station answered: {reply.Code}");

        Assert.Multiple(() =>
        {
            Assert.That(reply.Code, Is.EqualTo(ResponseCode.Failed_NoNegotiation));
            Assert.That(reply.SchemaID, Is.Null, "a refusal names no entry");
        });

    }


    /// <summary>
    /// An unknown highest-priority entry does not hide the supported one behind it.
    /// </summary>
    /// <remarks>
    /// The Tesla's offer has a shape this project had never built for itself: <b>the entry the car
    /// wants most is one no station can be expected to speak</b>. The real charge point it met dealt
    /// with that — it answered the SchemaID of the standard entry, the one at priority 2 — and the
    /// fallback is therefore ordinary field behaviour rather than a corner case.
    ///
    /// The capture cannot test it against us directly, because we support neither of its two
    /// namespaces and so refuse both. So this reproduces the *shape* with a namespace we do speak:
    /// a proprietary entry at priority 1, ISO 15118-2 at priority 2. A station that took the
    /// first entry it could parse, or the first in document order, would answer differently.
    /// </remarks>
    [Test]
    public async Task AnUnknownTopPriorityEntryDoesNotHideTheOneBelowIt()
    {

        var offer = new SupportedAppProtocolReq(
        [
            new AppProtocolEntry("urn:tesla:din:2018:MsgDef",  0, 7, SchemaID: 7, Priority: 1),
            new AppProtocolEntry("urn:iso:15118:2:2013:MsgDef", 2, 0, SchemaID: 9, Priority: 2),
        ]);

        var buf = new byte[256];
        Assert.That(SupportedAppProtocolCodec.TryEncodeRequest(offer, buf, out var n), Is.True);

        var wire = new MemoryStream();
        await V2GTPStream.WriteRawFrameAsync(wire, V2GTPCodec.PayloadType_AppProtocol, buf.AsMemory(0, n));
        var requestLength = (int) wire.Length;
        wire.Position = 0;

        var settled = await SapHandshake.RunSeccSideAsync(wire,
                          [new SapOffer(ProtocolVariant.Iso15118_2, PowerMode.Dc)]);

        var reply = (SupportedAppProtocolRes) SupportedAppProtocolCodec.DecodeAny(
                        wire.ToArray()[requestLength..].AsSpan(V2GTPCodec.HeaderSize), out _);

        Assert.Multiple(() =>
        {
            Assert.That(settled.Protocol, Is.EqualTo(ProtocolVariant.Iso15118_2));
            Assert.That(reply.Code, Is.EqualTo(ResponseCode.OK_SuccessfulNegotiation));
            Assert.That(reply.SchemaID, Is.EqualTo((byte) 9),
                        "the station must echo the SchemaID of the entry it accepted — 9, the one " +
                        "the car gave the entry at priority 2, not 7 and not a literal 1");
        });

    }


    private static string Padded(string hex)
        => hex.Length % 2 == 1 ? hex + "0" : hex;

}
