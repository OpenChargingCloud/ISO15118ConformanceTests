# Interop run artifacts

One directory per successful (or informative) interop run, named
`<yyyy-mm-dd>-<scenario>/` (e.g. `2026-08-01-iso2-ac-eim-notls/`). See
[`tools/interop-josev/README.md`](../../tools/interop-josev/README.md) for how to produce them.

Each directory should contain:

- `notes.md` — the Josev fork + commit, our commit, the exact commands, the outcome, and any
  wire discrepancies found.
- `frames.log` — the full frame log for the session: per frame, hex + decoded message + timestamp
  + direction (EVCC→SECC / SECC→EVCC).

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
