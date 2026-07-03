using NUnit.Framework;
using Vanaheimr.V2G.AppProtocol;
using Vanaheimr.V2G.Exi.Tests.Infrastructure;

namespace Vanaheimr.V2G.Exi.Tests;

/// <summary>
/// Vector-driven conformance tests for the AppProtocol codec.
/// <para>
/// Each vector is exercised three ways:
/// <list type="number">
///   <item><b>Encode match</b> — encoder output must equal <c>expectedHex</c> byte-for-byte.</item>
///   <item><b>Decode match</b> — decoding <c>expectedHex</c> must yield the input message.</item>
///   <item><b>Roundtrip</b>    — encoded bytes decode back to the input.</item>
/// </list>
/// </para>
/// <para>
/// <b>What "passing" means right now.</b> The seed vectors in <c>AppProtocol.vectors.json</c>
/// were produced by a Python simulator of this same codec. A passing run therefore proves
/// only that the codec is internally self-consistent — it does <i>not</i> prove conformance
/// to the EXI / ISO 15118 wire format. To gain wire conformance, regenerate the
/// <c>expectedHex</c> values using cbV2G or OpenV2G as an external reference.
/// </para>
/// </summary>
[TestFixture]
public class AppProtocolVectorTests
{
    [TestCaseSource(typeof(AppProtocolVectorSource), nameof(AppProtocolVectorSource.All))]
    public void Encode_Matches_Expected(string file, Vector v)
    {
        var expected = HexUtil.Parse(v.ExpectedHex);
        Assert.That(expected.Length, Is.EqualTo(v.ExpectedBytes),
            "vector's expectedBytes does not match the parsed length of expectedHex");

        // Generously sized buffer; tighter sizing comes once we have a max-size analysis.
        var buf = new byte[1024];
        int written;
        bool ok;

        switch (v.MessageType)
        {
            case "SupportedAppProtocolReq":
                ok = SupportedAppProtocolCodec.TryEncodeRequest(
                    VectorInputBinder.BindRequest(v.Input), buf, out written);
                break;
            case "SupportedAppProtocolRes":
                ok = SupportedAppProtocolCodec.TryEncodeResponse(
                    VectorInputBinder.BindResponse(v.Input), buf, out written);
                break;
            default:
                throw new NotSupportedException(
                    $"Unknown messageType '{v.MessageType}' in {file} / {v.Name}.");
        }

        Assert.That(ok, Is.True, $"{file}/{v.Name}: encoder returned false");

        var actual = buf.AsSpan(0, written).ToArray();
        if (!actual.AsSpan().SequenceEqual(expected))
            Assert.Fail($"{file}/{v.Name}: encode mismatch\n{HexUtil.Diff(expected, actual)}");
    }

    [TestCaseSource(typeof(AppProtocolVectorSource), nameof(AppProtocolVectorSource.All))]
    public void Decode_Matches_Input(string file, Vector v)
    {
        var bytes   = HexUtil.Parse(v.ExpectedHex);
        var decoded = SupportedAppProtocolCodec.DecodeAny(bytes, out int consumed);

        Assert.That(consumed, Is.EqualTo(bytes.Length), "did not consume all bytes");

        switch (v.MessageType)
        {
            case "SupportedAppProtocolReq":
            {
                var expReq = VectorInputBinder.BindRequest(v.Input);
                Assert.That(decoded, Is.InstanceOf<SupportedAppProtocolReq>());
                var gotReq = (SupportedAppProtocolReq)decoded;
                Assert.That(gotReq.AppProtocols, Is.EqualTo(expReq.AppProtocols));
                break;
            }
            case "SupportedAppProtocolRes":
                Assert.That(decoded, Is.EqualTo(VectorInputBinder.BindResponse(v.Input)));
                break;
            default:
                throw new NotSupportedException(v.MessageType);
        }
    }

    [TestCaseSource(typeof(AppProtocolVectorSource), nameof(AppProtocolVectorSource.All))]
    public void Roundtrip(string file, Vector v)
    {
        var buf = new byte[1024];
        int written;

        switch (v.MessageType)
        {
            case "SupportedAppProtocolReq":
            {
                var input = VectorInputBinder.BindRequest(v.Input);
                Assert.That(
                    SupportedAppProtocolCodec.TryEncodeRequest(input, buf, out written),
                    Is.True);
                var got = (SupportedAppProtocolReq)
                    SupportedAppProtocolCodec.DecodeAny(buf.AsSpan(0, written), out _);
                Assert.That(got.AppProtocols, Is.EqualTo(input.AppProtocols));
                break;
            }
            case "SupportedAppProtocolRes":
            {
                var input = VectorInputBinder.BindResponse(v.Input);
                Assert.That(
                    SupportedAppProtocolCodec.TryEncodeResponse(input, buf, out written),
                    Is.True);
                Assert.That(
                    SupportedAppProtocolCodec.DecodeAny(buf.AsSpan(0, written), out _),
                    Is.EqualTo(input));
                break;
            }
            default:
                throw new NotSupportedException(v.MessageType);
        }
    }
}
