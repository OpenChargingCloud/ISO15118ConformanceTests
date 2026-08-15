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

Nothing here can be moved by work on our side. Every row has a filed or drafted report.

The two EVerest AC rows that used to carry that exception are **gone as of 2026-08-13**, and how they
went is worth one line here rather than only in the run notes: they were not a counterparty wall and
they were not "their EV-side hardware simulation, which is ours to build" either. Their `-20`
`PowerDelivery` waits for a contactor *event* inside a 3 s window and remembers nothing that arrived
before it; our harness raised the car's CP line at plug-in, so their own confirmation was produced
**4,948 s early** and discarded. Moving one MQTT publish into the window turned four months of
`FAILED_ContactorError` into five complete sessions, AC and AC_BPT, with a control that still fails
([`…-d20-ac-contactor-window`](interop-runs/2026-08-13-everest-d20-ac-contactor-window/notes.md)).
**The entry described the wall confidently and described it wrong** — which is the argument for this
file naming a mechanism rather than a verdict, and for re-reading one before quoting it.

| | Counterparty | State | Waiting on |
|---|---|---|---|
| **Pause / Resume, -20** | Josev | ⛔ `EV→` | Their `-20` `SessionSetup` compares the resumed session ID against the *live* connection instead of the preserved context, which its `-20` states never fill — so `OK_OldSessionJoined` is unreachable. Six-line fix, mirroring their own working `-2` branch. Filed: [`josev-iso20-pause-resume.md`](reports/josev-iso20-pause-resume.md). |
| **DC Scheduled / Dynamic, -20** | eVDriveFlow | ◐ | `hasattr` used as a presence test on an `Optional[int]`, so our legally omitted `TargetSOC` overwrites theirs with `None`. Filed: [`evdriveflow-headless-session.md`](reports/evdriveflow-headless-session.md). |
| **Renegotiation, -2** | EVerest | ⛔ `EV→` | Their DC station requires CableCheck again after a renegotiation, so the `PowerDeliveryReq(Start)` that restarts the charge is answered `FAILED_SequenceError`. The trigger and the re-discovery are accepted — this is the restart path only, and the state that would take it already exists next door. Filed: [`everest-evsev2g-renegotiation-cablecheck.md`](reports/everest-evsev2g-renegotiation-cablecheck.md). |
| **TLS 1.2 unilateral, -2** | tux-evse | ⛔ pinned | Their configs offer neither suite ISO 15118-2 prescribes. Filed: [`tux-evse-tls.md`](reports/tux-evse-tls.md). |
| **CertificateInstallation, -20** | Josev · EVerest | ◐ | Both send a real signed request — SwitchEV's on 2026-07-22, EVerest's `PyEvJosev` on 2026-08-08 with its own OEM root. Our response is decoded and validated, and then each ends at the same `NotImplementedError`: the fork inherited the gap. Nothing to file that the upstream code does not already say out loud. |

## Never verified by anything but us

The matrix marks these `◐` because a peer *consumed* what we sent. Nothing checked it. That is a
weaker claim than it looks and worth separating from "untested".

- **Signed tariffs, -20** (Josev) — their AC EVCC consumed our signed `AbsolutePriceSchedule`; nothing
  external verifies the signature. **And as of 2026-08-11 nothing can**: the one remaining candidate,
  EVerest, sends no price schedule of its own and its EV is Josev's. See *Structural*, below.
- **Renegotiation, -20** (Josev) — their EV sends a real `SessionStopReq(ServiceRenegotiation)`
  [V2G20-1477] and then drops the link anyway — their `SessionStop` state sets `next_state = ServiceDiscovery`
  without building the request that state needs, and their framework raises on it. Filed:
  [`josev-iso20-renegotiation.md`](reports/josev-iso20-renegotiation.md).
