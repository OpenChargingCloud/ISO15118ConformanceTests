using System.Net;
using System.Security.Cryptography;

using NUnit.Framework;

using Vanaheimr.V2G.Simulation.Sap;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso2;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using Vanaheimr.V2G.Simulation.Tests.Timing;
using Vanaheimr.V2G.Simulation.Transport;

namespace Vanaheimr.V2G.Simulation.Tests.Traces;

/// <summary>
/// The session-trace corpus: whole EV↔station sessions recorded frame by frame, checked in, and replayed
/// against the EVCC that produced them.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it is for.</b> The EVCC state machines have to exist three times (C#, Kotlin, Swift — <c>A4</c>),
/// and unlike the codecs they have no reference implementation to be held to. This is the substitute:
/// one implementation's sessions, recorded, so the ports can be checked against something outside
/// themselves. <see cref="SessionTrace"/> spells out what that does and does not prove.
/// </para>
/// <para>
/// <b>Why the C# EVCC replays its own recording.</b> It sounds circular, and as a correctness check it
/// is. It is not there for correctness — it is there to prove the corpus is <i>replayable</i>: that a
/// session really is a pure function of the responses, that nothing in the loop reaches for a clock or a
/// random number that a file cannot supply, and that the replay harness the two ports are about to copy
/// actually works. Finding that out from Kotlin, with a fresh port also under suspicion, would cost far
/// more than finding it out here. It is the same reason the cross-emitter comparison had to be built
/// before it could be counted as a gate (§5, Track A note).
/// </para>
/// <para>
/// <b>EIM only, for now.</b> Byte-exact replay needs the requests to be a pure function of the responses.
/// ECDSA signing is randomised, so a PnC session's signed AuthorizationReq differs on every run and could
/// only be compared with the signature excluded. That comparison is owed; until it exists, saying "the
/// corpus is EIM" is more honest than quietly recording PnC and comparing everything but the part that
/// makes it PnC.
/// </para>
/// </remarks>
[TestFixture]
public class SessionTraceCorpusTests
{

    /// <summary>Pinned so a re-recording is byte-identical. -20 puts a timestamp in every message header,
    /// so without this the corpus could not be regenerated and diffed at all.</summary>
    private static readonly DateTimeOffset RecordedAt = DateTimeOffset.FromUnixTimeSeconds(1_767_225_600);

    /// <summary>Likewise pinned — see <c>Secc2.FixedSessionId</c> for why this seam exists.</summary>
    private static readonly byte[] RecordedSessionId = [0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11];

    /// <summary>Pinned too: -20 sends a 16-byte challenge in every AuthorizationSetupRes, EIM sessions
    /// included. Test material, never a live challenge.</summary>
    private static readonly byte[] RecordedGenChallenge =
        [0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x29, 0x2A, 0x2B, 0x2C, 0x2D, 0x2E, 0x2F];

    private static readonly TimeSpan SeccSequenceTimeout = TimeSpan.FromSeconds(60);


    /// <summary>One recordable session. Both sides are spelled out as delegates because the recorder needs
    /// to drive the EVCC over a stream of its choosing — a socket when recording, a file when replaying —
    /// and that is the whole trick: the EVCC never learns which it got.</summary>
    private sealed record Scenario(
        string                                            Name,
        string                                            Protocol,
        string                                            Mode,
        string                                            Note,
        Func<Stream, TimeProvider, CancellationToken, Task> RunEvcc,
        Func<Stream, TimeProvider, CancellationToken, Task> RunSecc,
        bool                                              Signed = false);


