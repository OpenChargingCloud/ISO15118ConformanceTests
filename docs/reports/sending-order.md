# In what order to send them

Thirty-three sendable reports carrying **forty-seven issues** across six projects, and none of them sent. The
[index](README.md) lists them by counterparty, which is how you find one. This file is how you work
through them.

**As of 2026-08-15 not one of them is waiting on a measurement.** The last open box was (37)'s *"run it
against your station"*, taken that evening; the only report still marked source-only,
[`everest-evsev2g-certificate-update`](everest-evsev2g-certificate-update.md), is walled off by their own
advertisement rather than by work nobody has done, and its checklist says so. Everything left in this
directory is a decision a person makes.

It is a suggestion, not a queue to execute. The rules are given first so you can re-derive the order
when something changes — and something will: `main` moves, projects wake up, and the first reply to any
of these changes what the next one should be.

## The rules that produced this

1. **A crash goes first.** Everything else can wait a week; a remotely triggerable one cannot.
2. **Send the smallest fix first to a project you have not written to before.** A maintainer who has
   just merged a one-liner from you reads the next one. This is about being heard, and it is not the
   same as severity — where the two conflict, both orders are given.
3. **Do not batch more than two or three at once per project.** Twenty-two issues to EVerest in a week
   is not diligence, it is noise, and the twenty-third gets skimmed.
4. **Respect the dependencies.** Four of these only make sense in a particular order, and two would be
   *wrong* out of it. Listed below.
5. **Measured before source-only.** A report that observed the behaviour lands differently from one
   that read the code. Where a source-only report has *"run it first"* as an open item, that item is
   the gate, not the ordering.
6. **A dormant project gets a PR, not an issue, and it goes last.** Nobody is waiting; a patch someone
   can merge in a year is worth more than a question nobody will answer.

## Hard dependencies — get these wrong and the work is wasted

| | before | after | why |
|---|---|---|---|
| **1** | [`everest-evse-security-ocsp-dropped`](everest-evse-security-ocsp-dropped.md) | [`everest-d20-ocsp-absent`](everest-d20-ocsp-absent.md) | Neither fix alone produces a staple. Landing the second first invites *"we already fixed OCSP"* and closes both. |
| **2** | client-auth [issue 1](everest-d20-client-auth/issue-1-client-auth-decided-by-the-ev.md) | [issue 3](everest-d20-client-auth/issue-3-server-chain-selection.md), then [issue 2](everest-d20-client-auth/issue-2-certificaterequest-contents.md) | Issue 1 has an answer available that would close its framing without touching the others; send it while it is still its own conversation. |
| **3** | eVDriveFlow issue **1** (`stdin`) | issue **2** | Fixing 1 is what reveals 2. Report 2 reads as a duplicate until 1 is understood. |
| **4** | [`josev-iso20-pki-curve`](josev-iso20-pki-curve.md) → `ext-switchev-iso15118` | → `SwitchEV/iso15118` | Same file in two live trees; the fork is the one that will move, and the upstream issue can then cite it. |
| **5** | [`everest-d20-eim-rejection`](everest-d20-eim-rejection.md) | [`everest-d20-ac-contactor-edge`](everest-d20-ac-contactor-edge.md) | Both say *"`EvseManager` does not tell the `-20` stack something it knows"*, and both are answered by a change on that side. Sent together they are one argument with two instances; sent in the other order the second reads as a rehash of the first. |

And one **anti**-dependency: [`everest-d20-rng-entropy`](everest-d20-rng-entropy.md) and
[`evdriveflow-session-id-entropy`](evdriveflow-session-id-entropy.md) are the same requirement in two
projects with **entirely different causes**. Do not write them as one thought, and do not send them the
same day — you will blur them.

---

## The order

### First — the one that should not wait

**1.** [`everest-evsev2g-paymentdetails-crash`](everest-evsev2g-paymentdetails-crash.md) — EVerest

A malformed contract certificate in `PaymentDetailsReq` crashes the V2G module, **pre-authentication**.
One reordered check fixes it. It is the only filing in this directory where the delay has a cost.

**Its last technical box was ticked on 2026-08-15**: reproduced twice on a running 2026.02.1 station,
SIGABRT from a peer that presented no client certificate, with a control arm and a liveness arm — and
the liveness arm is what upgraded the consequence from *"the module dies"* to *"the manager terminates
every module and exits"*. So the caveat this entry used to carry is gone, and **the only thing left
between this report and a maintainer is a person deciding how to send it**. Report it the way a crash
should be reported: their security policy first, not a public issue.

### Then — small, measured, and to a project that will act

