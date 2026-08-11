# Interop run artifacts

One directory per successful (or informative) interop run, named
`<yyyy-mm-dd>-<scenario>/` (e.g. `2026-08-01-iso2-ac-eim-notls/`; prefix the counterparty when it is
not Josev, e.g. `2026-08-01-tux-iso2-dc-notls/`, `2026-08-01-edf-iso20-dc-dynamic/`). See
[`tools/interop-josev/`](../../tools/interop-josev/README.md),
[`tools/interop-tux-evse/`](../../tools/interop-tux-evse/README.md),
[`tools/interop-evdriveflow/`](../../tools/interop-evdriveflow/README.md) and
[`tools/interop-everest/`](../../tools/interop-everest/README.md) for how to produce them.

Each directory should contain:

- `notes.md` — the counterparty's commit or image tag, our commit, the exact commands, the outcome,
  and any wire discrepancies found.
- `frames.log` — the full frame log for the session: per frame, hex + decoded message + timestamp
  + direction (EVCC→SECC / SECC→EVCC).

An **offline oracle run** — no session, no wire, just our recorded bytes put through somebody else's
codec — belongs here too and has no `frames.log`; it carries its verdict table instead. See
[`tools/interop-v2gdecoder/`](../../tools/interop-v2gdecoder/README.md) and the run it produced,
[`2026-08-07-v2gdecoder-oracle/`](2026-08-07-v2gdecoder-oracle/notes.md).

So does a **static sweep** — no session and no bytes either, just a counterparty's source held against
a rule. It carries the tool's output as its artifacts, and its `notes.md` says what the sweep could
*not* decide, since that is the whole risk with reading code instead of traffic. See
[`2026-08-11-libcbv2g-grammar-sweep/`](2026-08-11-libcbv2g-grammar-sweep/notes.md), and
[`2026-08-11-edf-pnc-source-audit/`](2026-08-11-edf-pnc-source-audit/notes.md) for the variant that
corroborates a source reading against frame logs **already** in this directory — the cheapest run there
is, and an argument for keeping the bytes rather than only the verdicts.

[`2026-08-11-everest-d20-rng-entropy/`](2026-08-11-everest-d20-rng-entropy/notes.md) is the strongest
form of that variant so far, and worth knowing about before you delete an old log: it measures a
counterparty's *random number generator* by recovering the 32-bit seed behind **49 SessionIDs their
station issued across twenty earlier runs**, none of which was recording them for that reason. The
artifacts kept for one question answered a different one nine days later —
[`2026-08-11-everest-iso2-metering-receipt/`](2026-08-11-everest-iso2-metering-receipt/notes.md) did it
again the same afternoon, out of a `frames.log` from 2026-08-02, and the reason it needed no rig is
that the finding is about a field that is **absent**: no live run can show you an omission you were not
already looking for, but a kept frame can be re-decoded by name.

## Producing them

The `[Explicit] [Category("Interop")]` fixtures write these artifacts themselves when
`V2G_INTEROP_RECORD=<dir>` is set — see
[`ISO15118ConformanceTests.Simulation/Interop/InteropRecording.cs`](../../ISO15118ConformanceTests.Simulation/Interop/InteropRecording.cs).
Per run: the raw octets of each direction, a `frames.log` (named messages and response codes, not just
payload types), a `flow.md`, and a `*.trace.json` in the same format as `Vectors/Session.*.trace.json`
when the session was strictly alternating and untruncated. When it was not, a `trace-not-built.txt`
says why and the bytes are there anyway — the failed run is the interesting one, and it is precisely
the one a strict corpus builder refuses.

`flow.md` is the one to paste into `notes.md`: the paired message sequence, every response code that
was not OK, and — when `V2G_INTEROP_SCENARIO` points at a reference — how the order compares with it.
That comparison is the part of interop a vector corpus can never do, and it is where §1.3's
conformance fixes live.

The reference can be either kind of file, told apart by structure:

- **a counterparty's scenario** (tux-evse), which is a real session captured and replayed — so the
  expected column is another car's route;
- **one of our own `Vectors/Session.*.trace.json`**, for a counterparty that publishes no such file
  (eVDriveFlow and EVerest are stacks, not replayers). Then the comparison answers "did the live run
  take the same route as ours" — not a conformance claim, and against a Dynamic-mode -20 peer it has
  every reason to say no. The divergence is the result.

Both directions are compared when the reference declares both. For a counterparty that is a
**station** — EVerest's `EvseV2G` is the likeliest thing to be on a real charger — the *station → EV*
section is the one that carries the news: what our car sends is ours and already pinned by the corpus,
while what their charger answers is the thing no test here has ever seen.

A recorded `trace.json` can be adopted as a corpus entry, which is how a conformance fix earned in a
one-off run becomes something all four back ends are held to. That is a deliberate step, not
automatic: read it first (it carries a foreign station's identifiers and schedules).

## Record mode → vectors (the valuable part)

Frames captured from Josev are **independently generated** conformance vectors — Josev shares none of
our lineage (it uses EXIficient, not cbV2G). The adoption path into the regular suite:

1. Isolate a single message's EXI bytes from `frames.log`.
2. Decode it with our codec (`DecodeAny`) and confirm the decoded content is what the scenario expects.
3. If it round-trips (decode → re-encode) byte-for-byte, add it as a checked-in vector under
   `WWCP_ISO15118_EXI_Tests/Vectors/` with `source: "josev@<sha>"`, so it guards against regressions
   from a second, independent oracle.
4. If it does *not* round-trip, that is a real interop finding — analyse the byte diff, fix the root
   cause (codec or generator), and add the captured bytes as a regression vector.

Nothing is checked in here until there is a real run to record.
