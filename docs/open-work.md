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

Nothing here can be moved by work on our side, **except** the two EVerest AC rows — those are ours to
unblock and are here because they read as counterparty walls and are not. Every other row has a filed
or drafted report.

| | Counterparty | State | Waiting on |
|---|---|---|---|
| **Pause / Resume, -20** | Josev | ⛔ `EV→` | Their `-20` `SessionSetup` compares the resumed session ID against the *live* connection instead of the preserved context, which its `-20` states never fill — so `OK_OldSessionJoined` is unreachable. Six-line fix, mirroring their own working `-2` branch. Filed: [`josev-iso20-pause-resume.md`](reports/josev-iso20-pause-resume.md). |
| **DC Scheduled / Dynamic, -20** | eVDriveFlow | ◐ | `hasattr` used as a presence test on an `Optional[int]`, so our legally omitted `TargetSOC` overwrites theirs with `None`. Filed: [`evdriveflow-headless-session.md`](reports/evdriveflow-headless-session.md). |
| **AC, -20** | EVerest | ◐ | Their SIL's own-EV contactor coupling — a property of driving their harness with a foreign EV, **not** a defect, and so nothing to file. Reading their source to explain it turned up one that is: [`everest-iso20-ac-contactor-latch.md`](reports/everest-iso20-ac-contactor-latch.md), on the same code path and not the cause of this wall. Moving this cell needs their EV-side hardware simulation driven, which is ours to build. |
| **AC_BPT** | EVerest | ◐ | Negotiated, then the same wall, for the same reason. |
| **TLS 1.2 unilateral, -2** | tux-evse | ⛔ pinned | Their configs offer neither suite ISO 15118-2 prescribes. Filed: [`tux-evse-tls.md`](reports/tux-evse-tls.md). |
| **CertificateInstallation, -20** | Josev · EVerest | ◐ | Both send a real signed request — SwitchEV's on 2026-07-22, EVerest's `PyEvJosev` on 2026-08-08 with its own OEM root. Our response is decoded and validated, and then each ends at the same `NotImplementedError`: the fork inherited the gap. Nothing to file that the upstream code does not already say out loud. |

## Never verified by anything but us

The matrix marks these `◐` because a peer *consumed* what we sent. Nothing checked it. That is a
weaker claim than it looks and worth separating from "untested".

- **Signed tariffs, -20** (Josev) — their AC EVCC consumed our signed `AbsolutePriceSchedule`; nothing
  external verifies the signature.
- **Renegotiation, -20** (Josev) — their EV sends a real `SessionStopReq(ServiceRenegotiation)`
  [V2G20-1477] and then drops the link anyway — their `SessionStop` state sets `next_state = ServiceDiscovery`
  without building the request that state needs, and their framework raises on it. Filed:
  [`josev-iso20-renegotiation.md`](reports/josev-iso20-renegotiation.md).
- **Plug & Charge, -2** (EVerest) — chain accepted and our signature verified, but their SIL has no
  contract-validating backend, so nothing decides whether the contract is *good*.

## Untested, and nothing is stopping us

The honest backlog. No counterparty defect in the way, no missing capability on our side that is known.

