using System.Security.Cryptography;

using Vanaheimr.V2G.Simulation.StateMachines.Iso2;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using Vanaheimr.V2G.Tp;

using C  = Vanaheimr.V2G.Iso15118_20.CommonMessages.Generated;
using I2 = Vanaheimr.V2G.Iso15118_2.Generated;

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
            I2.V2G_Message m     => I2.Iso2Codec.TryEncode(m, buffer, out var n) ? n : -1,
            C.AuthorizationReq r => C.CommonMessagesCodec.TryEncode(r, buffer, out var n) ? n : -1,
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
