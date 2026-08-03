using System.Net;
using System.Security.Cryptography;

using NUnit.Framework;

using Vanaheimr.V2G.Iso15118_2.Generated;
using Vanaheimr.V2G.Simulation.Metering;
using Vanaheimr.V2G.Simulation.Sap;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso2;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using Vanaheimr.V2G.Simulation.Tests.Timing;
using Vanaheimr.V2G.Simulation.Transport;
using Vanaheimr.V2G.Tp;

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
        bool                                              Signed  = false,
        bool                                              Metered = false);


    /// <summary>
    /// The station meter the metered scenarios fit, keyed off a fixed private key so the corpus
    /// records one meter identity rather than a fresh one per regeneration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fixing the key does <b>not</b> make the recording reproducible — ECDSA still picks its nonce
    /// at random, so every reading is 64 fresh bytes. What it fixes is <c>meterKey</c>, which would
    /// otherwise change on every regeneration and make the diff of a re-record useless. The same
    /// distinction <c>MeterVectorTests</c> had to correct in its own summary.
    /// </para>
    /// <para>
    /// Test material. It is checked into a public repository, which is exactly why it must never be
    /// a key any real meter uses.
    /// </para>
    /// </remarks>
    private const string MeterKeyD = "4a1f0f0b1d5f7c3e9a2b8c6d4e0f1a3b5c7d9e0f2a4b6c8d0e1f3a5b7c9d0e1f";

    private static ECDsa MeterKeyPair()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        key.ImportParameters(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D     = Convert.FromHexString(MeterKeyD),
        });
        return key;
    }

    private static SigningMeter Meter(TimeProvider clock)
    {
        // A reading of zero would verify perfectly and say nothing about whether the number in the
        // signature is the number on the wire — every field would be its default.
        var meter = new SigningMeter("VAN*M*4711", MeterKeyPair(), clock);
        meter.Add(4_200);
        return meter;
    }


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

        // ── Dynamic control mode ──────────────────────────────────────────
        //
        // Recorded the day the EVCC learned to drive Dynamic (2026-08-03), and for the ports rather
        // than for C#: the Kotlin and Swift EVCCs are validated against this corpus, so a mode with
        // no recorded session is a mode the ports can claim without ever being checked — the same
        // blind spot the roadmap names, one layer along. The mode touches four places (parameter
        // set, ScheduleExchange, EVPowerProfile, charge loop), and every one of them is in these
        // bytes.

        new("iso20-dc-eim-dynamic", "iso15118-20", "dc",
            "-20 DC, EIM, Dynamic control mode: the EV states energy needs and a departure time " +
            "instead of picking a schedule tuple — ControlMode = 2 in ServiceSelection, the Dynamic " +
            "arms in ScheduleExchange, the EVPowerProfile and the charge loop, the station " +
            "answering in kind ([V2G20-1600]).",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                await new Evcc20Dc(stream, clock, new ImmediateAsyncDelay(),
                                   LoopbackTimeouts.PerMessage) { PreferDynamicControlMode = true }.RunAsync(ct);
            },
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                await new Secc20Dc(SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge }.RunAsync(stream, ct);
            }),

        new("iso20-ac-eim-dynamic", "iso15118-20", "ac",
            "-20 AC, EIM, Dynamic control mode: as the DC one, on the AC message set — the arm " +
            "Evcc20Ac carries and the DC trace cannot reach.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Ac);
                await new Evcc20Ac(stream, clock, new ImmediateAsyncDelay(),
                                   LoopbackTimeouts.PerMessage) { PreferDynamicControlMode = true }.RunAsync(ct);
            },
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Ac);
                await new Secc20Ac(SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge }.RunAsync(stream, ct);
            }),

        // ── the multi-protocol offer ──────────────────────────────────────
        //
        // Both protocols in ONE SupportedAppProtocol request, the state machine chosen after the
        // handshake — the case a multiplexing station exists for. Two traces because the two answers
        // differ in the one byte that decides everything: the station's SchemaID names which entry
        // won, and the ports must run the machine it names rather than the one they would prefer.

        new("iso2-ac-eim-sapboth", "iso15118-2", "ac",
            "AC, EIM, negotiated: the EV offers -20 AC at priority 1 and -2 at priority 2 in one " +
            "handshake; this station speaks only -2, answers SchemaID 2 — the priority-2 entry — " +
            "and the -2 session runs. Also the first recording in which the SECC's answered " +
            "SchemaID is not 1, which is what shows it echoes the accepted entry rather than a literal.",
            async (stream, clock, ct) =>
            {
                var accepted = await SapHandshake.RunEvccSideAsync(stream,
                    new SapOffer[] { new(ProtocolVariant.Iso15118_20, PowerMode.Ac),
                                     new(ProtocolVariant.Iso15118_2,  PowerMode.Ac) }, ct);
                if (accepted.Protocol != ProtocolVariant.Iso15118_2)
                    throw new InvalidOperationException("this station is -2-only; the negotiation must settle on -2");
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

        new("iso20-dc-eim-sapboth", "iso15118-20", "dc",
            "-20 DC, EIM, negotiated: the same two-entry offer at a -20 station, which answers " +
            "SchemaID 1 — the priority-1 entry — and the -20 session runs.",
            async (stream, clock, ct) =>
            {
                var accepted = await SapHandshake.RunEvccSideAsync(stream,
                    new SapOffer[] { new(ProtocolVariant.Iso15118_20, PowerMode.Dc),
                                     new(ProtocolVariant.Iso15118_2,  PowerMode.Dc) }, ct);
                if (accepted.Protocol != ProtocolVariant.Iso15118_20)
                    throw new InvalidOperationException("this station speaks -20; the negotiation must settle on -20");
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

        // ── metered sessions ──────────────────────────────────────────────
        //
        // A station whose meter signs what it measured. The field is standard and almost never
        // populated — that is the whole reason to populate it (docs/CONCEPT.md §4.3) — and until
        // these two were recorded no session in the corpus carried one, so nothing downstream of the
        // recorder had ever seen a signed reading: not the bridge, not the app, not the ports.
        //
        // EIM, deliberately. A meter reports every cycle regardless of how the driver authorized, so
        // EIM is both the simpler case and the commoner one; and it keeps the meter's signature out
        // of any *signed* request, where it would land inside the fragment the EV digests and make
        // two substitutions fight over one digest. That case is real and is written down as open
        // rather than quietly avoided — see TheMeterSignatureIsNotInsideASignedFragment.
        //
        // Both protocols, because the protocol byte in the signed payload is the one thing keeping a
        // -20 reading from being presented as a -2 one, and it never appears on the wire: only a
        // recorded session can show the two are actually signed differently.

        new("iso2-ac-eim-meter", "iso15118-2", "ac",
            "AC, EIM, with a signing meter fitted: every ChargingStatusRes carries a MeterInfo whose " +
            "SigMeterReading is a real P-256 signature over the station's own reading, bound to this " +
            "session. The first recorded session a vehicle could verify a station's meter from.",
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
                            FixedGenChallenge = RecordedGenChallenge,
                            InstalledMeter    = Meter(clock) }.RunAsync(stream, ct);
            },
            Metered: true),

        new("iso20-dc-eim-meter", "iso15118-20", "dc",
            "-20 DC, EIM, with the same meter fitted: the reading rides in MeterInfo.MeterSignature " +
            "instead of SigMeterReading, over the same payload layout with the protocol byte set to " +
            "20 — so this trace and the -2 one differ in a byte that is never transmitted.",
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
                            FixedGenChallenge = RecordedGenChallenge,
                            InstalledMeter    = Meter(clock) }.RunAsync(stream, ct);
            },
            Metered: true),

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
    /// Creates the fixed contract identity the signed scenarios use. Separate from the corpus
    /// regenerator and even more deliberate: its bytes are an *input* to every recorded PnC session,
    /// so running this invalidates those traces and every port checked against them. Run it, then run
    /// <see cref="RegenerateTheCorpus"/>.
    /// </summary>
    [Test, Explicit("Regenerates Vectors/Session.pnc-material.json — invalidates every signed trace")]
    public void RegenerateThePncMaterial()
    {
        TestContext.Out.WriteLine($"wrote {PncMaterial.Regenerate()}");
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
            signingKey = PublicHalfOf(publicKey);
        }

        TraceSigningKey? meterKey = null;
        if (scenario.Metered)
        {
            using var key = MeterKeyPair();
            meterKey = PublicHalfOf(key);
        }

        return SessionTrace.Build(scenario.Name, scenario.Protocol, scenario.Mode, scenario.Note,
                                  recorder.Sent, recorder.Received, signingKey, meterKey);

    }


    private static TraceSigningKey PublicHalfOf(ECDsa key)
    {
        var q = key.ExportParameters(includePrivateParameters: false).Q;
        return new TraceSigningKey(Convert.ToHexString(q.X!).ToLowerInvariant(),
                                   Convert.ToHexString(q.Y!).ToLowerInvariant());
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
    /// <remarks>
    /// <b>Both directions, since 2026-08-03.</b> This used to compare a signed session's *requests*
    /// only and leave its responses out of the comparison entirely — harmless while the EV was the
    /// sole signer, and a hole the moment a station's meter started signing too. A station's reading
    /// travels in a response, so the direction that was not being compared was exactly the one the
    /// next feature needed. Comparing both is also strictly stronger for the existing PnC traces,
    /// whose responses had never been checked at all.
    /// </remarks>
    [Test]
    public async Task RecordingTheCorpusAgainProducesTheSameBytes(
        [ValueSource(nameof(ScenarioNames))] string name)
    {

        var scenario = Scenarios.Single(s => s.Name == name);
        var recorded = await RecordAsync(scenario);
        var onDisk   = SessionTrace.ReadFrom(TracePath(name));

        Assert.Multiple(() =>
        {
            Assert.That(recorded.Protocol,   Is.EqualTo(onDisk.Protocol));
            Assert.That(recorded.Mode,       Is.EqualTo(onDisk.Mode));
            Assert.That(recorded.Note,       Is.EqualTo(onDisk.Note));
            Assert.That(recorded.SigningKey, Is.EqualTo(onDisk.SigningKey));
            Assert.That(recorded.MeterKey,   Is.EqualTo(onDisk.MeterKey),
                        "the meter identity changed — a fresh key would make every re-record's diff total");
        });

        Assert.That(recorded.Exchanges, Has.Count.EqualTo(onDisk.Exchanges.Count));

        // What the substitution threw away, so it can be required to be non-empty exactly when the
        // session has a random part — and empty otherwise, which is the strong statement an EIM
        // recording gets to make.
        var randomised = new List<string>();

        for (var i = 0; i < onDisk.Exchanges.Count; i++)
        {
            foreach (var (fresh, old, direction) in new[]
                     {
                         (recorded.Exchanges[i].Request,  onDisk.Exchanges[i].Request,  "request"),
                         (recorded.Exchanges[i].Response, onDisk.Exchanges[i].Response, "response"),
                     })
            {
                Assert.That(fresh.IsSigned, Is.EqualTo(old.IsSigned),
                            $"exchange {i} {direction}: one recording thinks this frame is signed and " +
                             "the other does not");
                Assert.That(fresh.CarriesMeterSignature, Is.EqualTo(old.CarriesMeterSignature),
                            $"exchange {i} {direction}: one recording found a meter reading here and " +
                             "the other did not");

                if (fresh.Signature      != old.Signature)      randomised.Add($"{i} {direction} signature");
                if (fresh.MeterSignature != old.MeterSignature) randomised.Add($"{i} {direction} meter");

                // Substitute the recorded random parts back in, then require everything — payload
                // type, message name, and every other byte — to be identical.
                var comparable = fresh with
                {
                    Frame          = Convert.ToHexString(Restore(fresh, old)).ToLowerInvariant(),
                    Signature      = old.Signature,
                    MeterSignature = old.MeterSignature,
                };

                Assert.That(comparable, Is.EqualTo(old),
                            $"exchange {i} {direction} ({old.Message}) differs — either something is no " +
                             "longer pinned, or the session really changed and the corpus needs " +
                             "regenerating on purpose");
            }
        }

        // The half the substitution discards. Two runs must produce *different* random parts, or the
        // signing is not randomised and the whole mechanism was unnecessary; and a session with no
        // signer at all must reproduce exactly, which is what makes a re-record's diff readable.
        if (scenario.Signed || scenario.Metered)
            Assert.That(randomised, Is.Not.Empty,
                        "two recordings came out byte-identical, so nothing here is actually " +
                        "randomised and the substitution is solving a problem that does not exist");
        else
            Assert.That(randomised, Is.Empty,
                        "an unsigned, unmetered session recorded a value that changes between runs: " +
                        $"{string.Join(", ", randomised)}");

    }


    /// <summary>Puts the checked-in random parts back into a freshly recorded frame.</summary>
    private static byte[] Restore(TraceFrame fresh, TraceFrame old)
    {
        var bytes = fresh.Bytes;

        // Order does not matter: the two live in different fields — one in the message header, one
        // in the body's MeterInfo — and each substitution decodes and re-encodes the whole frame.
        if (old.SignatureBytes      is { } signature) bytes = SignedFrame.WithSignatureValue(bytes, signature);
        if (old.MeterSignatureBytes is { } reading)   bytes = SignedFrame.WithMeterSignature(bytes, reading);

        return bytes;
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

            // And the same pairing for the meter. A trace named for a signing meter that recorded no
            // reading would be an ordinary session under a promising name — and the app, which reads
            // this corpus to show a station's signed reading, would have nothing to show and no way
            // to tell that apart from a station that simply has no meter.
            var isMetered = name.EndsWith("-meter");
            Assert.That(trace.Exchanges.Any(e => e.Response.CarriesMeterSignature), Is.EqualTo(isMetered),
                        isMetered ? "a metered trace whose responses carry no meter signature recorded "
                                  + "a station without a meter"
                                  : "a station with no meter fitted must sign no readings");
            Assert.That(trace.MeterKey is not null, Is.EqualTo(isMetered),
                        "the meter key and the meter signatures must appear together, or verification "
                      + "is skipped");
        });

    }


    /// <summary>
    /// Every recorded meter reading verifies, against the meter key the trace carries and the values
    /// that are actually on the wire.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The point of recording a signed reading is that somebody downstream can check it, so the
    /// corpus checks it here first. Everything the verification needs is taken out of the frame —
    /// meter id, reading, timestamp, and the session id from the message header — because that is
    /// what a vehicle has, and verifying against values the recorder happens to still hold in memory
    /// would prove nothing about what was transmitted.
    /// </para>
    /// <para>
    /// <b>What a green here does not mean.</b> The key travels in the same file as the session it
    /// authenticates, so this says the reading was not altered between the meter and the file and is
    /// bound to this session. It does not say the station is who it claims — for that the key has to
    /// arrive out of band, which is the pairing code's job (<c>docs/CONCEPT.md</c> §4.5).
    /// </para>
    /// </remarks>
    [Test]
    public void EveryRecordedMeterReadingVerifies(
        [Values("iso2-ac-eim-meter", "iso20-dc-eim-meter")] string name)
    {

        var trace   = SessionTrace.ReadFrom(TracePath(name));
        var metered = trace.Exchanges.Where(e => e.Response.CarriesMeterSignature).ToList();

        Assert.That(metered, Is.Not.Empty, "no meter reading in a trace recorded with a meter fitted");

        using var key = KeyOf(trace.MeterKey!);

        Assert.Multiple(() =>
        {
            foreach (var exchange in metered)
            {
                Assert.That(exchange.Response.MeterSignatureBytes, Has.Length.EqualTo(64),
                            $"exchange {exchange.Index}: the field holds 64 bytes — one raw P-256 r‖s pair");
                Assert.That(SignedFrame.MeterReadingVerifiesWith(exchange.Response.Bytes, key), Is.True,
                            $"exchange {exchange.Index} ({exchange.Response.Message}): the recorded " +
                             "reading does not verify against the trace's own meter key");
            }
        });

    }


    /// <summary>
    /// The meter verification is a check and not a decoration: another key does not do, and neither
    /// does the right key over an altered reading.
    /// </summary>
    /// <remarks>
    /// The second half is the one worth having. A signature that verifies regardless of the number
    /// beside it would be the most convincing possible way to display a wrong reading, and a
    /// verification that reads its inputs from the wrong place — the recorder's memory rather than
    /// the frame — would behave exactly like that.
    /// </remarks>
    [Test]
    public void TheMeterVerificationActuallyBites()
    {

        var trace    = SessionTrace.ReadFrom(TracePath("iso2-ac-eim-meter"));
        var exchange = trace.Exchanges.First(e => e.Response.CarriesMeterSignature);

        using var key      = KeyOf(trace.MeterKey!);
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.Multiple(() =>
        {
            Assert.That(SignedFrame.MeterReadingVerifiesWith(exchange.Response.Bytes, key), Is.True,
                        "the recorded reading must verify against the recorded key");

            Assert.That(SignedFrame.MeterReadingVerifiesWith(exchange.Response.Bytes, otherKey), Is.False,
                        "any key must not do — that would make the verification decorative");

            // A CPO shaving the reading between meter and vehicle: the bytes still decode, the
            // signature is untouched, and the number no longer matches what was signed.
            var shaved = ShaveTheReading(exchange.Response.Bytes);
            Assert.That(shaved, Is.Not.EqualTo(exchange.Response.Bytes), "the tampering did nothing");
            Assert.That(SignedFrame.MeterReadingVerifiesWith(shaved, key), Is.False,
                        "an altered reading verified — the verification is not reading the number " +
                        "from the frame it is checking");
        });

    }


    /// <summary>
    /// No frame carries a meter signature *and* a header signature at once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a limit written down as a running check rather than as a comment. In a Plug &amp; Charge
    /// session the EV echoes the station's <c>MeterInfo</c> back inside a <b>signed</b>
    /// <c>MeteringReceiptReq</c>, so the meter's 64 random bytes land inside the fragment the EV
    /// digests. Substituting the recorded meter signature would then leave the digest in
    /// <c>SignedInfo</c> describing a body that no longer exists, and the comparison would fail on
    /// the digest rather than on anything real.
    /// </para>
    /// <para>
    /// Solvable — verify each side's digest against its own body instead of substituting — and not
    /// solved here, because the metered scenarios are EIM and never reach that shape. If someone
    /// records a metered PnC session this fails immediately, which is the point: the alternative is
    /// a corpus that quietly compares nothing for exactly the session that motivated it.
    /// </para>
    /// </remarks>
    [Test]
    public void TheMeterSignatureIsNotInsideASignedFragment(
        [ValueSource(nameof(ScenarioNames))] string name)
    {

        var trace = SessionTrace.ReadFrom(TracePath(name));

        foreach (var exchange in trace.Exchanges)
            foreach (var frame in new[] { exchange.Request, exchange.Response })
                Assert.That(frame.IsSigned && frame.CarriesMeterSignature, Is.False,
                            $"exchange {exchange.Index} ({frame.Message}) carries both a header " +
                             "signature and a meter signature. The substitution cannot handle that " +
                             "yet: the meter's bytes are inside the fragment the header signature " +
                             "digests, so replacing them invalidates the digest.");

    }


    private static ECDsa KeyOf(TraceSigningKey key) =>
        ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = Convert.FromHexString(key.X),
                Y = Convert.FromHexString(key.Y),
            },
        });


    /// <summary>Re-encodes a -2 charge-loop response with 100 Wh taken off its meter reading, leaving
    /// the signature and everything else exactly as recorded.</summary>
    private static byte[] ShaveTheReading(byte[] frame)
    {

        if (!V2GTPDispatcher.TryDecode(frame, out var set, out var message, out var error))
            throw new InvalidDataException($"expected a -2 ChargingStatusRes: {error}");

        if (message is not V2G_Message m || m.Body.BodyElement is not ChargingStatusResType status)
            throw new InvalidDataException($"expected a -2 ChargingStatusRes, got {message?.GetType().Name}");

        var shaved = m with
        {
            Body = m.Body with
            {
                BodyElement = status with
                {
                    MeterInfo = status.MeterInfo! with
                    {
                        MeterReading = status.MeterInfo!.MeterReading!.Value - 100,
                    },
                },
            },
        };

        var payload = new byte[8192];
        if (!Iso2Codec.TryEncode(shaved, payload, out var n))
            throw new InvalidOperationException("re-encoding the shaved response failed");

        var result = new byte[V2GTP.HeaderSize + n];
        if (!V2GTPDispatcher.TryEncode(set, payload.AsSpan(0, n), result, out var written))
            throw new InvalidOperationException("re-framing the shaved response failed");

        return result.AsSpan(0, written).ToArray();

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