| | Counterparty | Note |
|---|---|---|
| **Pause / Resume, -20** | EVerest | ~~▢~~ **run 2026-08-08 — and it is ours that failed.** Their station resumed on the first attempt (`OK_OldSessionJoined`, over mutual TLS with their minted vehicle credential); our EVCC then re-sent `AuthorizationSetupReq` and got `FAILED_SequenceError`, because a resumed `-20` session skips authorization and opens at `{AC,DC}_ChargeParameterDiscovery`. Moved to *our stack*, below. `-2` is `—` in the matrix, not `▢`. |
| **Signed tariffs, -20** | EVerest | ▢ |
| **Renegotiation, -2 and -20** | EVerest | ~~▢ both~~ **-20 run 2026-08-10** — their `PyEvJosev` EV took our station's `ServiceRenegotiation` notification, stopped the charge, ran welding detection and sent `SessionStopReq(ServiceRenegotiation)`; our SECC answered `OK` and stayed open, and their EV closed the connection. Same defect as Josev's, now seen in **DC** and against the fork `26f7988` ([run](interop-runs/2026-08-10-everest-iso20-renegotiation-reverse/notes.md), [filing](reports/josev-iso20-renegotiation.md)). `-2` is still `▢`. |
| **Contract provisioning, -2** | EVerest · Josev | ▢ **new on 2026-08-11**, and the reason it is new is that our own `-2` stack could not ask until that morning (`WWCP_ISO15118` `c1a7989`: `CertificateInstallationReq` *and* `CertificateUpdateReq`, the service advertised as a `-2` VAS and selected by id). So **no counterparty's `-2` provisioning path has ever been exercised by this project.** One session against EVerest's `EvseV2G` would do two things at once: test their `CertificateInstallation` — which forwards the EXI to a backend over MQTT and is a real implementation — and settle [the `CertificateUpdate` filing](reports/everest-evsev2g-certificate-update.md), whose whole open question is which of two outcomes their stub produces on the wire. Needs `-2` PnC over TLS, which has run against them before. Josev implements neither and answers `FAILED` correctly, so there is nothing to measure there. |
| ~~**Plug & Charge, -20**~~ | ~~eVDriveFlow~~ | **Closed 2026-08-11 by answering the condition: they implement none.** Moved to *Structural*, below. |

## Ours to fix

- **Our `-20` station has one sequence timeout for every message type, and `-20` does not.**
  `Secc20Base(TimeSpan sequenceTimeout, …)` takes a single value and applies it in every phase, so it is
  the same shape as [the defect filed against EVerest on 2026-08-11](reports/everest-d20-sequence-timeout.md):
  Table 215 gives 60 s for *all other messages*, and Tables 216 and 217 — obliged by `[V2G20-1500]` and
  `[V2G20-1502]` — override it to **0,5 s** after `AC_ChargeLoopRes` and `DC_ChargeLoopRes`, the phase
  in which the contactor is closed.
  <br>Nobody has measured ours either, and now the instrument exists:
  `Evcc20Base.GoSilentInChargeLoop` was added to measure theirs and works just as well against a
  loopback. **The fix and its test are the same piece of work** — a per-message-type lookup at the one
  place the timer is armed, plus a loopback test that goes silent in the charge loop and asserts the
  station gives up inside a second.
  <br>**The `-2` half of that question is answered, and the answer is that there is nothing to do.**
  Table 108 was re-extracted page by page on 2026-08-11 — `pdftotext -layout` had flattened its five
  stacked parameter names into a single column, and read that way the message list looks like a
  per-message sequence timeout. It is not: that list belongs to `V2G_SECC_Msg_Performance_Time`
  (`CurrentDemandRes` 0,025 s — how fast the SECC must *answer*), and `V2G_SECC_Sequence_Timeout` sits
  in the `(all messages)` row at **60 s**, beside the same 40 / 60 / 55 that `-20`'s Table 215 carries.
  So the charge-loop override is an addition of the newer document; our `-2` station's flat timeout is
  correct, and so is EVerest's. Only the `-20` half is ours to fix.
  <br>Recorded rather than fixed on the day it was found, because the filing it came from was the
  turn's work; it is the next thing in this section rather than a someday item.