- ~~**Plug & Charge, -2** (EVerest) — chain accepted and our signature verified, but their SIL has no
  contract-validating backend, so nothing decides whether the contract is *good*.~~
  **Closed 2026-08-13 by building the backend.** It was never a missing capability of theirs: EVerest
  delegates the contract decision to whoever is wired as `token_validator`, which in a real deployment
  is the CSMS through their OCPP module and in their SIL is `DummyTokenValidator` returning a constant
  from its own config file. So the hole was in *our* rig, and closing it needed no patch to theirs —
  [`contract-validator-arm.sh`](../tools/interop-everest/contract-validator-arm.sh) starts the station
  with that module withheld (`--standalone`) and answers on its topics over MQTT with their own
  `everestpy`, which is what their `everest-testing` `ProbeModule` does.
  <br>**Both halves are now measured** ([run](interop-runs/2026-08-13-everest-contract-validator/notes.md)).
  What their station hands over: one call per session carrying the eMAID off the leaf's CN, the
  three-certificate chain in PEM, and `connectors` added by `EvseManager`. What it does with an answer:
  `Accepted` carried a `-2` PnC session **past `Authorization` for the first time in this project**, on
  to `ChargeParameterDiscovery` and `CableCheck`; `Invalid` + `certificate_status: CertificateRevoked`
  produced **`AuthorizationRes = FAILED_CertificateRevoked`**, which no configuration of their SIL can
  reach — `DummyTokenValidator` cannot set `certificate_status` at all, so `evse_managerImpl.cpp:386`
  fills in `value_or(Accepted)` and that branch is dead.
  <br>**And the reason every earlier PnC run dead-ended was one missing connection, not the missing
  backend.** `EvseManager` republishes the contract token through its own `token_provider`
  implementation, and only `config-sil-ocpp-pnc.yaml` and `config-sil-ocpp201-pnc.yaml` connect that to
  `auth`; everywhere else it is published to a variable nobody subscribed to and the session runs to
  `auth_timeout_pnc`. `PaymentDetailsRes` is `OK`, the signature verifies, and then 366 polls and
  `FAILED`, with no token in any log. Not a defect — their plain SIL configs are simply not PnC
  configs — but a fact about driving their harness, now in the
  [harness README](../tools/interop-everest/README.md).
  <br>**Still not verified by anything but us:** whether the *contract* is good. Nothing on either side
  of this checks that, and nothing can while the decider is a test double — the arm proves their
  station asks correctly and carries the verdict correctly, which is the whole of what a backend is
  answerable for. One observation left deliberately unfiled: `iso15118CertificateHashData` came back
  **absent** on a chain that verified (`OcspCache::lookup: not in cache`), so a backend is handed no
  revocation material at all. That is the far end of
  [`everest-evse-security-ocsp-dropped`](reports/everest-evse-security-ocsp-dropped.md) and worth
  re-measuring once that lands.
  <br>**The arm's second run turned into the forty-fifth filing.** Pointed at their `-20` station it
  found that a *rejected* verdict reaches `Evse15118D20` not at all — `EvseManager` forwards them for
  Plug & Charge only, and that module offers no PnC — so their station answers `Ongoing` for 180 s and
  then the wrong code, where `[V2G20-2230]` allows 1,5 s. The `-2` control run the same day is what
  makes it sendable: `EvseV2G` does the identical thing and is **right** to, because `[V2G2-854]`
  inverts the rule. [`everest-d20-eim-rejection`](reports/everest-d20-eim-rejection.md),
  [run](interop-runs/2026-08-13-everest-d20-eim-rejection/notes.md).

## Untested, and nothing is stopping us

The honest backlog. No counterparty defect in the way, no missing capability on our side that is known.

**Two rows opened on 2026-08-13 by closing something else, and one closed the same evening.** Until that
morning the `-20` AC cells were behind a wall, so nothing beyond them could be listed here at all; once
AC and AC_BPT ran
([`…-d20-ac-contactor-window`](interop-runs/2026-08-13-everest-d20-ac-contactor-window/notes.md)),
ordinary untested work appeared behind them:

| | Counterparty | Note |
|---|---|---|
| ~~**AC over TLS, -20**~~ | ~~EVerest~~ | **Run the same evening — AC 2/2 and AC_BPT 2/2 over mutual TLS 1.3**, their `Handshake complete!` and `Verify certificate result is okay`, contactor window unchanged at +832…1048 ms because the handshake is spent before `PowerDelivery`. That was the run that mattered: every AC session before it was plain TCP, which `[V2G20-1237]` and `[V2G20-2356]` both forbid, so the cells were green for a transport the standard does not allow. [`…-d20-ac-tls13`](interop-runs/2026-08-13-everest-d20-ac-tls13/notes.md). |
| ~~**A recorded reverse AC run**~~ | ~~EVerest~~ | **Run the same evening, and it was the most productive hour of the day.** Their EV discovered our station over SDP, negotiated `-20:AC` and charged — 56 exchanges, all `OK`, 44 charge loops to `SessionStop`. It found **a defect of ours** (the reverse fixture defaulted its SAP catalogue to DC, so every reverse `-20` run ever made announced DC-only) and **a measurement of theirs** (their EV paces the AC charge loop at ≈532 ms, against the 500 ms `[V2G20-1500]` allows a station to wait — 2 of 2 strict runs die on the first loop). [`…-d20-ac-reverse`](interop-runs/2026-08-13-everest-d20-ac-reverse/notes.md). |
| ~~**The EVCC half of Tables 216/217**~~ | ~~—~~ | **Decided 2026-08-13: the EVCC is bound, and to a *performance* criterion rather than an error one.** Table 216 gives `V2G_EVCC_Sequence_Performance_Time` one row — `AC_ChargeLoopReq`, **0,25 s** — and `[V2G20-1499]` makes implementing it a *shall*; Figure 212 draws that span from response-received to next-request-sent and its legend separates *Performance Time (Performance Criteria)* from *Timeout (Error Criteria)*, which is the SECC's 0,5 s. So their EV's ≈532 ms misses the car's own performance time by **2,1×**, and the abort belongs to the station, where `[V2G20-443]` does make it an error. Written up in [`normative-basis.md`](normative-basis.md), including the absence it turned up: there is no general clause starting the EVCC's sequence timer, only the SECC's. |
| ~~**`-2` AC over TLS 1.2**~~ | ~~EVerest~~ | **Run 2026-08-14 — four sessions, 13/13 `OK` each**, against `EvseV2G` with one line changed (`tls_security: force`). Their transport is conformant where it matters: TLS 1.2 with `TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA256`, one of the two ISO 15118-2 prescribes, and they send their **whole chain** where `Evse15118D20` sends a bare leaf. `-2` TLS is unilateral, so it needed no vehicle credential and no PKI regeneration at all. It also found a defect **of ours** — see *Ours to fix*, closed the same hour. [`…-iso2-ac-tls12`](interop-runs/2026-08-14-everest-iso2-ac-tls12/notes.md). |
| ~~**Reverse over TLS**~~ | ~~EVerest~~ | **Run 2026-08-14, and the reason it had never run was ours.** `InteropEnvironment.ServerTlsOrNull` has existed since the tux-evse runs and the eVDriveFlow reverse fixture uses it; the EVerest one built a plain listener and advertised `tls: false` as a constant — so *"the reverse direction has never run over TLS"* was a statement about our harness that read like one about the counterparties. With two lines changed: their EV discovered our station over SDP with the TLS byte, handshook **mutual TLS 1.3**, presented an OEM vehicle certificate of its own, and charged — 56 exchanges, all `OK`, 43 charge loops. [`…-d20-ac-reverse-tls`](interop-runs/2026-08-14-everest-d20-ac-reverse-tls/notes.md). |

