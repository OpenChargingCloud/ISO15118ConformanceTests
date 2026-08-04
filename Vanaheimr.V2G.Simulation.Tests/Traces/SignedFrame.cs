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

using System.Security.Cryptography;

using Vanaheimr.V2G.Simulation.StateMachines.Iso2;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;

using Vanaheimr.V2G.Simulation.Metering;

using Ac20 = cloud.charging.open.protocols.ISO15118_20.AC.Generated;
using C    = cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated;
using Dc20 = cloud.charging.open.protocols.ISO15118_20.DC.Generated;
using I2   = cloud.charging.open.protocols.ISO15118_2.Generated;

namespace Vanaheimr.V2G.Simulation.Tests.Traces;

/// <summary>
/// Everything the trace corpus needs to know about a frame that carries a signature.
/// </summary>
/// <remarks>
/// <para>
/// <b>The problem this solves.</b> A recorded session can be compared byte for byte only if the
/// requests are a pure function of the responses, and ECDSA breaks that: the nonce is random, so the
/// same message signed twice produces two different <c>SignatureValue</c>s. That is why the first
/// corpus was EIM only.
/// </para>
/// <para>
/// <b>The observation that makes it tractable.</b> The <i>signature value</i> is the only random part
/// of a signed message. The body is deterministic, and so is <c>SignedInfo</c> — it holds a SHA-256
/// digest of the signed element's fragment, which is a function of the message. So a signed frame is
/// deterministic apart from 64 bytes whose position the codec knows and a byte comparison does not.
/// </para>
/// <para>
/// <b>Hence the comparison.</b> Decode the frame a port produced, substitute the <i>recorded</i>
/// signature value, re-encode, and compare bytes as before. Everything except those 64 bytes is
/// checked exactly — including <c>SignedInfo</c>, therefore including the digest, therefore including
/// <i>which octets were signed</i>, which is the part that actually goes wrong in a port. Then verify
/// the port's <i>own</i> signature against its own <c>SignedInfo</c> with the corpus public key,
/// which is the half the substitution deliberately discards.
/// </para>
/// <para>
/// Substituting relies on decode-then-re-encode reproducing the original bytes. That is not an
/// assumption made here: it is the round-trip property the vector corpus already pins for every
/// message set.
/// </para>
/// <para>
/// <b>What this does not check.</b> That ECDSA itself is correct. Verification uses the same library
/// that signed, so a broken library would agree with itself — the RFC 8032 vectors and the reference
/// corpora are where that question lives. What is checked here is message construction and the
/// selection of signed octets.
/// </para>
/// </remarks>
internal static class SignedFrame
{

    /// <summary>The raw <c>r‖s</c> signature value a frame carries, or <c>null</c> if it carries none.
    /// Used at recording time to decide whether an exchange needs the signature-aware path at all.</summary>
    public static byte[]? SignatureValueOf(byte[] frame) => Decode(frame) switch
    {
        I2.V2G_Message m     => m.Header.Signature?.SignatureValue.Value,
        C.AuthorizationReq r => r.Header.Signature?.SignatureValue.Value,
        // Anything else is, as far as the corpus is concerned, unsigned. Substitute() is the place
        // that refuses loudly, and it is reached only once an exchange has been marked as signed.
        _ => null,
    };

    /// <summary>Re-encodes <paramref name="frame"/> with <paramref name="signatureValue"/> in place of
    /// whatever signature value it carried, header and all.</summary>
    public static byte[] WithSignatureValue(byte[] frame, byte[] signatureValue)
    {
        var (set, message) = DecodeWithSet(frame);
        return Encode(set, Substitute(message, signatureValue));
    }

    /// <summary>Verifies a frame's own signature against its own <c>SignedInfo</c>. This is the half
    /// <see cref="WithSignatureValue"/> throws away, and without it a port could emit any 64 bytes it
    /// liked and still compare equal.</summary>
    public static bool VerifiesWith(byte[] frame, ECDsa publicKey)
    {
        var message = Decode(frame);

        // Both protocols sign in the standalone-xmldsig form — the shape a live Josev peer produces
        // and accepts. -2 hashes with SHA-256; -20's mandatory suite is P-256/SHA-256 here too,
        // because the contract key is P-256 (a P-521 contract key would want SHA-512).
        return message switch
        {
            I2.V2G_Message m when m.Header.Signature is { } s
                => XmlDsigInterop2.VerifyStandaloneXmldsig(s.SignedInfo, s.SignatureValue.Value, publicKey),

            C.AuthorizationReq r when r.Header.Signature is { } s
                => XmlDsigInteropVerify.VerifyStandaloneXmldsig(
                       s.SignedInfo, s.SignatureValue.Value, publicKey, HashAlgorithmName.SHA256),

            _ => false,
        };
    }