- ~~**Neither of our stations implements `[V2G2-460]` / `[V2G20-460]`.**~~ **The `-2` half is fixed
  2026-08-11**, stack branch `iso2-unknown-session`; **the `-20` half is still open** and the reason is
  below. Found while reading EVerest's `-2` station for the same rule: `FAILED_UnknownSession` appeared
  **nowhere** in our live code — only in the `old/` tree's enum — so a request whose header carried any
  SessionID at all was served as the session owner's. The requirement is one sentence and identical in
  both documents: any request except `SessionSetupReq` whose SessionID is not the stored one shall be
  answered `FAILED_UnknownSession`.
  <br>**Why no test of ours could have caught it**, which is the half worth keeping: our EVCC had no
  way to send a SessionID other than the one it was given, so a loopback session never put a wrong one
  in front of the station. Same shape as the MeterInfo gap on 2026-08-10 and the running-limit one
  before it — the third time this month that a question our car cannot ask hid an answer nobody checked.
  <br>**The car half** is `Evcc2.SendSessionId`, opt-in and defaulting to the real id, so every recorded
  session keeps its bytes. **The station half** answers with *the response that pairs with the request*
  — the same table `[V2G2-538]` already needed, split out of `SequenceError` into `Refuse(req, code)` —
  and **leaves the phase alone**, which is this station's documented `-2` policy for the whole `FAILED`
  family: nothing in `-2` obliges either side to end a session over a response code, so a car that
  echoes the right id next time charges. `Secc2.UnknownSessionAt` and `UnknownSessionRefusals` make it
  visible from a run, since a non-fatal refusal is otherwise invisible.
  <br>Four tests in
  [`Iso2UnknownSessionTests`](../ISO15118ConformanceTests.Simulation/StateMachines/Iso2UnknownSessionTests.cs);
  **three of the four fail** when the guard is neutered, which is how it was checked, and the fourth
  pins the unchanged default. Suite green at 1 374.
  <br>**It cost four test harnesses, and that is the interesting part.** `Secc2TariffTests`,
  `Secc2PnCTests`, `Secc2SignedMeterTests` and `Secc2TransitionTests` drive `Secc2.Handle` directly and
  built every header with `new byte[8]` — they had been modelling, for their whole existence, a car that
  sends the all-zero SessionID for the entire session. Exactly the peer the EVerest filing is about.
  They now read `secc.SessionId` back, as a real car does.
  <br>**`-20` is still open**, and not for want of trying: `Secc20Base` has no table of corresponding
  responses at all — its sequence guard *throws* rather than answering (`"would be
  ResponseCode.FAILED_SequenceError"`), so `[V2G20-460]` needs that builder first, across three
  generated message sets. One piece of work serving both requirements, and bigger than this one was.
  <br>**And there is a worked example to copy the shape from**, found on 2026-08-11 while reading
  their `-20` library for something else: EVerest's `d20/context_helper.cpp` is exactly that table —
  `handle_sequence_error<Response>(session)` templated over the response type, dispatched from one
  `send_sequence_error(req_type, ctx)` across **all sixteen** `-20` message types, with every
  `state/*.cpp` calling it from its `else` arm. So their `-20` answers an out-of-sequence request with
  the corresponding response, where ours raises `SessionAborted` and ends the session without
  answering at all — and the same table then serves `[V2G20-460]`. Ours is the gap here and theirs is
  the reference, which is worth saying in a file that mostly runs the other way. It is also the
  `-20` twin of the `-2` defect **tux-evse's VW capture found in us** on 2026-08-06 and that was
  fixed the same day; the `-2` half answers properly now and the `-20` half never did.
  <br>None of it blocked [the filing against EVerest](reports/everest-evsev2g-session-id-zero.md),
  since that probe is raw Python and owes our state machines nothing.
  [`…-iso2-session-id-zero`](interop-runs/2026-08-11-everest-iso2-session-id-zero/notes.md).

- ~~**Our ISO 15118-20 EVCC could not ask to be told the meter reading.**~~ **Fixed 2026-08-10**, stack
  branch `iso20-meter-info`. `Evcc20Dc` and `Evcc20Ac` both passed the literal `false` for
  `MeterInfoRequested`, so `[V2G20-1081]` — the one mechanism the standard gives the *car* for asking —
  was unreachable from here, and therefore **no run of this suite had ever tested any station's
  `[V2G20-1082]`**, the duty to answer.
  <br>`Evcc20Base.RequestMeterInfo` is the switch, opt-in and defaulting to `false` so every recorded
  session and every vector keeps the bytes it was recorded with — the shape of `Battery` and
  `TransportSecurity.Unknown`. `Evcc20Base.MeterInfoResponses` counts what came back, and
  `Secc20Base.MeterInfoRequestedByEv` records what was asked, because the request field is otherwise
  invisible from both ends of a loopback. `V2G_INTEROP_METER=1` reaches it from a run.
  <br>Four tests in
  [`Iso20MeterInfoTests`](../ISO15118ConformanceTests.Simulation/E2E/Iso20MeterInfoTests.cs); **two of
  the four fail** when the plumbing is put back to the literal `false`, which is how it was checked, and
  the fixture says which two and why the other two exist.
  <br>**It found something the same hour.** EVerest's `Evse15118D20` reads the field, forwards it as a
  feedback signal and never sets `MeterInfo` on the response — measured with a control in which our
  request changed by one bit and their responses did not change at all
  ([`…-d20-meter-info`](interop-runs/2026-08-10-everest-d20-meter-info/notes.md),
  [the twenty-ninth filing](reports/everest-d20-meter-info.md)).
  <br>**The pattern is the point, and it is the third time this month**: a gap in our own car hid a gap
  in somebody else's station. A suite cannot find a station ignoring a question its own EV never asks.