**That table is now empty**, which has not been true since it was written. What replaced it is one
observation the same run left behind, recorded as a candidate rather than a filing: **their `PyEvJosev`
asked for TLS in its SDP request, was answered `NoTLS`, and ran an ISO 15118-20 session over plain TCP** —
the requirement `[V2G20-1237]` puts `-20` in the TLS 1.3 row alone, and it is the same one our own EVCC
was fixed against on 2026-08-10. It is not filed because this run configured their car and then offered
it the downgrade; the arm that would settle it is a **station of theirs** answering `NoTLS` to a `-20` EV,
which is a different run and a cheap one.

**The table emptied and then produced work that was not in it.** `AC_BPT` in reverse — carried only in
the 08-13 run note's `Next`, never in this file — ran the same afternoon and is the half of BPT no
forward run can produce: **their car chose our bidirectional service**, rather than answering a request
in which we had ranked it first
([`…-d20-ac-bpt-reverse`](interop-runs/2026-08-14-everest-d20-ac-bpt-reverse/notes.md)). It cost one
line in their config and one guard in ours, and it left an obvious next — **`DC_BPT` in reverse**, the
same one-line change against `Secc20Dc`'s `{ 2, 6 }` with the guard already covering it. **Run the same
hour**, and it is the first this week that cost no code at all: their EV picked service 6 and drove the
whole DC sequence, CableCheck and PreCharge and WeldingDetection included, plain and over mutual TLS 1.3
([`…-d20-dc-bpt-reverse`](interop-runs/2026-08-14-everest-d20-dc-bpt-reverse/notes.md)). Writing it here
rather than in a run note's `Next` is what made it get done, which is the argument for this file.

**`←SECC` in Dynamic control mode ran 2026-08-15**, with a Scheduled control arm that switches with the
offer — three sessions identical in every count, differing only in the mode
([`…-dc-dynamic-reverse`](interop-runs/2026-08-15-everest-d20-dc-dynamic-reverse/notes.md)). It needed a
property the station had never exposed, which is the **fourth** instance in three days of *a value our
own side already held that no caller could reach*. That pattern now has more instances than any
counterparty defect found in the same period, and it is worth treating as the thing to look for rather
than as a run of bad luck.

**Dynamic over AC ran the same day**, with its own Scheduled control — and it closed a loose end rather
than just filling a cell. The three arms are 56 exchanges each and differ only in composition, and lined
up with the two earlier AC reverse runs they give an invariant across five sessions: **`PowerDelivery`
before the loop plus charge loops = 45, every time**. The extra `PowerDeliveryReq` first noted on 08-14
and withdrawn the same day is readiness polling — our own `PowerOn` phase self-loops for it — never the
transport, and Dynamic only makes it larger
([`…-ac-dynamic-reverse`](interop-runs/2026-08-15-everest-d20-ac-dynamic-reverse/notes.md)).

**`AC_BPT` in Dynamic ran the same day**, completing all four AC charge-loop control-mode variants against
a live peer — and refuting the explanation the run before it had offered
([`…-ac-bpt-dynamic-reverse`](interop-runs/2026-08-15-everest-d20-ac-bpt-dynamic-reverse/notes.md)). Two
inferences withdrawn in two days from one three-line observation, both hedged when written, both refuted
by the next run rather than by re-reading. The invariant underneath never moved. **The rule worth keeping
is narrower than "hedge more":** an explanation offered for a difference between two runs is a hypothesis
about the next ten, and this file should treat it as one.

