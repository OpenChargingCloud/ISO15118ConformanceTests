# EVerest's next EV — `Ev15118`, before it merges

**Matrix cells:** the *Their EV* row of the EVerest column · SECC · ISO 15118-2 · DC, EIM · EVerest

Back to the [interop matrix](../../README.md).

---

**EVerest is replacing `PyEvJosev`.** [everest-core#3031](https://github.com/EVerest/everest-core/pull/3031)
adds an EV side to `libiso15118` and a module around it, `Ev15118`, in every config; #3034 drops Josev
from the build. Unmerged as of 2026-11-06, so every released tree — 2026.02.1, 2026.02.2, `main` — still
runs `PyEvJosev`, and the cells beside this one still describe that module. Run against the PR head
`35d3eda52` on 2026-11-06, seven scenarios against our unchanged station, plus a diagnostic arm and a
`PyEvJosev` control ([run](../interop-runs/2026-11-06-everest-ev15118/notes.md)):

- **Six complete under our conformant timers** — `-20` AC and DC, Scheduled and Dynamic, mutual TLS
  1.3, and `-2` AC plain and over TLS 1.2.
- **The ≈532 ms charge-loop turnaround is gone.** `Ev15118` paces every `-20` request at a deliberate
  25 ms (`DEFAULT_SEND_DELAY`) and every `-2` one at 100 ms (`MIN_REQUEST_INTERVAL`); measured on the
  wire, 25,8 ms and 100,7 ms. A same-day control re-ran `PyEvJosev` under the same capture: 535 ms, the
  old number. So [`josev-iso20-evcc-charge-loop-pacing`](../reports/josev-iso20-evcc-charge-loop-pacing.md)
  stays true of every released tree and has nothing left to say about `main` once #3031 merges.
- **`-2` DC stops after `CableCheck`, 2 of 2 — on our omission.** Our station never sends the optional
  `EVSEIsolationStatus`; their EV refuses an absent one on purpose. With our station patched to report
  `Valid`, the same EV charges to `SessionStop`, 2 of 2. ISO 15118-2 makes the element optional and
  defers its handling to IEC 61851-23, which is not available here — so not a defect of either side, and
  the decision about our station is in [`open-work`](../open-work.md).
- **The codec stops flipping with the direction.** `Ev15118` is `libiso15118` over **cbV2G**, the codec of
  their stations — no longer EXIficient via Josev — and its state machine is EVerest's own. The reason
  [`josev-is-everests-ev`](josev-is-everests-ev.md) gives for running `←SECC` against EVerest sparingly
  ends with this PR.
