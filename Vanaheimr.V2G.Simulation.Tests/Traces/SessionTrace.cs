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

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;

namespace Vanaheimr.V2G.Simulation.Tests.Traces;

/// <summary>One V2GTP frame as recorded: the whole frame, header included, as lower-case hex.</summary>
/// <param name="PayloadType">The header's payload type, e.g. <c>0x8002</c> — readable, and redundant
/// against <paramref name="Frame"/> on purpose, because a wrong payload type is otherwise a silent
/// two-byte difference in the middle of a hex blob.</param>
/// <param name="Message">The decoded message name (<c>SessionSetupReq</c>, …). A label for failure
/// messages only — nothing is checked against it. The frame bytes are the oracle.</param>
/// <summary>A P-256 public key, as the two field elements. Enough to verify a raw <c>r‖s</c>
/// signature, and deliberately not a certificate: what is being checked is that the port signed the
/// right octets with the right key, not that a chain validates.</summary>
public sealed record TraceSigningKey(string X, string Y);


/// <param name="Signature">The raw <c>r‖s</c> signature value this frame carried, when it carried one.
/// Its presence is what switches the comparison to the signature-aware path — see
/// <see cref="SignedFrame"/> for why a signed frame cannot simply be compared byte for byte.</param>
/// <param name="MeterSignature">The raw <c>r‖s</c> value in this frame's <c>MeterInfo</c> —
/// <c>SigMeterReading</c> (-2) or <c>MeterSignature</c> (-20) — when it carries one. A *second*
/// randomised signature, by a *different* signer, in a different place: the station's meter signs
/// this one, and it rides in the body rather than the header. Recorded separately for exactly the
/// reason the header signature is, and separately from it because substituting one must not disturb
/// the other.</param>
public sealed record TraceFrame(string PayloadType, string Message, string Frame,
                                string? Signature = null, string? MeterSignature = null)
{
    [JsonIgnore]
    public byte[] Bytes => Convert.FromHexString(Frame);

    [JsonIgnore]
    public byte[]? SignatureBytes => Signature is null ? null : Convert.FromHexString(Signature);

    [JsonIgnore]
    public byte[]? MeterSignatureBytes =>
        MeterSignature is null ? null : Convert.FromHexString(MeterSignature);

    [JsonIgnore]
    public bool IsSigned => Signature is not null;

    [JsonIgnore]
    public bool CarriesMeterSignature => MeterSignature is not null;
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
/// randomised, so any PnC-signed request differs on every run and cannot be compared as bytes.
/// <b>Schema 2 (2026-07-31) lifts that</b>: a frame carrying a signature records its signature value
/// separately and is compared through <see cref="SignedFrame"/>, which substitutes the recorded value
/// before comparing and verifies the produced one on its own. Everything but those 64 bytes is still
/// compared exactly.
/// </para>
/// </remarks>
/// <param name="SigningKey">The public half of the identity a signed session signs with — present
/// exactly when some exchange is signed. Verification needs a key from outside the frame, or a port
/// could sign with anything at all and still compare equal.</param>
/// <param name="MeterKey">The public half of the station's <b>meter</b> key, present exactly when
/// some frame carries a meter signature. Deliberately a second key rather than a reuse of
/// <paramref name="SigningKey"/>: the whole point of <c>SigMeterReading</c> is that the meter signs
/// what it measured rather than the station asserting it, so a corpus that verified both with one
/// key would quietly erase the distinction it exists to record.
/// <para>
/// <b>And it proves less than it looks like.</b> In a recording the key necessarily travels with the
/// session it authenticates, so verifying against it shows the reading was not altered between meter
/// and file and is bound to this session — not that the station is who it claims. For that the key
/// has to arrive out of band, which in this project is the pairing code's <c>meter</c> field
/// (<c>docs/CONCEPT.md</c> §4.5). Anything reading this file should say so rather than show a tick.
/// </para></param>
public sealed record SessionTrace(
    string                       Name,
    string                       Protocol,
    string                       Mode,
    string                       Note,
    IReadOnlyList<TraceExchange> Exchanges,
    TraceSigningKey?             SigningKey = null,
    TraceSigningKey?             MeterKey   = null)
{

    /// <summary>
    /// 3 since 2026-08-03: frames gained an optional <c>meterSignature</c> and traces an optional
    /// <c>meterKey</c>, so a station's signed meter reading can be recorded without making the
    /// session incomparable.
    /// </summary>
    /// <remarks>
    /// Bumped rather than added silently, for the same reason 2 was: a reader that does not know
    /// about the second signature would compare a meter-signed response byte for byte and fail on
    /// 64 random bytes — or, worse, a reader that skips responses would pass and check nothing.
    /// (2 since 2026-07-31: the header signature and <c>signingKey</c>.)
    /// </remarks>
    public const int SchemaVersion = 3;

    /// <remarks>
    /// Nulls are omitted since schema 3. With two optional signatures per frame and neither present
    /// on the overwhelming majority of them, writing both out would bury the handful of frames that
    /// carry one under four hundred lines that say "no" — in a file whose whole job is to be read by
    /// somebody deciding whether a regeneration's diff is innocent. The readers all treat an absent
    /// key as null already, which is what makes this safe.
    /// </remarks>
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented          = true,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };


    /// <summary>
    /// Splits both recorded directions into V2GTP frames and pairs them up.
    /// </summary>
    public static SessionTrace Build(string name, string protocol, string mode, string note,
                                     byte[] sent, byte[] received, TraceSigningKey? signingKey = null,
                                     TraceSigningKey? meterKey = null)
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

        // A signed session with no key would record signatures nobody can check, which is worse than
        // not recording them: the byte comparison would pass and the verification would be skipped.
        if (exchanges.Any(e => e.Request.IsSigned) && signingKey is null)
            throw new InvalidDataException(
                $"trace '{name}' has signed requests but no signing key — the signature-aware " +
                 "comparison would substitute the recorded value and verify nothing.");

        // The same rule for the meter's key, and it has to be checked in both directions: a meter
        // reading reaches the corpus in a station *response*, which is precisely the direction the
        // first version of this comparison never looked at.
        if (exchanges.Any(e => e.Request.CarriesMeterSignature || e.Response.CarriesMeterSignature)
            && meterKey is null)
            throw new InvalidDataException(
                $"trace '{name}' carries a meter signature but no meter key — the substitution " +
                 "would remove the only random part and leave nothing checking it.");

        return new SessionTrace(name, protocol, mode, note, exchanges, signingKey, meterKey);

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

        // The SAP frames are not V2G messages and have no header to carry a signature; asking would
        // mean decoding them with the wrong codec.
        var signature = isSap ? null : SignedFrame.SignatureValueOf(frame);
        var meter     = isSap ? null : SignedFrame.MeterSignatureOf(frame);

        static string? Hex(byte[]? bytes) =>
            bytes is null ? null : Convert.ToHexString(bytes).ToLowerInvariant();

        return new TraceFrame($"0x{payloadType:X4}", FrameLabel.Describe(frame, isSap).Message,
                              Convert.ToHexString(frame).ToLowerInvariant(),
                              Hex(signature), Hex(meter));
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
            signingKey    = SigningKey,
            meterKey      = MeterKey,
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

        static TraceSigningKey? KeyAt(JsonElement root, string name) =>
            root.TryGetProperty(name, out var key) && key.ValueKind is not JsonValueKind.Null
                ? key.Deserialize<TraceSigningKey>(Json)
                : null;

        return new SessionTrace(
            root.GetProperty("name").GetString()!,
            root.GetProperty("protocol").GetString()!,
            root.GetProperty("mode").GetString()!,
            root.GetProperty("note").GetString()!,
            root.GetProperty("exchanges").Deserialize<List<TraceExchange>>(Json)!,
            KeyAt(root, "signingKey"),
            KeyAt(root, "meterKey"));

    }


    public void WriteTo(string path) =>
        File.WriteAllText(path, ToJson(), new UTF8Encoding(false));

    public static SessionTrace ReadFrom(string path) =>
        FromJson(File.ReadAllText(path));

}