**The reverse `-2` run over TLS 1.2 closed the list on 2026-08-15**, and it was the least routine item on
it. Their car authorizes by **EIM over plain TCP and by Plug & Charge over TLS** — `-2`'s own *no
Contract without TLS* rule, applied by the car rather than by the station this project met it from in
August. It needed no PKI regeneration at all
([`…-iso2-ac-reverse-tls12`](interop-runs/2026-08-15-everest-iso2-ac-reverse-tls12/notes.md)).

**It also opened something that belongs here rather than in a run note.** Every inbound Plug & Charge
result in this matrix — both protocols, every reverse run — was recorded with the contract chain
**unvalidated**: `Secc2` and `Secc20Base` have carried a `ContractChainValidator` since they verified
signatures, and no interop run could set it, so what was checked was the signature against the leaf the
car presented. `V2G_INTEROP_CONTRACT_ROOTS` now reaches it and the `-2` run above is anchored at
EVerest's MO root, with a negative control. **The `-20` reverse PnC cells have not been re-run**, and
until they are, *"their signed AuthorizationReq verified by our SECC"* means the signature and not the
contract. That is one variable per cell, and it is the largest remaining overstatement in the matrix.

The item that closed took **three attempts, all of them ours**: a client chain one certificate short, a
set of leftover credentials that could not have fitted, and their one-shot SDP. Each is written up in
the run note rather than smoothed away, and two became [`tls-pki-setup.sh`](../tools/interop-everest/tls-pki-setup.sh)
and its restore twin, so the next TLS run starts from a script instead of from a run note.

**The struck-through table below is the previous state**, kept because the shape of a day that empties a
backlog is that half of it turns out not to have been work.

| | Counterparty | Note |
|---|---|---|
| **Pause / Resume, -20** | EVerest | ~~▢~~ **run 2026-08-08 — and it is ours that failed.** Their station resumed on the first attempt (`OK_OldSessionJoined`, over mutual TLS with their minted vehicle credential); our EVCC then re-sent `AuthorizationSetupReq` and got `FAILED_SequenceError`, because a resumed `-20` session skips authorization and opens at `{AC,DC}_ChargeParameterDiscovery`. Moved to *our stack*, below. `-2` is `—` in the matrix, not `▢`. |
| ~~**Signed tariffs, -20**~~ | ~~EVerest~~ | **Closed 2026-08-11 by answering the condition: their station sends no price schedule at all, deliberately.** Moved to *Structural*, below. |
| **Renegotiation, -2 and -20** | EVerest | ~~▢ both~~ **`-2` run 2026-08-11 — and it found something.** Their station accepts `PowerDeliveryReq(Renegotiate)` and the fresh `ChargeParameterDiscovery`, then refuses the `PowerDeliveryReq(Start)` that restarts the charge with `FAILED_SequenceError`: `handle_iso_charge_parameter_discovery` puts a DC session into `WAIT_FOR_CABLECHECK`, so they want the isolation test re-run where Annex I's own sequence goes straight to `PowerDelivery` and the `[V2G2-680]` NOTE keeps the contactor closed. Moved to *Blocked on the counterparty*; [the fortieth filing](reports/everest-evsev2g-renegotiation-cablecheck.md), [run](interop-runs/2026-08-11-everest-iso2-renegotiation/notes.md). **-20 run 2026-08-10** — their `PyEvJosev` EV took our station's `ServiceRenegotiation` notification, stopped the charge, ran welding detection and sent `SessionStopReq(ServiceRenegotiation)`; our SECC answered `OK` and stayed open, and their EV closed the connection. Same defect as Josev's, now seen in **DC** and against the fork `26f7988` ([run](interop-runs/2026-08-10-everest-iso20-renegotiation-reverse/notes.md), [filing](reports/josev-iso20-renegotiation.md)). **Both directions are now run; neither is `▢`.** |
| **Contract provisioning, -2** | EVerest · Josev | ~~▢~~ **✅ complete against EVerest 2026-08-11, in three sessions — the third closed the loop by standing in as the MO backend.** Their station has no issuer of its own: it publishes the EV's EXI and waits 4 500 ms. With [`Iso2MoBackend`](../ISO15118ConformanceTests.Simulation/Interop/Iso2MoBackend.cs) answering through their own MQTT, the `CertificateInstallationRes` came back **`OK`**, our EVCC verified the four-reference signature and unwrapped the contract key, and the session continued to `PaymentDetails` and `Authorization`. One deviation measured and **not** explained: the return frame carries our 1 458 EXI bytes **plus a trailing `0x00`**, declared in the V2GTP length — benign for a decoder, wrong on the wire, cause unread. The earlier two sessions: Their certificate service is advertised (ServiceID 2, `ContractCertificate`), our `CertificateInstallationReq` is accepted, and the EXI reaches their MQTT interface **byte-identical** — 802 bytes, SHA-256 match, twice. With no backend in their SIL they wait exactly 4 500 ms and fail the session. A control pair (EIM vs Contract) isolated the response code: `FAILED_SequenceError` in an EIM session, plain `FAILED` in a Contract one, because their state table admits the message only in the Contract branch — **an open question, not yet a finding** ([run](interop-runs/2026-08-11-everest-iso2-cert-install/notes.md)). It did **not** settle the `CertificateUpdate` filing: they advertise parameter-set-ID **1 only**, Update being an explicit `TODO`, so the selection gate answers before the stub can. Josev implements neither and answers `FAILED` correctly, so there is nothing to measure there. |
| ~~**Plug & Charge, -20**~~ | ~~eVDriveFlow~~ | **Closed 2026-08-11 by answering the condition: they implement none.** Moved to *Structural*, below. |