    private static readonly Scenario[] Scenarios =
    [
        new("iso2-ac-eim", "iso15118-2", "ac",
            "AC, external payment, happy path: SAP, setup, service discovery, authorization poll, " +
            "charge-parameter discovery, three charging-status cycles, stop.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_2, ct, PowerMode.Ac);
                await new Evcc2(stream, PowerMode.Ac, clock, new ImmediateAsyncDelay(),
                                LoopbackTimeouts.PerMessage).RunAsync(ct);
            },
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_2, ct, PowerMode.Ac);
                await new Secc2(PowerMode.Ac, SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge }.RunAsync(stream, ct);
            }),

        new("iso2-dc-eim", "iso15118-2", "dc",
            "DC, external payment: as the AC session, plus cable check, pre-charge, current demand " +
            "and welding detection — the phases AC does not have.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_2, ct, PowerMode.Dc);
                await new Evcc2(stream, PowerMode.Dc, clock, new ImmediateAsyncDelay(),
                                LoopbackTimeouts.PerMessage).RunAsync(ct);
            },
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_2, ct, PowerMode.Dc);
                await new Secc2(PowerMode.Dc, SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge }.RunAsync(stream, ct);
            }),

        new("iso20-ac-eim", "iso15118-20", "ac",
            "-20 AC, EIM. Crosses between the CommonMessages and AC message sets, which are separate " +
            "grammars with separate V2GTP payload types — a port that muddles the two is visible here " +
            "in the frame header, two bytes in.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Ac);
                await new Evcc20Ac(stream, clock, new ImmediateAsyncDelay(),
                                   LoopbackTimeouts.PerMessage).RunAsync(ct);
            },
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Ac);
                await new Secc20Ac(SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge }.RunAsync(stream, ct);
            }),

        new("iso20-dc-eim", "iso15118-20", "dc",
            "-20 DC, EIM: CommonMessages plus the DC set, including the cable-check and pre-charge " +
            "poll phases and welding detection.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                await new Evcc20Dc(stream, clock, new ImmediateAsyncDelay(),
                                   LoopbackTimeouts.PerMessage).RunAsync(ct);
            },
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                await new Secc20Dc(SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge }.RunAsync(stream, ct);
            }),

        // ── signed sessions ───────────────────────────────────────────────
        //
        // The reason schema 2 exists. Both of these carry a signed AuthorizationReq, which is not
        // byte-reproducible: ECDSA picks its nonce at random. SignedFrame is what makes them
        // comparable anyway — see it for the argument, and TheSignatureAwareComparisonActuallyBites
        // below for the evidence that it is a check rather than a hole.

        new("iso2-ac-pnc", "iso15118-2", "ac",
            "AC, Plug & Charge: PaymentDetails carries the contract chain, the AuthorizationReq is " +
            "signed over its own EXI fragment in the Josev interop form. The only session in the " +
            "corpus whose requests are not a pure function of the responses.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_2, ct, PowerMode.Ac);
                using var contractKey = PncMaterial.Key();
                await new Evcc2(stream, PowerMode.Ac, clock, new ImmediateAsyncDelay(),
                                LoopbackTimeouts.PerMessage)
                          {
                              Pnc = new PncEvccOptions(PncMaterial.Certificate(),
                                                       [PncMaterial.Certificate()], contractKey),
                          }.RunAsync(ct);
            },
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_2, ct, PowerMode.Ac);
                await new Secc2(PowerMode.Ac, SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge }.RunAsync(stream, ct);
            },
            Signed: true),

        new("iso20-dc-pnc", "iso15118-20", "dc",
            "-20 DC, Plug & Charge: the AuthorizationReq echoes the station's GenChallenge and " +
            "carries the contract chain, signed over the PnC_AReqAuthorizationMode fragment.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                using var contractKey = PncMaterial.Key();
                await new Evcc20Dc(stream, clock, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage)
                          {
                              Pnc = new PncEvccOptions(PncMaterial.Certificate(),
                                                       [PncMaterial.Certificate()], contractKey),
                          }.RunAsync(ct);
            },
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                await new Secc20Dc(SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge }.RunAsync(stream, ct);
            },
            Signed: true),
    ];


    private static IEnumerable<string> ScenarioNames => Scenarios.Select(s => s.Name);

    private static string FileName(string name) => $"Session.{name}.trace.json";

    /// <summary>Where the tests read it from — copied next to the assembly by the csproj.</summary>
    private static string TracePath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Vectors", FileName(name));

    /// <summary>Where the regenerator writes it: the source tree. Writing to the output directory would
    /// produce a corpus that vanishes on the next clean, and one the Kotlin and Swift suites — which read
    /// it out of the source tree — would never see.</summary>
    private static string SourceTracePath(string name)
    {

        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Vanaheimr.V2G.Simulation.Tests.csproj")))
            dir = dir.Parent;

        Assert.That(dir, Is.Not.Null, "could not find the test project directory");

        var vectors = Path.Combine(dir!.FullName, "Vectors");
        Directory.CreateDirectory(vectors);
        return Path.Combine(vectors, FileName(name));

    }


    /// <summary>
    /// Records every scenario over a real loopback socket and writes the corpus.
    /// <see cref="ExplicitAttribute"/> for the same reason the meter corpus is: these files are an oracle
    /// for two other languages, so they must change when somebody means them to and never as a side
    /// effect of a test run.
    /// </summary>
    /// <summary>
    /// Creates the fixed contract certificate the signed scenarios use. Separate from the corpus
    /// regenerator and even more deliberate: its bytes are an *input* to every recorded PnC session,
    /// so running this invalidates those traces and every port checked against them. Run it, then run
    /// <see cref="RegenerateTheCorpus"/>.
    /// </summary>
    [Test, Explicit("Regenerates Vectors/Session.pnc-contract.der — invalidates the signed traces")]
    public void RegenerateThePncCertificate()
    {
        PncMaterial.Regenerate();
        TestContext.Out.WriteLine($"wrote {PncMaterial.SourceCertificatePath()}");
    }


    [Test, Explicit("Regenerates Vectors/Session.*.trace.json — run deliberately")]
    public async Task RegenerateTheCorpus()
    {
        foreach (var scenario in Scenarios)
        {
            var trace = await RecordAsync(scenario);
            var path  = SourceTracePath(scenario.Name);
            trace.WriteTo(path);
            TestContext.Out.WriteLine($"{scenario.Name}: {trace.Exchanges.Count} exchanges → {path}");
        }
    }


    private static async Task<SessionTrace> RecordAsync(Scenario scenario)
    {

        using var cts      = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

        var clock = new ManualTimeProvider(RecordedAt);

        var seccTask = Task.Run(async () =>
        {
            using var seccStream = await listener.AcceptAsync(cts.Token);
            await scenario.RunSecc(seccStream, clock, cts.Token);
        }, cts.Token);

        using var socket = await TcpV2GClient.ConnectAsync(
                                     IPAddress.Loopback.ToString(), listener.LocalEndpoint.Port, ct: cts.Token);

        var recorder = new RecordingStream(socket);
        await Task.WhenAll(scenario.RunEvcc(recorder, clock, cts.Token), seccTask);

        TraceSigningKey? signingKey = null;
        if (scenario.Signed)
        {
            using var publicKey = PncMaterial.PublicKey();
            var q = publicKey.ExportParameters(includePrivateParameters: false).Q;
            signingKey = new TraceSigningKey(Convert.ToHexString(q.X!).ToLowerInvariant(),
                                             Convert.ToHexString(q.Y!).ToLowerInvariant());
        }

        return SessionTrace.Build(scenario.Name, scenario.Protocol, scenario.Mode, scenario.Note,
                                  recorder.Sent, recorder.Received, signingKey);

    }


    /// <summary>
    /// The corpus is replayable: the EVCC, given nothing but the recorded responses, emits byte for byte
    /// the requests that were recorded — including the V2GTP headers and the SupportedAppProtocol
    /// handshake. This is the gate the Kotlin and Swift ports will meet in their own languages.
    /// </summary>
    [Test]
    public async Task EveryTraceReplaysByteExactly([ValueSource(nameof(ScenarioNames))] string name)
    {

        var scenario = Scenarios.Single(s => s.Name == name);
        var path     = TracePath(name);

        Assert.That(File.Exists(path), Is.True,
                    $"trace missing: {path} — run RegenerateTheCorpus");

        var trace  = SessionTrace.ReadFrom(path);
        var replay = new TraceReplayStream(trace);

        await scenario.RunEvcc(replay, new ManualTimeProvider(RecordedAt), CancellationToken.None);

        Assert.That(replay.Complete, Is.True,
                    $"the session stopped after {replay.Replayed} of {trace.Exchanges.Count} recorded " +
                     "exchanges — it ended early, which sends no wrong bytes and would otherwise pass.");

    }


    /// <summary>
    /// Recording again produces the same file. That is what makes a checked-in corpus reviewable: a
    /// regeneration whose diff is total says nothing about what actually changed, and the session id and
    /// the -20 GenChallenge — both random by default, both present in every session — would each be
    /// enough on their own to make every frame differ.
    /// </summary>
    /// <remarks>
    /// A running check rather than a note in a doc comment, because this repository has now recorded that
    /// distinction four times (<c>docs/CONCEPT.md</c> §5, Track A note) and once more here: the meter
    /// corpus claims in its own summary that "anyone can regenerate it and get identical bytes", and its
    /// signatures are randomised ECDSA, so that sentence is true only of the payload field.
    /// </remarks>
    [Test]
    public async Task RecordingTheCorpusAgainProducesTheSameBytes(
        [ValueSource(nameof(ScenarioNames))] string name)
    {

        var scenario = Scenarios.Single(s => s.Name == name);
        var recorded = await RecordAsync(scenario);
        var onDisk   = SessionTrace.ReadFrom(TracePath(name));

        if (!scenario.Signed)
        {
            Assert.That(recorded.ToJson(), Is.EqualTo(onDisk.ToJson()),
                        "a fresh recording differs from the checked-in one — either something is no " +
                        "longer pinned, or the session really changed and the corpus needs " +
                        "regenerating on purpose");
            return;
        }

        // A signed session is reproducible *except* for its signature values, and saying so precisely
        // is better than exempting it. Compare under exactly the rule the replay uses: substitute the
        // checked-in signature, then require the bytes to be identical.
        Assert.That(recorded.Exchanges, Has.Count.EqualTo(onDisk.Exchanges.Count));

        for (var i = 0; i < onDisk.Exchanges.Count; i++)
        {
            var (fresh, old) = (recorded.Exchanges[i].Request, onDisk.Exchanges[i].Request);

            Assert.That(fresh.IsSigned, Is.EqualTo(old.IsSigned),
                        $"exchange {i}: one recording thinks this frame is signed and the other does not");

            var comparable = old.SignatureBytes is { } signature
                                 ? Convert.ToHexString(SignedFrame.WithSignatureValue(fresh.Bytes, signature))
                                          .ToLowerInvariant()
                                 : fresh.Frame;

            Assert.That(comparable, Is.EqualTo(old.Frame), $"exchange {i} ({old.Message}) differs");
        }

        // And the part the substitution discards: two runs must produce *different* signatures, or the
        // signing is not randomised and this whole mechanism was unnecessary.
        var signedPairs = recorded.Exchanges.Zip(onDisk.Exchanges)
                                  .Where(p => p.First.Request.IsSigned)
                                  .ToList();

        Assert.That(signedPairs, Is.Not.Empty, "a scenario marked Signed recorded no signed request");
        Assert.That(signedPairs.Any(p => p.First.Request.Signature != p.Second.Request.Signature),
                    "two recordings produced identical signatures — if ECDSA here were deterministic, " +
                    "the signature-aware comparison would be solving a problem that does not exist");

    }


    /// <summary>
    /// A trace that stops before the interesting phases replays perfectly and proves nothing, so the
    /// corpus is checked for the shapes it is supposed to contain rather than only for replaying.
    /// </summary>
    [Test]
    public void TheCorpusCoversTheSessionShapes([ValueSource(nameof(ScenarioNames))] string name)
    {

        var trace    = SessionTrace.ReadFrom(TracePath(name));
        var messages = trace.Exchanges.Select(e => e.Request.Message).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(trace.Exchanges, Has.Count.GreaterThan(8));

            Assert.That(messages[0], Does.StartWith("SupportedAppProtocol"),
                        "every session starts with the SAP handshake, and the ports need it recorded");

            Assert.That(messages, Has.Some.Matches<string>(m => m.StartsWith("SessionSetupReq")));
            Assert.That(messages, Has.Some.Matches<string>(m => m.StartsWith("SessionStopReq")),
                        "a trace that never reaches SessionStop is a truncated session");

            // The DC-only phases. Their absence from an AC trace is equally part of the shape: an EVCC
            // that ran a cable check on AC would be wrong, and only a per-mode expectation says so.
            // Substring, not prefix: the same phase is WeldingDetectionReqType in -2 and
            // DC_WeldingDetectionReq in -20, which is a naming difference and not a behavioural one.
            var dc = trace.Mode is "dc";
            Assert.That(messages.Any(m => m.Contains("WeldingDetection")), Is.EqualTo(dc),
                        $"welding detection is DC-only; this is the '{trace.Mode}' trace");
            Assert.That(messages.Any(m => m.Contains("CableCheck")), Is.EqualTo(dc),
                        $"the cable check is DC-only; this is the '{trace.Mode}' trace");
            Assert.That(messages.Any(m => m.Contains("PreCharge")), Is.EqualTo(dc),
                        $"pre-charge is DC-only; this is the '{trace.Mode}' trace");

            // Every frame's payload type must be one the dispatcher recognises, and -20 must actually
            // cross message sets — a -20 session that never leaves CommonMessages is not a -20 session.
            var payloadTypes = trace.Exchanges
                                    .SelectMany(e => new[] { e.Request.PayloadType, e.Response.PayloadType })
                                    .Distinct().ToList();

            if (trace.Protocol is "iso15118-20")
                Assert.That(payloadTypes, Has.Count.GreaterThan(1),
                            "a -20 trace whose frames all carry one payload type never left CommonMessages");

            // A trace named for Plug & Charge that recorded no signature would be an EIM session with
            // a misleading name — and the signature-aware comparison would never run on it.
            var isPnc = name.EndsWith("-pnc");
            Assert.That(trace.Exchanges.Any(e => e.Request.IsSigned), Is.EqualTo(isPnc),
                        isPnc ? "a PnC trace with no signed request is an EIM session under another name"
                              : "an EIM trace should carry no signatures");
            Assert.That(trace.SigningKey is not null, Is.EqualTo(isPnc),
                        "the signing key and the signatures must appear together, or verification is skipped");
        });

    }


    /// <summary>
    /// The replay harness fails when it should. Without this the previous test's green could equally mean
    /// "the comparison never compares anything" — the failure mode a corpus check is most prone to, and
    /// the one that is invisible from a passing suite.
    /// </summary>
    [Test]
    public void AnAlteredRequestIsRejected()
    {

        var trace = SessionTrace.ReadFrom(TracePath("iso2-ac-eim"));

        // Flip one bit in the second request's EXI payload — the first is the SAP handshake, and the
        // point is to prove a *session* message is compared, not merely the opening frame.
        var tampered = trace.Exchanges[1].Request.Bytes;
        tampered[^1] ^= 0x01;

        var replay = new TraceReplayStream(trace);
        replay.Write(trace.Exchanges[0].Request.Bytes, 0, trace.Exchanges[0].Request.Bytes.Length);

        var mismatch = Assert.Throws<TraceMismatch>(() => replay.Write(tampered, 0, tampered.Length));

        Assert.Multiple(() =>
        {
            Assert.That(mismatch!.Message, Does.Contain("exchange 1"));
            Assert.That(mismatch.Message, Does.Contain("first difference at"));
        });

    }


    /// <summary>
    /// The signature-aware comparison is a check and not a hole.
    /// </summary>
    /// <remarks>
    /// The mechanism substitutes the recorded signature before comparing, which is exactly the shape
    /// of a comparison that accidentally accepts everything. So both halves are exercised against a
    /// real recorded frame: a body altered under a valid signature must fail the byte comparison, and
    /// a signature altered under a valid body must fail verification. If either passed, signed
    /// exchanges would be riding along unchecked while the suite stayed green.
    /// </remarks>
    [Test]
    public void TheSignatureAwareComparisonActuallyBites(
        [Values("iso2-ac-pnc", "iso20-dc-pnc")] string name)
    {

        var trace  = SessionTrace.ReadFrom(TracePath(name));
        var signed = trace.Exchanges.First(e => e.Request.IsSigned);

        var key = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = Convert.FromHexString(trace.SigningKey!.X),
                Y = Convert.FromHexString(trace.SigningKey.Y),
            },
        });

        using (key)
        {
            Assert.Multiple(() =>
            {
                // The recorded frame itself: matches, and its signature verifies.
                Assert.That(SignedFrame.WithSignatureValue(signed.Request.Bytes, signed.Request.SignatureBytes!),
                            Is.EqualTo(signed.Request.Bytes),
                            "substituting a frame's own signature must be a no-op");
                Assert.That(SignedFrame.VerifiesWith(signed.Request.Bytes, key), Is.True,
                            "the recorded signature must verify against the recorded key");

                // A different key does not.
                using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                Assert.That(SignedFrame.VerifiesWith(signed.Request.Bytes, otherKey), Is.False,
                            "any key must not do — that would make the verification decorative");

                // A tampered signature survives the byte comparison (it is substituted away) and must
                // be caught by the verification. This is the case the whole design turns on.
                var tamperedSignature = signed.Request.SignatureBytes!;
                tamperedSignature[^1] ^= 0x01;
                var reSigned = SignedFrame.WithSignatureValue(signed.Request.Bytes, tamperedSignature);

                Assert.That(SignedFrame.WithSignatureValue(reSigned, signed.Request.SignatureBytes!),
                            Is.EqualTo(signed.Request.Bytes),
                            "the byte comparison cannot see a changed signature — that is the point");
                Assert.That(SignedFrame.VerifiesWith(reSigned, key), Is.False,
                            "…so the verification has to, or a wrong signature would pass both checks");
            });
        }

    }


    /// <summary>A session that ends early is the other way a replay can pass without meaning anything:
    /// nothing diverges, because nothing more is sent.</summary>
    [Test]
    public void AnEarlyEndingSessionIsNotComplete()
    {

        var trace  = SessionTrace.ReadFrom(TracePath("iso2-ac-eim"));
        var replay = new TraceReplayStream(trace);

        var sap = trace.Exchanges[0].Request.Bytes;
        replay.Write(sap, 0, sap.Length);

        Assert.That(replay.Complete, Is.False);
        Assert.That(replay.Replayed, Is.EqualTo(1));

    }

}
