# What is open

The interop matrix in [`README.md`](../README.md) says what is **proven**. This says what is not, why,
and who it is waiting on. It is the inverse of the same table and nothing else — every entry here is a
matrix cell that is not `✅`.

## How to use it, and the mistake it exists to prevent

**Do not build a to-do list out of the `## Next` sections in `docs/interop-runs/`.** Those are
snapshots taken at the end of a run. A later run closes one without touching the earlier note, so a
list assembled from them is a list of what *was* open, ordered by when it was written down — which
reads exactly like a list of what is open now.

On 2026-08-08 that produced a survey claiming mutual TLS 1.3 against eVDriveFlow was the obvious next
thing to do. It had been done on 2026-08-07 and is `✅` in the matrix. Half the other "gaps" in that
survey were the same mistake: EVerest over TLS 1.3, IsoMux over TLS, the tux-evse reverse direction,
AC against a live station, MCS — all closed on 08-05 to 08-07, all still listed as open in a `Next`
section written before them.

So: **the matrix is the state, the run notes are the history, and this file is the derived to-do.**
When a cell turns `✅`, delete its entry here. If this file disagrees with the matrix, the matrix wins
and this file is stale.

## Blocked on the counterparty

Nothing here can be moved by work on our side. Each has a filed or drafted report.

| | Counterparty | State | Waiting on |
|---|---|---|---|
| **Pause / Resume, -20** | Josev | ⛔ `EV→` | Their `-20` `SessionSetup` compares the resumed session ID against the *live* connection instead of the preserved context, which its `-20` states never fill — so `OK_OldSessionJoined` is unreachable. Six-line fix, mirroring their own working `-2` branch. Filed: [`josev-iso20-pause-resume.md`](reports/josev-iso20-pause-resume.md). |
| **DC Scheduled / Dynamic, -20** | eVDriveFlow | ◐ | `hasattr` used as a presence test on an `Optional[int]`, so our legally omitted `TargetSOC` overwrites theirs with `None`. Filed: [`evdriveflow-headless-session.md`](reports/evdriveflow-headless-session.md). |
| **AC, -20** | EVerest | ◐ | Their SIL's own-EV contactor coupling; the session reaches `ScheduleExchange` and stops there. |
| **AC_BPT** | EVerest | ◐ | Negotiated, then the same contactor wall. |
| **TLS 1.2 unilateral, -2** | tux-evse | ⛔ pinned | Their configs offer neither suite ISO 15118-2 prescribes. Filed: [`tux-evse-tls.md`](reports/tux-evse-tls.md). |
| **CertificateInstallation, -20** | Josev | ◐ | Our signed response is verified; their implementation then ends at its own `NotImplementedError`. |

## Never verified by anything but us

The matrix marks these `◐` because a peer *consumed* what we sent. Nothing checked it. That is a
weaker claim than it looks and worth separating from "untested".

- **Signed tariffs, -20** (Josev) — their AC EVCC consumed our signed `AbsolutePriceSchedule`; nothing
  external verifies the signature.
- **Renegotiation, -20** (Josev) — their EV sends a real `SessionStopReq(ServiceRenegotiation)`
  [V2G20-1477] and then drops the link anyway.
- **Plug & Charge, -2** (EVerest) — chain accepted and our signature verified, but their SIL has no
  contract-validating backend, so nothing decides whether the contract is *good*.

## Untested, and nothing is stopping us

The honest backlog. No counterparty defect in the way, no missing capability on our side that is known.

| | Counterparty | Note |
|---|---|---|
| **Pause / Resume, -20** | EVerest | ▢ — and now specified. Their resume needs **mutual TLS**: it matches `SHA-512(session_id ‖ vehicle_cert_hash)` and takes the hash from the verified TLS peer certificate, so `ConnectionPlain` can never reach the branch (`everest-core` @ `b61bb12`; see the [EVerest page](everest-cross-validation.md)). The run is `config-sil-dc-tls.yaml`, our EVCC with a client certificate, `--pause` then `--resume <hex>` **re-presenting the same certificate** — a plain-TCP attempt is guaranteed to answer `OK_NewSessionEstablished` and would prove nothing. `-2` is `—` in the matrix, not `▢`. |
| **Signed tariffs, -20** | EVerest | ▢ |
| **Renegotiation, -2 and -20** | EVerest | ▢ both protocols. |
| **Plug & Charge, -20** | eVDriveFlow | ▢ — but first establish whether they do contract certificates at all; their documentation does not mention them. |

## Open questions about our own stack

Not gaps in coverage — things a counterparty's behaviour raised about us, which are not settled.

- **Does an ISO 15118-20 resume have to be bound to the vehicle certificate?** EVerest binds it:
  `SHA-512(session_id ‖ vehicle_cert_hash)`, computed from the verified TLS peer certificate, and a
  mismatch silently starts a new session. **Our SECC binds nothing** — `Secc20Base.SessionSetup`
  rejoins on the session ID alone, and the code comment says so: *"same OldSessionJoined mechanic as
  -2"*. If the binding is required, ours accepts a resume it should refuse, and anyone who learns a
  session ID can claim a paused session. If it is not required, EVerest is stricter than the standard
  and interop suffers in the other direction.
  **This is not resolvable from what is in this repository:** the `-20` standard text is not here (only
  ISO's schemas, which carry no requirement prose), and neither implementation is evidence about the
  other. It needs somebody with the document. Recorded rather than guessed.

## Structural — will not close without someone else building something

- **tux-evse, everything -20.** Their stack speaks ISO 15118-2 and DIN 70121. The whole `-20` column
  is `—` and stays that way.
- **WPT and ACDP session state machines.** No independent stack implements them, so `▢ codec only` is
  the ceiling. What *did* change on 2026-08-08: the bytes are now judged by EXIficient rather than only
  by the generator that produced them, which is the strongest form available without a second stack.
- **MCS and MCS_BPT beyond EVerest.** Only one counterparty implements them at all.
- **Multi-protocol SAP offer beyond EVerest.** Same.

## Not in the matrix at all

- **Sixteen filings across six projects** are drafted and unsent in [`reports/`](reports/README.md).
  Each ends with a *Before sending* checklist whose unticked items are the parts only a person can do.
  This is the largest single block of finished work waiting on a human.
- **A methodological item, from the EVerest MQTT run:** *"Run every future session twice, in every
  harness. One session is not a test of a station."* Not systematically applied.
- **Kotlin and Swift parity** — Dynamic control mode and energy-transfer-mode selection exist in the C#
  EVCC and not in the ports. That is app-side work, in `libs/EVSimulatorApp`, not here.

## Where the codec stands, for contrast

Closed, and worth stating because it is the part that used to hold the open questions:

| | `-2` | `-20` |
|---|---:|---:|
| byte-exact through EXIficient | 183 of 186 | 345 of 353 |
| length differences, all measured | 2 | 8 |
| unreadable by an independent codec | 0 | 0 |
| unexplained | 1 — and it is [V2Gdecoder's](reports/v2gdecoder-fuzzy-grammar.md), not ours | **0** |