## Ours to fix

- ~~**Our `-20` car has one timeout for every response it waits for, and `-20` does not — and it is
  checked too late to catch a station that never answers.**~~ **Fixed 2026-08-11**, stack branch
  `iso20-evcc-msg-timeout`, and it came out of *withdrawing* the item below rather than from a run.
  `Evcc20Base` took one `perMessageTimeout` and applied it to every exchange, where
  `V2G_EVCC_Msg_Timeout` is per message type: Table 215 gives 2 s for the ordinary ones, **5 s** for
  `CertificateInstallationReq` and `ServiceDetailReq`, and Tables 216/217/218 override the
  **charge-loop request to 0,5 s** (`[V2G20-1499]`, `[V2G20-1501]`, `[V2G20-5069]`). The exact car-side
  twin of the station defect fixed the same morning, and of
  [the one filed against EVerest](reports/everest-d20-sequence-timeout.md).
  <br>**The second half was the worse one, and it is the one the tests are really about.**
  `ExchangeRaw` awaited `ReadFrameAsync` with **no budget** and compared the elapsed time *afterwards*,
  so the timeout could only ever catch an answer that arrived **late**. A station that simply stopped
  answering held our car until the session-level token fired — minutes in a live run, and forever
  without one. The read now carries its own `CancelAfter`, the same fix the station side got that
  morning. `ChargeLoopMsgTimeout` and `SlowMsgTimeout` are init-only, so a test can put the flat
  behaviour back.
  <br>**It needed an instrument that did not exist**: a station that goes quiet *holding the socket
  open*. One that hangs up is an EOF, and an EOF ends the read whatever the timeout does — which is
  exactly why the old code looked healthy. `Secc20Base.GoSilentInChargeLoop` is the mirror of the EVCC
  knob built for the station's own timer, and it is usable against a **foreign** EV in an interop run,
  which is the point of building it in the stack rather than in a fixture. **No run of this suite had
  ever measured any EV's per-message timeout, ours included.**
  <br>Three tests in
  [`Iso20MsgTimeoutTests`](../ISO15118ConformanceTests.Simulation/E2E/Iso20MsgTimeoutTests.cs); the
  tight one fails **both** when the read budget is removed and when the charge-loop value is not applied
  — checked one at a time — the control pins that the car is ended by the peer's EOF rather than by its
  own timer, and the third pins that an ordinary session is untouched. Suite green at 1 403.
  <br>Requirement side settled first — see [`normative-basis.md`](normative-basis.md) for the four
  parameters of Tables 215–218 and which clock each belongs to.

- ~~**Our `-20` station has one sequence timeout for every message type, and `-20` does not.**~~
  **The charge-loop half is fixed 2026-08-11**, and the other half turned out not to exist (below).
  `Secc20Base` took a single `sequenceTimeout` and applied
  it in every phase — the same shape as [the defect filed against EVerest](reports/everest-d20-sequence-timeout.md):
  Table 215 gives 60 s for *all other messages*, and Tables 216/217 — obliged by `[V2G20-1500]` and
  `[V2G20-1502]` — override it to **0,5 s** after `AC_ChargeLoopRes`/`DC_ChargeLoopRes`, the phase in
  which the contactor is closed. Until now our own station flattened it to 60 s too.
  <br>**The fix and its test were one piece of work**, exactly as predicted. `Secc20Base` now tracks
  whether the last response it sent was a charge-loop response and arms the *next-request* wait — both
  the `RunAsync` read (the real enforcement against a silent EV) and the reactive `Handle` check — with
  `ChargeLoopSequenceTimeout` (init-only, default 0,5 s) instead of the baseline. Two loopback tests in
  [`Iso20ChargeLoopTimeoutTests`](../ISO15118ConformanceTests.Simulation/E2E/Iso20ChargeLoopTimeoutTests.cs)
  drive `Evcc20Base.GoSilentInChargeLoop` — the instrument built to measure *theirs* — against our own
  station: the tight one asserts the session ends in under a second and **fails when the default is put
  back to 60 s** (verified), the control pins the flat behaviour the fix removes.
  <br>~~**What is left, and it is smaller:** Table 217 gives the DC-only self-loop phases their own
  sub-60 s timeouts too — CableCheck and PreCharge at 1,5 s, WeldingDetection at 0,25 s.~~
  **Withdrawn 2026-08-11: there are no such timeouts, and this entry was the scrambled extract talking.**
  It had said so itself — *"their exact values were column-scrambled … only the charge-loop 0,5 s was
  confirmed cleanly"* — and re-reading the table settled it the other way. Tables 216/217/218 each carry
  **four** parameters, and `V2G_SECC_Sequence_Timeout` has **exactly one row in each**: the charge-loop
  *response*, 0,5 s, which is what `ChargeLoopSequenceTimeout` already implements. The 1,5 s and 0,25 s
  are `V2G_SECC_Msg_Performance_Time` — how fast the station must *answer* CableCheck, PreCharge and
  WeldingDetection — and the 2 s are `V2G_EVCC_Msg_Timeout`, the *car's* wait. **Our `-20` station is
  complete for this parameter.** The cause was `pdftotext -layout` flattening a stacked name cell, the
  identical illusion Table 108 produced in `-2` the same day; `-table` reads it correctly and the AC and
  WPT tables are the control. Written up in [`normative-basis.md`](normative-basis.md).
  <br>**The `-2` half needed nothing and still does.** Table 108 was re-extracted page by page on
  2026-08-11 — `pdftotext -layout` had flattened its five stacked parameter names into one column, and
  read that way the message list looks like a per-message sequence timeout. It is not: that list belongs
  to `V2G_SECC_Msg_Performance_Time` (`CurrentDemandRes` 0,025 s — how fast the SECC must *answer*), and
  `V2G_SECC_Sequence_Timeout` sits in the `(all messages)` row at **60 s**. So the charge-loop override
  is an addition of the newer document; our `-2` station's flat timeout is correct, and so is EVerest's.