- ~~**Our `-2` DC charge loop is open-loop: it never reads the limits the station revises in every
  `CurrentDemandRes`.**~~ **Fixed 2026-08-10**, stack branch `iso2-running-limits`, both halves — and
  then **downgraded from a conformance defect to a behavioural one** the same evening, when the
  requirement side was finally read. Found from their `EvseManager` warning *"EV ignores new EVSE max
  limits. Setting target current to new EVSE max limits"* — 47 times across our recorded EVerest runs.
  Decoded from
  [`frames.log`](interop-runs/2026-08-10-everest-session-log-lengths/frames.log) of the full charge the
  same day:

  | | |
  |---|---|
  | their `ChargeParameterDiscoveryRes` | `EVSEMaximumCurrentLimit` **200.0 A**, `EVSEMaximumPowerLimit` 150 000 W, `EVSEMaximumVoltageLimit` 900.0 V |
  | our `CurrentDemandReq` ×3 | `EVTargetCurrent` **120 A** at `EVTargetVoltage` 400 V — 48 kW |
  | their `CurrentDemandRes` ×3 | `EVSEMaximumCurrentLimit` **55.2 A** |

  **The first suspicion was wrong and is worth recording as wrong**: we do *not* exceed what they
  advertised at `ChargeParameterDiscovery` — 120 A against 200 A, 48 kW against 150 kW, comfortably
  inside. What we ignore is the limit they **revise downward in the charge loop**, which -2 lets the
  SECC do in every `CurrentDemandRes`. They dropped it to 55.2 A and we went on asking for 120 A, three
  times out of three, so their station clamped and warned each time.
  <br>The cause is one method: `Evcc2.CurrentDemand()` builds `EVTargetCurrent` from the constant
  `DcRequestedAmps`, and nothing anywhere in the `-2` EVCC reads `EVSEMaximumCurrentLimit`,
  `EVSEMaximumPowerLimit` or `EVSECurrentLimitAchieved` off a `CurrentDemandRes`.
  <br>**Why no test of ours could have caught it**, which is the part worth keeping: our own SECC sent
  `EVSEMaximumCurrentLimit: null` in every `CurrentDemandRes`, so a loopback session never presented a
  running limit to read. The fix therefore had two halves.
  <br>**The car half.** `Evcc2` keeps the station's current and power ceiling — seeded from the
  `DC_EVSEChargeParameter` of the discovery response, then replaced by whatever each `CurrentDemandRes`
  carries — and `DcTargetAmps` holds the setpoint inside both. `EVMaximumPowerLimit` states the same
  operating point, so the two fields cannot contradict each other once a ceiling moves. Floor rather
  than round: a car that rounds up asks for more than it was allowed. Each field is replaced only when a
  message actually carries one, so a station that states its ceiling once keeps it.
  <br>**The station half.** `Secc2.DcRunningMaxAmps` and `DcAdvertisedMaxAmps` let a station state a
  ceiling, serve under it, and report `EVSECurrentLimitAchieved` truthfully. **Both opt-in**, the same
  shape as `TransportSecurity.Unknown` in the `[V2G20-1237]` fix above: left unset the wire output is
  byte-for-byte what the session corpus records, so no vector needed regenerating. One inaccuracy is
  deliberately left alone and named at the property: the default path still reports
  `EVSECurrentLimitAchieved = false` while serving at its own `DcMaxAmps`, and correcting that would
  rewrite every recorded trace to settle a question no counterparty has asked.
  <br>Four tests in
  [`Iso2RunningLimitTests`](../ISO15118ConformanceTests.Simulation/StateMachines/Iso2RunningLimitTests.cs);
  **two of the four fail** when the clamp is removed, which is how it was checked. The other two pin the
  station half and the unchanged default, and the fixture says which is which. Suite green at 1 366.
  <br>**The requirement side, cited the same evening — and it cost the claim rather than confirming
  it.** There is **no obligation on the EV** to hold its target inside the station's stated maximum, in
  either document. `[V2G20-2188]` puts that duty on the **SECC** — it must not violate the communicated
  limits while chasing the setpoint — and its NOTE says outright that `EVTargetVoltage`/`EVTargetCurrent`
  are targets rather than upper limitations. `[V2G20-2654]` even provides for a station lowering its
  loop limits below what it announced, which is precisely EVerest's 200 A → 55.2 A. `-2` has neither an
  EV-side obligation nor a `[V2G20-2188]` equivalent; it defines the semantics and delegates the
  physical side to IEC 61851-23. Written up in
  [`normative-basis.md`](normative-basis.md).
  <br>So **our car did not violate anything.** What it did was ignore information a real car uses —
  their `EvseManager` clamps such a request under a comment calling it a *broken EV implementation* —
  and the fix stands on that ground rather than on a clause. The half that *does* have a requirement
  behind it is the station one: a station stating a ceiling and serving past it is the case
  `[V2G20-2188]` forbids, and ours now clamps to what it announces.

