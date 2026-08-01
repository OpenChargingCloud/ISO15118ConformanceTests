# Interop run artifacts

One directory per successful (or informative) interop run, named
`<yyyy-mm-dd>-<scenario>/` (e.g. `2026-08-01-iso2-ac-eim-notls/`; prefix the counterparty when it is
not Josev, e.g. `2026-08-01-tux-iso2-dc-notls/`, `2026-08-01-edf-iso20-dc-dynamic/`). See
[`tools/interop-josev/`](../../tools/interop-josev/README.md),
[`tools/interop-tux-evse/`](../../tools/interop-tux-evse/README.md) and
[`tools/interop-evdriveflow/`](../../tools/interop-evdriveflow/README.md) for how to produce them.

Each directory should contain:

- `notes.md` — the counterparty's commit or image tag, our commit, the exact commands, the outcome,
  and any wire discrepancies found.
- `frames.log` — the full frame log for the session: per frame, hex + decoded message + timestamp
  + direction (EVCC→SECC / SECC→EVCC).

## Producing them

The `[Explicit] [Category("Interop")]` fixtures write these artifacts themselves when
`V2G_INTEROP_RECORD=<dir>` is set — see
[`Vanaheimr.V2G.Simulation.Tests/Interop/InteropRecording.cs`](../../Vanaheimr.V2G.Simulation.Tests/Interop/InteropRecording.cs).
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
  (eVDriveFlow is a state machine, not a replayer). Then the comparison answers "did the live run take
  the same route as ours" — not a conformance claim, and against a Dynamic-mode -20 peer it has every
  reason to say no. The divergence is the result.

A recorded `trace.json` can be adopted as a corpus entry, which is how a conformance fix earned in a
one-off run becomes something all four back ends are held to. That is a deliberate step, not
automatic: read it first (it carries a foreign station's identifiers and schedules).

## Record mode → vectors (the valuable part)

Frames captured from Josev are **independently generated** conformance vectors — Josev shares none of
our lineage (it uses EXIficient, not cbV2G). The adoption path into the regular suite:

1. Isolate a single message's EXI bytes from `frames.log`.
2. Decode it with our codec (`DecodeAny`) and confirm the decoded content is what the scenario expects.
3. If it round-trips (decode → re-encode) byte-for-byte, add it as a checked-in vector under
   `Vanaheimr.V2G.Exi.Tests/Vectors/` with `source: "josev@<sha>"`, so it guards against regressions
   from a second, independent oracle.
4. If it does *not* round-trip, that is a real interop finding — analyse the byte diff, fix the root
   cause (codec or generator), and add the captured bytes as a regression vector.

Nothing is checked in here until there is a real run to record.