Four issues, two projects — two and two — over a week or two. Each is a one-line or one-value fix with a
measurement behind it, and each buys the credit that the harder ones later will need. (4) and (5) are a
pair and are the reason this section is no longer three: they are the two halves of one table, and the
second is the one that was measured.

**2.** [`everest-evsev2g-session-id-zero`](everest-evsev2g-session-id-zero.md) — one deletion. Their own
DIN twin, their own `-20` module and their own DIN test all get it right; that is the friendliest
possible framing and it is true.

**3.** [`everest-d20-sequence-timeout`](everest-d20-sequence-timeout.md) — one constant. Measured to
60,0025 s (DC) and 60,0160 s (AC) against their own log, one control each. **Its last technical box was
ticked on 2026-08-15**: the AC arm was run so that Table 216's row is a measurement rather than an
inference from Table 217's, and the two agree to 14 ms. Nothing left but posting it.

**4.** [`josev-iso20-charge-loop-timeout`](josev-iso20-charge-loop-timeout.md) — SwitchEV. The
constants already exist in their file and are referenced nowhere. **Send this within days of (3)**:
same defect, two independent stacks, and each issue is more credible for the other existing. **No longer
source-only — run 2026-08-15**, four arms against their own SECC, and their log names the value it
waited on. Lead with that line rather than with ours.

**5.** [`josev-iso20-evcc-charge-loop-pacing`](josev-iso20-evcc-charge-loop-pacing.md) — **EVerest, since
2026-08-15**, and it used to be here as SwitchEV's. Their EV turns the charge loop around in ≈532 ms
where Table 216 gives it 0,25 s. The localization moved it: the codec costs ~30 ms per direction and
Josev's own EVCC does the same loop in **43 ms**, so the half second belongs to `PyEvJosev` rather than
to the stack it wraps.

**This breaks its pairing with (4)** and that is worth knowing before either goes out. They are still
the station half and the car half of the same two table rows, and §7 is still the argument that shipping
both together is what makes either invisible — but they are now **two projects**, so "send them
together" means two issues in two trackers on the same day, not a pair to one maintainer. The one
sentence that stayed SwitchEV's — the absent `V2G_EVCC_SEQUENCE_PERFORMANCE_TIME` — travels with (4)
instead, which is the tidier home for it anyway.

**Say what it is not.** It is a *performance* criterion, not a timeout anyone may abort on — Figure
212's legend sorts the two thresholds on that interval differently — and the report leads with that
distinction. Posting it as "your EV violates a 0,25 s timeout" is the one framing that gets a correct
finding closed in a sentence.

### Then — client auth, in its own order

**6.** client-auth [issue 1](everest-d20-client-auth/issue-1-client-auth-decided-by-the-ev.md) — one
line at the call site, costs no conformant interop.
**7.** client-auth [issue 3](everest-d20-client-auth/issue-3-server-chain-selection.md) — the strongest
evidence in the set.
**8.** client-auth [issue 2](everest-d20-client-auth/issue-2-certificaterequest-contents.md) — the
conformance profile.

Their [README](everest-d20-client-auth/README.md) explains why three and why this order.

### Then — the one the client-auth reader is already in the right code for

**9.** [`everest-d20-eim-rejection`](everest-d20-eim-rejection.md) — a rejected EIM authorization never
reaches `Evse15118D20`, because `EvseManager` forwards verdicts for Plug & Charge only. Their station
answers `Ongoing` for 180 s and then the wrong code, where `[V2G20-2230]` allows 1,5 s and names
`WARNING_EIMAuthorizationFailure` — a branch their own `-20` library has and cites.

Placed here on **code locality**, which is the one thing that beats the batching rule: 6–8 are all
`Evse15118D20`/`libiso15118` authorization, and so is this. It does stretch rule 3 to four in a row for
one project — send it a clear week after (8), or move it behind the OCSP chain if the client-auth
conversation is still open.

**Lead with the `-2` control, not with the defect.** The report's value over "you forgot to forward EIM"
is that `[V2G2-854]` requires the opposite for ISO 15118-2, and `EvseV2G` gets it right — so the obvious
fix regresses the other stack. A maintainer who reads that first will read the rest differently.

**10.** [`everest-d20-ac-contactor-edge`](everest-d20-ac-contactor-edge.md) — the `-20` AC contactor wait
is edge-triggered against a level `EvseManager` already holds, so a contactor that closed a moment early
can never be learned about and the station answers `FAILED_ContactorError` on a closed contactor.

**Immediately after (9), per dependency 5**, and in the same breath: both are *"`EvseManager` does not
tell the `-20` stack something it knows"*, both are fixed on that side, and the pair is a stronger
argument than either alone — one is a verdict never forwarded, the other a state never re-reported.
Sent apart, this one reads as a rehash.

