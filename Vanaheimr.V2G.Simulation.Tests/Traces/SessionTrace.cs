using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Vanaheimr.V2G.AppProtocol;
using Vanaheimr.V2G.Iso15118_2.Generated;
using Vanaheimr.V2G.Tp;

namespace Vanaheimr.V2G.Simulation.Tests.Traces;

/// <summary>One V2GTP frame as recorded: the whole frame, header included, as lower-case hex.</summary>
/// <param name="PayloadType">The header's payload type, e.g. <c>0x8002</c> — readable, and redundant
/// against <paramref name="Frame"/> on purpose, because a wrong payload type is otherwise a silent
/// two-byte difference in the middle of a hex blob.</param>
/// <param name="Message">The decoded message name (<c>SessionSetupReq</c>, …). A label for failure
/// messages only — nothing is checked against it. The frame bytes are the oracle.</param>
public sealed record TraceFrame(string PayloadType, string Message, string Frame)
{
    [JsonIgnore]
    public byte[] Bytes => Convert.FromHexString(Frame);
}


/// <summary>One request/response pair. -2 and -20 sessions are strictly alternating, which is what makes
/// pairing by position sound; <see cref="SessionTrace.Build"/> checks that the two directions really did
/// carry the same number of frames rather than assuming it.</summary>
public sealed record TraceExchange(int Index, TraceFrame Request, TraceFrame Response);


