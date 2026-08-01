# Interop run artifacts

One directory per successful (or informative) interop run, named
`<yyyy-mm-dd>-<scenario>/` (e.g. `2026-08-01-iso2-ac-eim-notls/`; prefix the counterparty when it is
not Josev, e.g. `2026-08-01-tux-iso2-dc-notls/`). See
[`tools/interop-josev/README.md`](../../tools/interop-josev/README.md) and
[`tools/interop-tux-evse/README.md`](../../tools/interop-tux-evse/README.md) for how to produce them.

Each directory should contain:

- `notes.md` — the counterparty's commit or image tag, our commit, the exact commands, the outcome,
  and any wire discrepancies found.
- `frames.log` — the full frame log for the session: per frame, hex + decoded message + timestamp
  + direction (EVCC→SECC / SECC→EVCC).

## Producing them

The `[Explicit] [Category("Interop")]` fixtures write these artifacts themselves when
`V2G_INTEROP_RECORD=<dir>` is set — see
[`Vanaheimr.V2G.Simulation.Tests/Interop/InteropRecording.cs`](../../Vanaheimr.V2G.Simulation.Tests/Interop/InteropRecording.cs).
Per run: the raw octets of each direction, a `frames.log`, and a `*.trace.json` in the same format as
`Vectors/Session.*.trace.json` when the session was strictly alternating and untruncated. When it was
not, a `trace-not-built.txt` says why and the bytes are there anyway — the failed run is the
interesting one, and it is precisely the one a strict corpus builder refuses.

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
