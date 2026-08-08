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
| **Pause / Resume, -20** | EVerest | ~~▢~~ **run 2026-08-08 — and it is ours that failed.** Their station resumed on the first attempt (`OK_OldSessionJoined`, over mutual TLS with their minted vehicle credential); our EVCC then re-sent `AuthorizationSetupReq` and got `FAILED_SequenceError`, because a resumed `-20` session skips authorization and opens at `{AC,DC}_ChargeParameterDiscovery`. Moved to *our stack*, below. `-2` is `—` in the matrix, not `▢`. |
| **Signed tariffs, -20** | EVerest | ▢ |
| **Renegotiation, -2 and -20** | EVerest | ▢ both protocols. |
| **Plug & Charge, -20** | eVDriveFlow | ▢ — but first establish whether they do contract certificates at all; their documentation does not mention them. |

## Ours to fix

Both halves of our ISO 15118-20 pause/resume were built by analogy to `-2`, and the code comment in
`Secc20Base.SessionSetup` names the assumption out loud: *"same OldSessionJoined mechanic as -2"*.
**That assumption is wrong in both directions** — `-20` added an obligation `-2` does not have, and
dropped a behaviour `-2` requires. Settled against the requirement text on 2026-08-08; see
[`normative-basis.md`](normative-basis.md) for the clauses and for what may be cited from where.

- **Our EVCC cannot resume an ISO 15118-20 session.** After `OK_OldSessionJoined` it replays its full
  opening sequence, including `AuthorizationSetupReq`; EVerest's station has already moved to
  `{AC,DC}_ChargeParameterDiscovery` by then and answers `FAILED_SequenceError`. Demonstrated live on
  2026-08-08 ([run notes](interop-runs/2026-08-08-everest-pause-resume-tls/notes.md)) and **required by
  `[V2G20-1032]`** — the allowed next request after a resumed `SessionSetupRes` is
  `ChargeParameterDiscoveryReq`, per `[V2G20-1843]` with `[V2G20-2097]`/`[V2G20-2098]`/`[V2G20-5046]`.
  Service discovery, detail and selection are skipped along with authorization; only one next message is
  allowed and none of them is it. App-side work in `Evcc20Base`, and it needs a loopback regression test
  — **our own SECC accepts the wrong sequence**, so the existing E2E cannot catch it.
- **Our SECC accepts an ISO 15118-20 resume from any EVCC.** `Secc20Base.SessionSetup` rejoins on the
  session ID alone. `[V2G20-2545]` makes the check that the resume came from the same EVCC a **shall**,
  leaving only the *method* to the operator, and the standard's own notes spell out the consequence of
  omitting it: a second EV that reuses another's SessionID inherits that EV's authorization, PnC or EIM
  alike. So anyone who learns a paused session ID can claim it, and ours will hand it over. EVerest's
  `SHA-512(SessionID ‖ SHA-512(vehicle cert))` is the standard's own worked example (8.3.4.1.4.3,
  *should*-level) — one valid method, not the required one; a TLS-less deployment needs some other.
  Two further pieces are missing with it: the EVCC-side mirror check `[V2G20-2539]`, and the purge on a
  failed resume (`[V2G20-2613]`/`[V2G20-2614]`, `[V2G20-2615]`–`[V2G20-2617]`).
- **Minor, in a `✅` cell:** on an ISO 15118-2 resume, `[V2G2-743]` requires `EAmount` to be reduced by
  the energy already delivered. Our `-2` EVCC sends a constant 22 kWh
  (`Iso2/Evcc2.cs:536`). `DepartureTime` is omitted entirely, which makes `[V2G2-742]` vacuous rather
  than violated. Carries the `-2` document caveat in [`normative-basis.md`](normative-basis.md) — the
  text to hand is the 2022 DIS revision, while our stack targets ISO 15118-2:2014.

## Open questions about our own stack

Not gaps in coverage — things a counterparty's behaviour raised about us, which are not settled.

**Currently none**, which is worth writing down rather than leaving as an empty heading. The last entry
was the ISO 15118-20 vehicle-certificate binding, open from the 2026-08-08 EVerest run until the
requirement text settled it the same day; it moved up to *Ours to fix* — the check is required, the
method is not. Everything a counterparty's behaviour has raised about us is now either fixed, or a known
defect with an owner.

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
- **A candidate seventeenth filing, unwritten:** counterparty `iso-20` certificate scripts that emit
  secp256r1 material. The `-20` profile is secp521r1/ECDSA with Ed448 alongside (`[V2G20-2674]`,
  `[V2G20-2319]`), and its TLS tables admit only `secp521r1`/`x448` as named groups and
  `TLS_AES_256_GCM_SHA384`/`TLS_CHACHA20_POLY1305_SHA256` as suites — not TLS 1.3's own
  mandatory-to-implement `TLS_AES_128_GCM_SHA256`. This converts the *"not settled"* note in the
  [2026-08-06 TLS run](interop-runs/2026-08-06-everest-iso20-tls13-windows/notes.md) into a finding about
  somebody else's test PKI rather than a worry that we are stricter than the field. **Confirm what their
  script actually emits before writing anything** — the run note records only that it is not secp521r1.
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