- ~~**Neither of our stations implements `[V2G2-460]` / `[V2G20-460]`.**~~ **Both halves are fixed
  2026-08-11** — `-2` on stack branch `iso2-unknown-session`, `-20` on `iso20-unknown-session` the same
  day, and the `-20` one closed `[V2G20-459]` with it. Found while reading EVerest's `-2` station for the
  same rule: `FAILED_UnknownSession` appeared
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
  <br>None of it blocked [the filing against EVerest](reports/everest-evsev2g-session-id-zero.md),
  since that probe is raw Python and owes our state machines nothing.
  [`…-iso2-session-id-zero`](interop-runs/2026-08-11-everest-iso2-session-id-zero/notes.md).

  **The `-20` half, the same day, and it was the bigger piece.** `Secc20Base` had no table of
  corresponding responses at all: its sequence guard *threw* rather than answering — the wildcard arm of
  the phase switch raised `SessionAborted` under a comment naming the code it would have sent — so the
  station killed the connection instead of answering, and `[V2G20-460]` had nothing to answer *with*.
  `Secc20Base.Refuse(set, request, Refusal)` is that table now: **twenty request types across the three
  generated message sets** (13 CommonMessages, 5 DC, 2 AC), each with its own `ResponseCode` enum, which
  is why the reason travels as an enum of ours and is mapped per set rather than passed as a code. The
  AC/DC half is delegated through `RefuseInEnergyTransferSet` — the same seam, for the same reason, as
  `HandleChargeLoop`. One table, two requirements: `[V2G20-459]` and `[V2G20-460]`.
  <br>**The refusal is terminal here and not in `-2`**, and that asymmetry is the standards' — §8.6
  against §8.8.2, already implemented by `Handle`'s `IsFailure` and now pinned by a test. Where the
  schema forces content that would otherwise be a promise, it is filled with empty material and never a
  real credential: a refused `CertificateInstallationRes` carries an empty chain, a refused
  `AuthorizationSetupRes` offers EIM and no `GenChallenge`.
  <br>**The worked example was theirs**, found on 2026-08-11 while reading their `-20` library for
  something else: EVerest's `d20/context_helper.cpp` is the same table in C++ —
  `handle_sequence_error<Response>(session)` templated over the response type, dispatched from one
  `send_sequence_error(req_type, ctx)` across all sixteen of their `-20` message types. Worth saying in a
  file that mostly runs the other way. It is also the `-20` twin of the `-2` defect **tux-evse's VW
  capture found in us** on 2026-08-06 and that was fixed the same day; the `-20` half never had been.
  <br>Six tests in
  [`Iso20UnknownSessionTests`](../ISO15118ConformanceTests.Simulation/StateMachines/Iso20UnknownSessionTests.cs);
  **four of the six fail** when the guard is neutered — two per requirement, checked one at a time — the
  fifth pins the unchanged default, and the sixth enumerates the request types out of the three
  assemblies and asserts every one is answered by the type that pairs with it. That last one exists
  because the failure mode of a hand-written table of twenty arms is a *missing* arm, which nothing else
  here would reach. Suite green at 1 400.
  <br>**And it cost the test harnesses again — nine call sites this time, and the same value.**
  `SessionContext.SessionId` starts as eight zero bytes, so every `-20` fixture driving `Handle` from its
  own context had been sending the **all-zero SessionID** in every request after `SessionSetupReq`, for
  its whole existence, against a station with no check to fail. Adding the check turned **32 passing
  tests red at once**, which is how it surfaced. That is the same value and the same shape as
  [the defect filed against EVerest's `-2` station](reports/everest-evsev2g-session-id-zero.md) — we were
  modelling the peer we filed about. The one-line fix now lives in
  [`Iso20Handshake`](../ISO15118ConformanceTests.Simulation/StateMachines/Iso20Handshake.cs); the EV
  keeping its own context, which was always right, just never adopted the id the station issued.
  `Secc20CertInstallTests` needed a different answer — it replays a **captured Josev frame** whose
  SessionID is inside the signed bytes, so the station is given that session instead.

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
  <br>~~**And the interop fixtures had a second copy of it, which that fix did not reach.**~~ **Fixed
  2026-08-14.** `InteropEnvironment.DevTlsOrNull` builds its own `X509Chain` rather than going through
  `V2GChainValidator`, and its callback was `(_, certificate, _, _)` — so every counterparty every TLS
  interop run has ever met was judged on its bare leaf, and the intermediates had to be spoon-fed in the
  trust bundle. **Nothing could detect that**, because a bundle of root + Sub-CAs passes either way; only
  a *root-only* anchor tells them apart, and no run had used one until the arm that was meant to confirm
  a paragraph about EVerest's chain. It was refused here and accepted by `openssl s_client -CAfile`
  against the same station minutes apart
  ([`…-iso2-ac-tls12`](interop-runs/2026-08-14-everest-iso2-ac-tls12/notes.md)). Now routed through
  `TrustRoots.PeerIntermediates` like the app's two peers, with a sixth test in the same file — **the one
  of the seven that fails when the fix is removed**, checked by removing it.
  <br>**No earlier run is invalidated**: a superset bundle validates the same chains, so every
  *"we verified their chain"* stands. It was a weaker claim than it read, in all of them.
- ~~**The EVerest reverse fixture could not run over TLS, and the matrix said the counterparties could
  not.**~~ **Fixed 2026-08-14.** `InteropEnvironment.ServerTlsOrNull` has existed since the tux-evse
  reverse runs — its own documentation calls it *"the only way that direction can run over TLS at
  all"* — and the eVDriveFlow reverse fixture uses it. `EverestInteropTests` built a plain
  `TcpV2GListener` and advertised SDP with `tls: false` **written as a constant**, so the entry that read
  *"the reverse direction has never run over TLS in any mode"* was a fact about our harness wearing a
  counterparty's clothes. Two lines: the listener takes the options, the SDP flag is derived from them.
  Their EV then handshook mutual TLS 1.3 on the first attempt
  ([`…-d20-ac-reverse-tls`](interop-runs/2026-08-14-everest-d20-ac-reverse-tls/notes.md)).
  <br>**Third instance in a week of the same shape** — *a capability we already held that no call site
  reached for* — after the reverse fixture's defaulted power mode (08-13) and the interop TLS callback's
  discarded peer chain (08-14, above). All three were invisible for the same reason: the wrong behaviour
  and the right one are indistinguishable until something narrows the input.
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
- **A signed tariff verified by anybody but us.** Our SECC signs an `AbsolutePriceSchedule` and our EVCC
  verifies one; the loopback covers both ends and nothing external does. After the 2026-08-11 audit the
  field of candidates is empty rather than merely unexplored: **EVerest's `-20` station emits no price
  schedule at all** — `create_default_scheduled_control_mode` says *"Providing no price schedule!"* and
  cites an `iso15118.elaad.io` agreement that `[V2G20-2176]`, a *shall*, *"is not required and should be
  ignored"*; the Dynamic branch sets only departure time and SOC. **Josev** consumes ours without
  checking (its EVCC-side tariff check is a literal `# TODO`), **eVDriveFlow** never gets that far, and
  **tux-evse** has no `-20`. So this cannot be moved by choosing a different counterparty, only by one of
  them implementing it.
  <br>Deliberately **not filed**: a deviation documented at the point of non-compliance and attributed to
  an industry agreement is a decision, not a defect. The data point is worth more than the verdict —
  a published `shall` that a major implementation treats as withdrawn — and it is a caution before this
  project cites `[V2G20-2176]` against anyone.
  [`…-d20-price-schedule-audit`](interop-runs/2026-08-11-everest-d20-price-schedule-audit/notes.md).

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

- **Forty-seven issues across six projects** are drafted and unsent in [`reports/`](reports/README.md),
  in thirty-three sendable reports. Each ends with a *Before sending* checklist whose unticked items are
  the parts only a person can do. This is the largest single block of finished work waiting on a human,
  and by a wide margin: *Ours to fix* is empty, *Open questions about our own stack* has none, and
  *Untested, nothing stopping us* holds two runs that between them are an afternoon.
  <br>**The forty-sixth was written on 2026-08-13** and came out of a question this file had been
  carrying: whether `EvseManager` re-reports a contactor it already knows is closed. It does not — the
  `-20` AC wait is edge-triggered against a level, so a contactor that closed a moment early can never
  be learned about ([`everest-d20-ac-contactor-edge`](reports/everest-d20-ac-contactor-edge.md)). It is
  the second half of the shape [`everest-d20-eim-rejection`](reports/everest-d20-eim-rejection.md) found,
  and [`sending-order.md`](reports/sending-order.md) now pairs them as dependency 5.
  <br>**The forty-seventh was written on 2026-08-14**, and it is the first filing here about a
  requirement that is not an error criterion. Their `PyEvJosev` paces the AC charge loop at ≈532 ms
  where Table 216 gives the EVCC **0,25 s** (`[V2G20-1499]`) and the SECC a 0,5 s error criterion
  (`[V2G20-1500]`, `[V2G20-443]`) — measured 2 of 2 against our own conformant timer, and written **as
  a performance deviation, not a violated timeout**, because Figure 212's legend sorts the two
  thresholds on that interval into different kinds and a report that blurs them is refutable in one
  sentence ([`josev-iso20-evcc-charge-loop-pacing`](reports/josev-iso20-evcc-charge-loop-pacing.md)).
  Both target trees were checked at HEAD the same day and are still on the revisions the reading was
  taken from, so it may honestly be one upstream issue that the fork inherits rather than two filings —
  which its checklist leaves as a decision. It pairs with
  [`everest-d20-sequence-timeout`](reports/everest-d20-sequence-timeout.md): that station's flattened
  60 s is exactly why nobody there has noticed this EV.
  <br>**What the writing added to the measurement**, and it is the part that would have been missed by
  filing on 08-13: the ≈532 ms is **not** a charge-loop pacing decision. The same log times the setup
  phase at ≈573–600 ms per exchange, so it is the session's per-message cost and the charge loop is
  merely the one phase whose budget is tight enough to notice. Where that cost goes was **not**
  localized, and the report says so in its own section rather than in a footnote.
  <br>**[`reports/sending-order.md`](reports/sending-order.md)** now says in what order, and why: a
  crash first, then small measured fixes to buy the attention the hard ones need, the five orderings
  that would waste the work if reversed, and eVDriveFlow last as patches because nobody is there to
  answer an issue.
  <br>**The mechanical half of those checklists was cleared on 2026-08-11**
  ([audit](interop-runs/2026-08-11-reports-upstream-audit/notes.md),
  [tools](../tools/reports-audit/README.md)): all 189 `file:line` citations re-verified, and every
  finding re-tested against its project's current default branch. **189 was the count that day; it is
  242 now** — the forty-fifth report added its own, and configuring `TREE_TUX_NET` on 2026-08-13
  finally resolved the one that had been unresolved since the day it was written
  ([`tux-evse-spin`](reports/tux-evse-spin.md)'s central citation, which turned out to be correct). The
  standing state is **227 resolved, 15 ambiguous, 0 unresolved**; the ambiguous ones are basenames that
  exist in two trees and the reports name the tree in prose. Re-run it before the first filing goes out,
  not because it is due but because `main` moves. Five of the six counterparties have
  not committed since the drafts were written. everest-core has — and **three findings are now fixed
  there**, so two of them are retired and the third
  ([loop-shutdown](reports/everest-loop-shutdown.md)) is re-pitched and **rewritten**: its trigger is
  fixed, its structure — any throw out of `poll()` ending the accept loop — is not, and reading
  `main` for the rewrite turned up **eight sites that still reach it** plus a second fix of theirs
  carrying the comment `// FIXME (aw): we should not die here immediately`. They still show as defects in
  standalone `EVerest/libiso15118`, and **that repository is not maintained**; everest-core's
  `lib/everest/iso15118/` is the only tree that decides.
  Two more needed their argument rewritten and **were rewritten on 2026-08-11**:
  [ocsp-absent](reports/everest-d20-ocsp-absent.md) rested on three absences of which `main` has filled
  two, so the ask is now three ordered one-line changes rather than "implement OCSP";
  [client-auth](reports/everest-d20-client-auth.md) §1's TLS 1.2 path turns out to be deliberate,
  documented, and *correct in the library that serves both protocols* — the defect is a `-20`-only
  module opting into it, so the fix is one line at the call site. Neither measurement changed; both
  reports got smaller. **The rest of every checklist is judgement, and stays with a person.**
- ~~**The eighteenth needs one thing that is ours:** the contactor report has never been seen happen.~~
  **Done 2026-08-09**, hours after it was written: `ac_contactor_closed(false)` published on their own
  interface inside the 3 s window, `PowerDeliveryRes(OK)` ~95 ms later and three charge loops after
  that, 2 of 2, against a control that fails at 3.000 s
  ([run notes](interop-runs/2026-08-09-everest-ac-contactor-injection/notes.md)). It did **not** buy
  the two AC matrix cells, which was the hope — and the reason given here for four days was wrong.
  <br>**Bought on 2026-08-13, and not by more capability.** The injection was not "not the same
  capability as driving their EV-side hardware"; driving their EV-side hardware was never what was
  missing. Their contactor confirmation was already being produced — 4,948 s too early, in a state that
  does not read `ClosedContactor` — and the whole wall was that `libiso15118` remembers nothing that
  arrived before the 3 s window opened. Moving the car's CP command *into* the window bought both cells,
  five sessions, nothing injected
  ([run notes](interop-runs/2026-08-13-everest-d20-ac-contactor-window/notes.md)). The `cphold` control
  still walls, which is now the evidence rather than the obstacle.
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