    // ── the meter's signature ─────────────────────────────────────────────
    //
    // A second randomised signature, by a second signer, and the reason schema 3 exists. A station
    // with a meter fitted signs its reading into MeterInfo — SigMeterReading in -2, MeterSignature
    // in -20 — and that value is 64 random-looking bytes in a *response*, which is the direction the
    // corpus never had to think about before. Everything below is the header signature's story one
    // field along: record it, substitute it to compare, verify it on its own.
    //
    // What it is NOT is the header signature under another name. That one is the vehicle's, over a
    // message fragment, in XMLDSig. This one is the meter's, over a layout of our own
    // (MeterSigningPayload), in a fixed 64-byte slot. Two signers in one frame is the whole point:
    // a MeteringReceiptReq the EV signed can carry a reading the *station's meter* signed.

    /// <summary>One frame's meter reading, flattened out of two protocols' <c>MeterInfo</c>.</summary>
    /// <param name="Protocol">2 or 20 — part of what was signed, so a -20 reading cannot be replayed
    /// as a -2 one. It never appears on the wire, which is why only a session-level check can catch
    /// it being wrong.</param>
    private sealed record MeterReading(int Protocol, byte[] SessionId, string MeterId,
                                       ulong Reading, long? Timestamp, byte[]? Signature);

    /// <summary>The raw <c>r‖s</c> meter signature a frame carries, or <c>null</c> for the frames —
    /// most of them — whose message type has no <c>MeterInfo</c> at all.</summary>
    public static byte[]? MeterSignatureOf(byte[] frame) => ReadingOf(Decode(frame))?.Signature;

    /// <summary>Re-encodes <paramref name="frame"/> with <paramref name="meterSignature"/> in place of
    /// the one its <c>MeterInfo</c> carried.</summary>
    /// <remarks>
    /// Independent of <see cref="WithSignatureValue"/> on purpose: a frame can carry both, and
    /// substituting one must leave the other exactly as the port produced it. Applying both is the
    /// caller's business, and order does not matter — they touch different fields.
    /// </remarks>
    public static byte[] WithMeterSignature(byte[] frame, byte[] meterSignature)
    {
        var (set, message) = DecodeWithSet(frame);
        return Encode(set, SubstituteMeter(message, meterSignature));
    }

    /// <summary>
    /// Verifies a frame's meter reading against the meter's public key, using only values that were
    /// on the wire — the half <see cref="WithMeterSignature"/> throws away.
    /// </summary>
    /// <remarks>
    /// This is the vehicle's own check, run on recorded bytes: rebuild <see cref="MeterSigningPayload"/>
    /// from the frame's <c>MeterInfo</c> and its <em>header's</em> session id, and verify. Reading the
    /// session id from the header rather than from the recorder is deliberate — the binding is what
    /// stops a reading captured elsewhere being presented here, and checking it against a value we
    /// already hold would check nothing.
    /// </remarks>
    public static bool MeterReadingVerifiesWith(byte[] frame, ECDsa meterKey)
    {
        if (ReadingOf(Decode(frame)) is not { Signature: { } signature } reading)
            return false;

        return SigningMeter.Verify(meterKey, reading.Protocol, reading.SessionId, reading.MeterId,
                                   reading.Reading, reading.Timestamp, signature);
    }


    private static MeterReading? ReadingOf(object message) => message switch
    {
        // -2 carries MeterInfo on three body elements: the two charge-loop responses a station
        // reports through, and the receipt the EV echoes back.
        I2.V2G_Message m => m.Body.BodyElement switch
        {
            I2.ChargingStatusResType  { MeterInfo: { } i } => Iso2(m, i),
            I2.CurrentDemandResType   { MeterInfo: { } i } => Iso2(m, i),
            I2.MeteringReceiptReqType { MeterInfo:     var i } => Iso2(m, i),
            _ => null,
        },

        // -20 has one per message set, because each set generates its own MeterInfoType.
        Ac20.AC_ChargeLoopRes { MeterInfo: { } i } r => Iso20(r.Header.SessionID, i.MeterID,
                                                              i.ChargedEnergyReadingWh,
                                                              i.MeterSignature, i.MeterTimestamp),
        Dc20.DC_ChargeLoopRes { MeterInfo: { } i } r => Iso20(r.Header.SessionID, i.MeterID,
                                                              i.ChargedEnergyReadingWh,
                                                              i.MeterSignature, i.MeterTimestamp),
        _ => null,
    };

    private static MeterReading Iso2(I2.V2G_Message m, I2.MeterInfoType info) =>
        new(2, m.Header.SessionID, info.MeterID, info.MeterReading ?? 0, info.TMeter,
            info.SigMeterReading);