Two things to say explicitly, both already in the draft. **It is not the withdrawn latch report** — a
maintainer who remembers the missing `*` will assume it is. And **it makes no severity claim**: we have
not shown a real EV produces the early ordering, only that the code cannot recover from it. Leading with
"your charger fails to charge" would be claiming the half we did not measure.

### Then — the OCSP chain, in the only order that works

**11.** [`everest-evse-security-ocsp-dropped`](everest-evse-security-ocsp-dropped.md) — the dropped
struct member, measured off their own MQTT reply.
**12.** [`everest-d20-ocsp-absent`](everest-d20-ocsp-absent.md) — and lead it with **their own log
line**, `<n> certificates != <n> OCSP responses`, which we measured firing once per TLS session. That
is the shortest route into the issue and it is their record, not our reading.

### Then — the two entropy findings, apart

**13.** [`everest-d20-rng-entropy`](everest-d20-rng-entropy.md) — 49 of 49 SessionIDs recovered from a
32-bit seed space. This is the strongest measurement in the whole directory and it deserves its own
week.

**14.** [`evdriveflow-session-id-entropy`](evdriveflow-session-id-entropy.md) — **as a PR**, and see
the dormant-project section. Not the same week as (13). **On the wire since 2026-08-15**: 24 of 24
SessionIDs eight ASCII digits, from their own station, read out of the frames with no decoder — which
also means the PR can quote a measurement rather than only their docstring. Its `[V2G20-460]` sibling
still has that box open, and the rig is now written down for it.

### Then — the bigger arguments, one at a time

These need a maintainer with attention, which is what the first fourteen were for.

**15.** [`everest-loop-shutdown`](everest-loop-shutdown.md) — re-pitched around the loop rather than the
handshake, citing two of their own fixes as agreement. Do not lead with the TLS handshake; it is fixed.
**16.** [`everest-isomux`](everest-isomux.md) — four issues in one module. Decide the shape first: one
issue with four headings or four issues. §1 and §2 have different answers available, which argues for
splitting, and the report says so. **One argument against splitting arrived 2026-08-15**: §2 and §4 turn
out to be **coupled** — while the TLS 1.2 cap stands, no client can exercise `trusted_ca_keys` at all,
so §4 is unobservable from outside until §2 moves. Two issues that must be read together are a case for
one issue with headings.
**17.** [`everest-d20-trust-anchor`](everest-d20-trust-anchor.md) — needs a change outside
`libiso15118` (`CaCertificateType` has no `OEM`), so it is a conversation, not a patch.
**18.** [`libcbv2g-grammar-deviations`](libcbv2g-grammar-deviations.md) — three grammars in one
generator. **Lead with C**: three types no caller can encode is the one finding here that is not a
difference of opinion. A is written as a question on purpose.

### Then — the rest of EVerest, in no particular order among themselves

**19.** [`everest-evsev2g-metering-chain`](everest-evsev2g-metering-chain.md) — two issues, different
severities, file separately.
**20.** ~~[`everest-evsev2g-renegotiation-cablecheck`](everest-evsev2g-renegotiation-cablecheck.md)~~ —
**withdrawn 2026-08-15, by this very entry.** It said the `-2` document caveat was *a gate, not a
footnote*; the gate was worked and the report did not survive it. Their station's
`FAILED_SequenceError` is correct — the DC state table admits `CableCheckReq` after
`ChargeParameterDiscoveryReq` and nothing else, in both editions — and the Annex I sequence the report
leaned on is the **AC** one. The defect is ours and is in `open-work.md`.
<br>**Nothing else in this order inherits the mistake**, checked two ways. It was the only report in the
directory that reproduced ISO prose rather than paraphrasing it — which is what
[`normative-basis.md`](../normative-basis.md) forbids, and the sweep found no second instance. And it is
the only one leaning on an annex **sequence diagram**; the one other annex citation here,
[`josev-iso20-vehicle-certificate`](josev-iso20-vehicle-certificate.md) on `-20` Annex B, is about
certificate profiles, where there is no AC/DC split to get wrong. Both facts point the same way, and
that is the useful part — a filing that quotes the standard at length is a filing that has stopped
checking the standard.
**21.** [`everest-d20-meter-info`](everest-d20-meter-info.md)
**22.** [`everest-evsev2g-session-log-responses`](everest-evsev2g-session-log-responses.md)
**23.** [`everest-evsev2g-certificate-update`](everest-evsev2g-certificate-update.md) — source only, and
its own checklist wants it on the wire first.
**24.** [`pyevjosev-manifest-services`](pyevjosev-manifest-services.md) — one line, and the smallest
thing in the directory. Good filler between two hard ones.