Both halves of our ISO 15118-20 pause/resume were built by analogy to `-2`, and the code comment in
`Secc20Base.SessionSetup` names the assumption out loud: *"same OldSessionJoined mechanic as -2"*.
**That assumption is wrong in both directions** — `-20` added an obligation `-2` does not have, and
dropped a behaviour `-2` requires. Settled against the requirement text on 2026-08-08; see
[`normative-basis.md`](normative-basis.md) for the clauses and for what may be cited from where.

- ~~**Our EVCC cannot resume an ISO 15118-20 session.**~~ **Fixed 2026-08-08**, app branch
  `iso20-resume-conformance`. `Evcc20Base` now opens a resumed session at `ChargeParameterDiscovery`,
  skipping authorization *and* service negotiation (`[V2G20-1032]`, `[V2G20-1843]` with
  `[V2G20-2097]`/`[V2G20-2098]`/`[V2G20-5046]`), and `Secc20Base` enforces the same sequence — which it
  could not do while it shared the bug. Three loopback tests in
  [`Iso20LoopbackTests`](../ISO15118ConformanceTests.Simulation/E2E/Iso20LoopbackTests.cs); the first was
  verified to fail when the EVCC branch is reverted.
- ~~**Our SECC accepts an ISO 15118-20 resume from any EVCC.**~~ **Fixed 2026-08-08**, same branch.
  `SessionBinding20` implements the standard's worked example — `SHA-512(SessionID ‖ SHA-512(vehicle
  leaf))` from the TLS handshake `[V2G20-2677]` requires anyway — and a failed check becomes a new
  session under a fresh id (`[V2G20-2626]`/`[V2G20-2627]`) rather than a distinguishable refusal. Fails
  closed: no certificate, no resume. The EVCC-side mirror `[V2G20-2539]` and the purge paths
  (`[V2G20-2615]`–`[V2G20-2617]`) went in with it; the car deliberately fails *open* where it cannot
  check, for the reason documented at `Evcc20Base.ResumeBinding`.
- ~~Re-run the EVerest pause/resume~~ **done** — both halves complete, and their own message log shows
  the five skipped messages
  ([re-run notes](interop-runs/2026-08-08-everest-pause-resume-tls-rerun/notes.md)). The matrix cell is
  `✅`. **One part of the fix remains unverified by anyone but us:** the session binding. This direction
  only consults *their* SECC's value, so ours was never compared against it; the cross-check needs their
  EVCC against our SECC, and their EVCC is Josev-derived, whose `-20` resume cannot reach
  `OK_OldSessionJoined` at all. Blocked on [our own filing](reports/josev-iso20-pause-resume.md) being
  acted on — which puts it in *Blocked on the counterparty*, above, in spirit if not in the table.
- ~~**Our TLS chain validation ignored what the peer sent.**~~ **Fixed 2026-08-09.** Both .NET call
  sites dropped the validation callback's `X509Chain` argument, whose `ChainPolicy.ExtraStore` carries
  the certificates the peer put on the wire, and validated the bare leaf — so a peer sending a complete
  chain was indistinguishable from one sending none. `TrustRoots.PeerIntermediates` now supplies them
  (the BouncyCastle path always did). **It cost a wrong finding about a counterparty**, corrected in
  the same run note; the lesson worth keeping is in there and not here.
  [`2026-08-09-edf-chain-validation`](interop-runs/2026-08-09-edf-chain-validation/notes.md);
  five tests in `ISO15118ConformanceTests.Simulation/Security/ChainValidationTests.cs`, the validator's
  first coverage of any kind.
- ~~**Our EVCC offers ISO 15118-20 without regard to the TLS version underneath it.**~~ **Fixed
  2026-08-10**, app branch `iso20-transport-conformance`, and the SECC mirror with it. `[V2G20-1237]`
  forbids offering `-20` in the `SupportedAppProtocolReq` when the established connection is TLS 1.2 or
  lower, or plain TCP; `[V2G20-2356]` is the SECC's mirror and `[V2G20-1805]` states both at once, all
  three pointing at Table 5. On 2026-08-06 our multi-protocol offer went out over a TLS 1.2 connection
  with the `-20` entry still in it, and EVerest's `IsoMux` selected it — their half is
  [the nineteenth filing](reports/everest-isomux.md), ours was this line.
  <br>`SapHandshake` now takes a `TransportSecurity`, drops `-20` from an offer that may not carry it,
  aborts rather than sending an empty request when nothing else was offered, and on the station side
  will not select `-20` there however the car ranked it. Fourteen tests in
  `WWCP_ISO15118_Session_Tests/Sap/Iso20TransportTests.cs`; **six of them fail** when the rule is
  neutered, which is how it was checked.
  <br>**The plain-TCP half stayed reachable, deliberately.** `TransportSecurity.Unknown` — the default —
  stands the rule down, so every existing caller behaves exactly as before and most of this matrix goes
  on running `-20` over TCP on purpose. What changed is that the three places representing a real peer
  (`evcc`, `secc`, and the interop fixture that made the mistake) now work the transport out and **say
  so in the transcript** when they proceed anyway. The defect was never the plain-TCP run; it was that
  nothing said a word when the same offer went out over TLS 1.2 against a real station.
- ~~**Our `-20` service-catalogue check is narrower than the two requirements it satisfies.**~~
  **One fixed 2026-08-10, the other withdrawn as not a gap.** The refusals added on 2026-08-09
  (`Secc20Base.SvcDetailStep` / `SvcSelectionStep`) turned out to be obliged rather than merely sensible
  — `[V2G20-425]`/`[V2G20-464]` for `FAILED_ServiceIDInvalid`, `[V2G20-433]`/`[V2G20-467]` for
  `FAILED_ServiceSelectionInvalid`, with `[V2G20-1216]` as the EVCC's mirror.
  - **The parameter set is now checked.** `[V2G20-433]` speaks of a *`ServiceID`, `ParameterSetID`
    pair* the SECC never offered, and `Advertised(ushort)` compared the id alone. `Secc20Base` records
    every pair as its `ServiceDetailRes` goes out and holds the selection against that, which also
    refuses a service whose detail was never asked for — the ParameterSetIDs exist nowhere else, so a
    car naming one it was not given is naming a value it invented. Three tests in
    [`Secc20ServiceCatalogueTests`](../ISO15118ConformanceTests.Simulation/StateMachines/Secc20ServiceCatalogueTests.cs);
    all three fail when the pair check is removed.
  - **`FAILED_NoEnergyTransferServiceSelected` was not a gap.** `[V2G20-1618]` wants it where the
    selection names no energy transfer service at all, and **this station cannot receive that request**:
    the schema makes `SelectedEnergyTransferService` a mandatory single element, and the only way to
    name a non-energy-transfer service is a VAS id, while `SvcDiscovery` sends `VASList: null`. An id
    that is neither is unadvertised, which is `[V2G20-467]`'s case and already answered. Writing the
    branch would have been unreachable code that reads as coverage. Recorded at `SvcSelectionStep` with
    the condition that would make it live: this station advertising a value-added service.
  <br>Worth keeping the shape of this entry rather than deleting it. Both items came from *reading a
  requirement to correct a stale comment*, neither was ever produced on the wire, and one of the two
  evaporated on contact with the schema. That ratio is the argument for checking before writing code,
  not against reading requirements.
- ~~**Minor, in a `✅` cell:** on an ISO 15118-2 resume, `[V2G2-743]` requires `EAmount` to be reduced by
  the energy already delivered.~~ **Fixed 2026-08-10**, and it was not where this entry said it was.
  The state machine had already stopped sending a constant: since the energy-goal binding, `EAmount` is
  the pack's `EnergyNeededWh`, which shrinks as the pack charges. What was wrong was one line in the
  **CLI** — `Battery = BuildBattery(args)` ran per connection, so the resumed session met a *fresh* pack
  and asked for the full original amount again with the real one already part-charged. The car now keeps
  one pack across the pause, which is what a car does.
  <br>Behind that, the batteryless fallback really did send 22 kWh twice. `ResumableSession` carries a
  `DeliveredWh` and `Evcc2.AlreadyChargedWh` takes it off the literal — read **only** when there is no
  battery, since a carried pack already accounts for it and subtracting twice is the obvious way to get
  this wrong.
  <br>Three tests in
  [`Iso2ResumeEnergyTests`](../ISO15118ConformanceTests.Simulation/StateMachines/Iso2ResumeEnergyTests.cs),
  and **one of the three** fails when the fix is removed — said out loud in the fixture, because the
  other two pin an invariant that was already true and neither covers the CLI line that was the actual
  defect. `DepartureTime` is still omitted entirely, which leaves `[V2G2-742]` vacuous rather than
  violated, unchanged and deliberate.
  <br>Carries the `-2` document caveat in [`normative-basis.md`](normative-basis.md) — the text to hand
  is the 2022 DIS revision, while our stack targets ISO 15118-2:2014.

## Open questions about our own stack

Not gaps in coverage — things a counterparty's behaviour raised about us, which are not settled.

**Currently none.** The one raised on 2026-08-10 — their `EvseManager` warning *"EV ignores new EVSE max
limits"*, 47 times across our runs — was decoded from the recorded frames the same day and became a
defect with an owner; it is in *Ours to fix*, below. The one before it was the ISO 15118-20
vehicle-certificate binding, open from the 2026-08-08 EVerest run until the requirement text settled it,
also below.

## Structural — will not close without someone else building something

- **tux-evse, everything -20.** Their stack speaks ISO 15118-2 and DIN 70121. The whole `-20` column
  is `—` and stays that way.
- **eVDriveFlow, Plug & Charge.** Established 2026-08-11 rather than assumed: **they implement none.**
  No `CertificateInstallation` handler in either role's state machine, and the Plug & Charge vocabulary
  lives only in the xsdata-generated bindings, ISO's schema and the Sphinx output of both — plus two
  Table 214 timeout keys with no handler behind them. Their README's *Supported features* does not
  claim it and `PnC` appears nowhere in their documentation; both halves ship
  `authorization_services = [EIM]`. The bytes already in this repository agree, which is why it took no
  run: their `AuthorizationSetupRes` is 20 payload bytes against our PnC-offering 38, with nowhere for
  a `GenChallenge`.
  <br>This is what closing a `▢` by **answering its condition** looks like rather than by testing:
  the entry had said *"first establish whether they do contract certificates at all"*, and the answer
  moves the cell here instead of onto a to-do list. `CertificateInstallation` for this counterparty was
  already `—` for the same reason.
  <br>The audit also found a latent defect and deliberately did **not** file it: their SECC hardcodes
  the EIM authorization mode whatever the configurable `authorization_services` says, which
  `[V2G20-1219]` and `[V2G20-2568]` each forbid — but it is unreachable in the shipped configuration,
  they claim no PnC, and reaching it means configuring their station rather than observing it. It is a
  note on [the existing filing](reports/evdriveflow-authorization-setup.md), where the paired EVCC
  handler already is. [`…-edf-pnc-source-audit`](interop-runs/2026-08-11-edf-pnc-source-audit/notes.md).
- **WPT and ACDP session state machines.** No independent stack implements them, so `▢ codec only` is
  the ceiling. What *did* change on 2026-08-08: the bytes are now judged by EXIficient rather than only
  by the generator that produced them, which is the strongest form available without a second stack.
- **The `-20` contract-key wrap.** Its *chain* stopped being self-checked on 2026-08-08 (EVerest's OEM
  root, above). The wrap itself — ephemeral secp521r1 ECDH → ConcatKDF-SHA512 → AES-256-GCM — is
  round-trip-tested by us and by nobody else, and cannot become otherwise here: both Josev forks send the
  request and neither implements the response, and their provisioning leaves are P-256, which cannot join
  the key agreement even if they did. Needs a stack that implements `-20` provisioning; none does.
  <br>**This is now the only part of the chain-and-certificate work with no external witness.** The
  BouncyCastle station path was listed here beside it until 2026-08-09, when it turned out to be a
  missing `--server-cert` rather than a structural limit
  ([`…-edf-bouncycastle-chain`](interop-runs/2026-08-09-edf-bouncycastle-chain/notes.md)). Worth the
  reminder that "structural" is a claim about the world and deserves the same scepticism as any other.
- **MCS and MCS_BPT beyond EVerest.** Only one counterparty implements them at all.
- **Multi-protocol SAP offer beyond EVerest.** Same.

## Not in the matrix at all

- **Thirty-eight filings across six projects** are drafted and unsent in [`reports/`](reports/README.md).
  Each ends with a *Before sending* checklist whose unticked items are the parts only a person can do.
  This is the largest single block of finished work waiting on a human.
- ~~**The eighteenth needs one thing that is ours:** the contactor report has never been seen happen.~~
  **Done 2026-08-09**, hours after it was written: `ac_contactor_closed(false)` published on their own
  interface inside the 3 s window, `PowerDeliveryRes(OK)` ~95 ms later and three charge loops after
  that, 2 of 2, against a control that fails at 3.000 s
  ([run notes](interop-runs/2026-08-09-everest-ac-contactor-injection/notes.md)). It did **not** buy
  the two AC matrix cells, which was the hope: injecting a contactor report is not the same capability
  as driving their EV-side hardware, and `cphold` still walls at `FAILED_ContactorError`.
- **A methodological item, from the EVerest MQTT run:** *"Run every future session twice, in every
  harness. One session is not a test of a station."* Not systematically applied.
- ~~**A candidate seventeenth filing, unwritten:** counterparty `iso-20` certificate scripts that emit
  secp256r1 material.~~ **Written 2026-08-08:**
  [`josev-iso20-pki-curve.md`](reports/josev-iso20-pki-curve.md). It turned out to be one script in two
  homes rather than a habit across counterparties — `create_certs.sh` in `SwitchEV/iso15118` and in
  EVerest's fork of it, whose `iso-20` branch selects the same `prime256v1` as `-2` under its own
  `# TODO Check correct version for ISO 15118-20`. Measured across all five branches of the generated
  set, and the report leads with the consequence rather than the table: `-20` contract provisioning
  cannot complete with that key material at all, the schema's key-wrap curve choice being `SECP521` or
  `X448` and nothing else
  ([measurement](interop-runs/2026-08-08-everest-oem-provisioning-chain/notes.md)). The requirement side
  — `[V2G20-2674]`, `[V2G20-2319]`, `[V2G20-2320]`, Tables 6 to 8 — is in
  [`normative-basis.md`](normative-basis.md), which is what made it filable at all.
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