    private static MeterReading Iso20(byte[] sessionId, string meterId, ulong reading,
                                      byte[]? signature, ulong? timestamp) =>
        new(20, sessionId, meterId, reading, (long?) timestamp, signature);


    private static object SubstituteMeter(object message, byte[] meterSignature)
    {
        switch (message)
        {
            case I2.V2G_Message m:
                var body = m.Body.BodyElement switch
                {
                    I2.ChargingStatusResType  { MeterInfo: { } i } b => (I2.BodyBaseType) (b with { MeterInfo = i with { SigMeterReading = meterSignature } }),
                    I2.CurrentDemandResType   { MeterInfo: { } i } b => b with { MeterInfo = i with { SigMeterReading = meterSignature } },
                    I2.MeteringReceiptReqType { MeterInfo: var i } b => b with { MeterInfo = i with { SigMeterReading = meterSignature } },
                    _ => throw NotModelled(m.Body.BodyElement),
                };
                return m with { Body = m.Body with { BodyElement = body } };

            case Ac20.AC_ChargeLoopRes { MeterInfo: { } i } r:
                return r with { MeterInfo = i with { MeterSignature = meterSignature } };

            case Dc20.DC_ChargeLoopRes { MeterInfo: { } i } r:
                return r with { MeterInfo = i with { MeterSignature = meterSignature } };

            default:
                throw NotModelled(message);
        }
    }

    private static NotSupportedException NotModelled(object? message) =>
        new($"the trace corpus does not model a meter signature on {message?.GetType().Name ?? "null"}. " +
             "Add it here rather than letting the comparison skip it.");


    // ── the type-specific parts ───────────────────────────────────────────
    //
    // Deliberately a closed switch that throws on anything unlisted rather than a reflective walk.
    // A trace whose signed message this code does not model must fail loudly at recording time; the
    // alternative is a corpus that silently skips the comparison for exactly the messages it was
    // built to compare.

    private static object Substitute(object message, byte[] signatureValue)
    {
        switch (message)
        {
            case I2.V2G_Message m when m.Header.Signature is { } s:
                return m with
                {
                    Header = m.Header with
                    {
                        Signature = s with { SignatureValue = new I2.SignatureValueType(Id: null, Value: signatureValue) },
                    },
                };

            case C.AuthorizationReq r when r.Header.Signature is { } s:
                return r with
                {
                    Header = r.Header with
                    {
                        Signature = s with { SignatureValue = new C.SignatureValueType(Id: null, Value: signatureValue) },
                    },
                };

            default:
                throw new NotSupportedException(
                    $"the trace corpus does not model a signature on {message.GetType().Name}. " +
                     "Add it here rather than letting the comparison skip it.");
        }
    }

    private static byte[] Encode(MessageSet set, object message)
    {
        // Called as plain statics rather than through the generated extension syntax: this file
        // reaches both protocols via type aliases, and a `using` alias does not bring a namespace's
        // extension methods into scope. Importing both namespaces outright would make SignatureType
        // and half a dozen others ambiguous — the very duplication SessionContext exists to manage.
        var buffer = new byte[8192];
        var ok = message switch
        {
            I2.V2G_Message m          => I2.Iso2Codec.TryEncode(m, buffer, out var n) ? n : -1,
            C.AuthorizationReq r      => C.CommonMessagesCodec.TryEncode(r, buffer, out var n) ? n : -1,
            Ac20.AC_ChargeLoopRes r   => Ac20.AcCodec.TryEncode(r, buffer, out var n) ? n : -1,
            Dc20.DC_ChargeLoopRes r   => Dc20.DcCodec.TryEncode(r, buffer, out var n) ? n : -1,
            _ => throw new NotSupportedException($"cannot re-encode a {message.GetType().Name}."),
        };

        if (ok < 0)
            throw new InvalidOperationException($"re-encoding a {message.GetType().Name} failed.");

        var frame = new byte[V2GTP.HeaderSize + ok];
        if (!V2GTPDispatcher.TryEncode(set, buffer.AsSpan(0, ok), frame, out var written))
            throw new InvalidOperationException($"re-framing a {message.GetType().Name} failed.");

        return frame.AsSpan(0, written).ToArray();
    }

    private static object Decode(byte[] frame) => DecodeWithSet(frame).Message;

    private static (MessageSet Set, object Message) DecodeWithSet(byte[] frame)
    {
        if (!V2GTPDispatcher.TryDecode(frame, out var set, out var message, out var error) || message is null)
            throw new InvalidDataException($"trace: {error}");
        return (set, message);
    }
}