### Then — SwitchEV, what is left

**25.** [`josev-iso20-renegotiation`](josev-iso20-renegotiation.md) — §1 to both trees, §2 upstream
only, and say in the upstream issue that the fork already carries §2's fix.
**26.** [`josev-iso20-pki-curve`](josev-iso20-pki-curve.md) — fork first, then upstream, per dependency 4.
**27.** [`josev-iso20-vehicle-certificate`](josev-iso20-vehicle-certificate.md) — **upstream only, and
send it beside 26**: same file, same reviewer, and the vehicle branch this asks for is a good moment to
fix the curves that one is about. Unusually easy to accept — the fork already carries the whole change,
so lead with that rather than with the two requirement numbers.
**28.** [`josev-iso20-pause-resume`](josev-iso20-pause-resume.md)

### Then — tux-evse and V2Gdecoder

Both semi-dormant but not dead. Issues, not PRs, and expect a slow answer.

**29.** [`tux-evse-spin`](tux-evse-spin.md) — C and D. The only tux finding that was *measured*, and the
reproduction is one line: one connection, one message, disconnect.
**30.** [`tux-evse-tls`](tux-evse-tls.md) — A and B, separately; A is a bug, B is a question.
**31.** [`tux-evse-capture-fidelity`](tux-evse-capture-fidelity.md) — E and F; F is the one-liner a
maintainer merges in a minute, so consider sending F first.
**32.** [`v2gdecoder-fuzzy-grammar`](v2gdecoder-fuzzy-grammar.md) — A and B. **Lead with what worked**:
285 of 287 frames round-tripped.

### Last — eVDriveFlow, as pull requests

`main` has not moved since **2023-04-17** — three years and four months. Six issues, and an issue is a
question nobody is there to answer. **Send patches instead**, in one batch, and expect nothing back.

**33.** issue **1** (`stdin` EOF) — the wall everything else is behind.
**34.** issue **2** (`hasattr` on an `Optional`) — only makes sense after 1.
**35.** issue **3** (`SupportedServiceIDs` dereferenced unconditionally) — independent of both, and the
one that needs no misbehaviour from the other side at all. **It got stronger on 2026-08-15 in two ways.**
Their station turns out to work: with the element present it runs the whole DC sequence to
`PowerDelivery`, so line 31 is a wall in front of a station that otherwise charges — which is a better
pitch than a traceback. And the same unguarded-optional read is in the **charge loop** as well
(`process_dc_charge_loop_request.py:114`, `DisplayParameters`), reached by a car that has done everything
right. That second site is in the same report rather than a separate filing; if it is split when posted,
the count goes to 49 and the argument does not change.
**36.** issue **4** (PnC alongside EIM raises `NotImplementedError`).
**37.** issue **5** (`[V2G20-460]` unimplemented — fifteen handlers, none compares). **No longer
source-only, and it is the strongest of the six**: measured 2026-08-15, **ten message types answered
`OK` under a SessionID their station never issued**, `PowerDelivery` among them, in sessions identical to
the control message for message — with their own debug log printing the foreign id three lines above the
answer that ignores it. Lead with that line. It is also the last measurement box that was open anywhere
in this directory.
**38.** issue **6** (26,6 bits where 58 are required) — **and it buys nothing until 5 is fixed**; say so,
as the report does.

---

## What is deliberately not in this order

- **The three withdrawn reports.** [`everest-iso20-ac-contactor-latch`](everest-iso20-ac-contactor-latch.md)
  and [`everest-d20-ac-namespace`](everest-d20-ac-namespace.md) are fixed on `main` and are not sent at
  all. They stay in the directory because the runs behind them are facts about `2026.02.1`.
  [`everest-evsev2g-renegotiation-cablecheck`](everest-evsev2g-renegotiation-cablecheck.md) is the third
  and the only one withdrawn because it was **wrong** rather than overtaken; it stays for the same
  reason and for one more, that its four citation errors are a better checklist than any list of rules.
- **Anything that needs a decision only you can make.** Several entries above have one — whether to
  include §1's `2026.02.1` replay, what shape `IsoMux` takes, whether the `-2` caveat survives a look at
  the 2014 text. Those are gates on individual filings, not on the order.

## Before the first one goes out

Re-run [`tools/reports-audit/`](../../tools/reports-audit/README.md). It takes two minutes and it has
already caught, in one week: three findings fixed upstream before they were sent, two whose argument
had been overtaken, and a dead mirror that would have received three filings nobody would have read.
**`main` moved three times while these were being written.**