/// <summary>
/// A recorded EV↔station session: every V2GTP frame in order, from the SupportedAppProtocol handshake to
/// SessionStop. The oracle for the EVCC ports (<c>docs/CONCEPT.md</c> §5, A4).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The codec port had a byte-exact oracle — the libcbv2g vector corpus — and A4
/// was written down as having none: <i>"there is no vector corpus for behaviour"</i>, with B0's Pi named
/// as the realistic substitute. Deferring the Pi hardware would then have deferred any check on A4 too.
/// A recorded session is the same construction one layer up: instead of one message's bytes, the ordered
/// bytes of a whole session, replayable without a peer.
/// </para>
/// <para>
/// <b>What it can and cannot catch.</b> It pins a port to what the C# EVCC does, byte for byte and in
/// order — a wrong field, a skipped phase, a mis-ordered poll, a bad V2GTP header. It cannot catch a bug
/// the C# EVCC has too. That is a real limit and the reason C# is a defensible reference rather than a
/// correct one: it is the implementation that earned the live-interop conformance fixes against Josev
/// (§1.3). A trace says "the port agrees with the implementation that has actually talked to somebody
/// else", which is a different and weaker claim than conformance.
/// </para>
/// <para>
/// <b>Determinism.</b> Byte-exactness across a replay requires the requests to be a pure function of the
/// responses. That holds for EIM sessions with a pinned clock, and not otherwise: ECDSA signing is
/// randomised, so any PnC-signed request differs on every run and cannot be compared as bytes. The
/// corpus is therefore EIM, and a signature-aware comparison is owed before PnC can join it.
/// </para>
/// </remarks>
public sealed record SessionTrace(
    string                       Name,
    string                       Protocol,
    string                       Mode,
    string                       Note,
    IReadOnlyList<TraceExchange> Exchanges)
{

    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented        = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };


    /// <summary>
    /// Splits both recorded directions into V2GTP frames and pairs them up.
    /// </summary>
    public static SessionTrace Build(string name, string protocol, string mode, string note,
                                     byte[] sent, byte[] received)
    {

        var requests  = SplitFrames(sent,     "EV→station");
        var responses = SplitFrames(received, "station→EV");

        if (requests.Count != responses.Count)
            throw new InvalidDataException(
                $"trace '{name}': {requests.Count} request frame(s) but {responses.Count} response frame(s) — " +
                 "the session is not strictly alternating, so pairing by position would be wrong.");

        var exchanges = requests
            .Select((request, i) => new TraceExchange(
                i,
                // The SupportedAppProtocol handshake shares payload id 0x8001 with the -2 messages and is
                // told apart by session phase, never by payload type (see V2GTP.PayloadType_AppProtocol).
                // Phase, here, is position: SAP is always the first exchange.
                Describe(request,      isSap: i == 0),
                Describe(responses[i], isSap: i == 0)))
            .ToList();

        return new SessionTrace(name, protocol, mode, note, exchanges);

    }


    private static List<byte[]> SplitFrames(byte[] bytes, string direction)
    {

        var frames = new List<byte[]>();
        var offset = 0;

        while (offset < bytes.Length)
        {

            if (!V2GTP.TryReadHeader(bytes.AsSpan(offset), out _, out var payloadLength))
                throw new InvalidDataException(
                    $"trace ({direction}): no valid V2GTP header at offset {offset} of {bytes.Length}.");

            var total = V2GTP.HeaderSize + checked((int) payloadLength);
            if (offset + total > bytes.Length)
                throw new InvalidDataException(
                    $"trace ({direction}): the frame at offset {offset} declares {payloadLength} payload byte(s), " +
                    $"but only {bytes.Length - offset - V2GTP.HeaderSize} remain — a truncated recording.");

            frames.Add(bytes[offset..(offset + total)]);
            offset += total;

        }

        return frames;

    }


    private static TraceFrame Describe(byte[] frame, bool isSap)
    {
        V2GTP.TryReadHeader(frame, out var payloadType, out _);
        return new TraceFrame($"0x{payloadType:X4}", Label(frame, isSap),
                              Convert.ToHexString(frame).ToLowerInvariant());
    }


    /// <summary>Best-effort name for the message in a frame. Only ever used in failure text, so a frame
    /// that will not decode is labelled rather than fatal — the bytes are still worth recording, and a
    /// codec that cannot read back what the session just sent is a finding for a different test.</summary>
    private static string Label(byte[] frame, bool isSap)
    {
        try
        {

            if (isSap)
                return SupportedAppProtocolCodec.DecodeAny(frame.AsSpan(V2GTP.HeaderSize), out _)
                                                .GetType().Name;

            if (!V2GTPDispatcher.TryDecode(frame, out _, out var message, out _) || message is null)
                return "undecodable";

            // -2 wraps everything in V2G_Message; the interesting name is the body element. The -20 sets
            // decode straight to the concrete message type.
            return message is V2G_Message v2g
                       ? v2g.Body.BodyElement?.GetType().Name ?? "V2G_Message(empty body)"
                       : message.GetType().Name;

        }
        catch (Exception e)
        {
            return $"undecodable({e.GetType().Name})";
        }
    }


    // ── files ─────────────────────────────────────────────────────────────

    public string ToJson() =>
        JsonSerializer.Serialize(new
        {
            schemaVersion = SchemaVersion,
            generator     = "Vanaheimr.V2G.Simulation.Tests.Traces.SessionTraceCorpusTests.RegenerateTheCorpus",
            name          = Name,
            protocol      = Protocol,
            mode          = Mode,
            note          = Note,
            exchanges     = Exchanges,
        }, Json);


    public static SessionTrace FromJson(string json)
    {

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var version = root.GetProperty("schemaVersion").GetInt32();
        if (version != SchemaVersion)
            throw new InvalidDataException(
                $"trace schema version {version}, this build understands {SchemaVersion}.");

        return new SessionTrace(
            root.GetProperty("name").GetString()!,
            root.GetProperty("protocol").GetString()!,
            root.GetProperty("mode").GetString()!,
            root.GetProperty("note").GetString()!,
            root.GetProperty("exchanges").Deserialize<List<TraceExchange>>(Json)!);

    }


    public void WriteTo(string path) =>
        File.WriteAllText(path, ToJson(), new UTF8Encoding(false));

    public static SessionTrace ReadFrom(string path) =>
        FromJson(File.ReadAllText(path));

}
