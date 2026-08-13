# In what order to send them

Thirty-one sendable reports carrying **forty-five issues** across six projects, and none of them sent. The
[index](README.md) lists them by counterparty, which is how you find one. This file is how you work
through them.

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

And one **anti**-dependency: [`everest-d20-rng-entropy`](everest-d20-rng-entropy.md) and
[`evdriveflow-session-id-entropy`](evdriveflow-session-id-entropy.md) are the same requirement in two
projects with **entirely different causes**. Do not write them as one thought, and do not send them the
same day — you will blur them.

---

## The order

### First — the one that should not wait

**1.** [`everest-evsev2g-paymentdetails-crash`](everest-evsev2g-paymentdetails-crash.md) — EVerest

A malformed contract certificate in `PaymentDetailsReq` crashes the V2G module, **pre-authentication**.
One reordered check fixes it. It is the only filing in this directory where the delay has a cost, and
its checklist has *"put it on a running station"* still open — send it anyway with the caveat rather
than sit on a crash. Report it the way a crash should be reported.

### Then — small, measured, and to a project that will act

Three issues, three different projects, over a week or two. Each is a one-line or one-value fix with a
measurement behind it, and each buys the credit that the harder ones later will need.

**2.** [`everest-evsev2g-session-id-zero`](everest-evsev2g-session-id-zero.md) — one deletion. Their own
DIN twin, their own `-20` module and their own DIN test all get it right; that is the friendliest
possible framing and it is true.

**3.** [`everest-d20-sequence-timeout`](everest-d20-sequence-timeout.md) — one constant. Measured to
60,0025 s against their own log.

**4.** [`josev-iso20-charge-loop-timeout`](josev-iso20-charge-loop-timeout.md) — SwitchEV. The
constants already exist in their file and are referenced nowhere. **Send this within days of (3)**:
same defect, two independent stacks, and each issue is more credible for the other existing. It is
source-only and says so; the measurement is on EVerest.

### Then — client auth, in its own order

**5.** client-auth [issue 1](everest-d20-client-auth/issue-1-client-auth-decided-by-the-ev.md) — one
line at the call site, costs no conformant interop.
**6.** client-auth [issue 3](everest-d20-client-auth/issue-3-server-chain-selection.md) — the strongest
evidence in the set.
**7.** client-auth [issue 2](everest-d20-client-auth/issue-2-certificaterequest-contents.md) — the
conformance profile.

Their [README](everest-d20-client-auth/README.md) explains why three and why this order.

### Then — the one the client-auth reader is already in the right code for

**8.** [`everest-d20-eim-rejection`](everest-d20-eim-rejection.md) — a rejected EIM authorization never
reaches `Evse15118D20`, because `EvseManager` forwards verdicts for Plug & Charge only. Their station
answers `Ongoing` for 180 s and then the wrong code, where `[V2G20-2230]` allows 1,5 s and names
`WARNING_EIMAuthorizationFailure` — a branch their own `-20` library has and cites.

Placed here on **code locality**, which is the one thing that beats the batching rule: 5–7 are all
`Evse15118D20`/`libiso15118` authorization, and so is this. It does stretch rule 3 to four in a row for
one project — send it a clear week after (7), or move it behind the OCSP chain if the client-auth
conversation is still open.

**Lead with the `-2` control, not with the defect.** The report's value over "you forgot to forward EIM"
is that `[V2G2-854]` requires the opposite for ISO 15118-2, and `EvseV2G` gets it right — so the obvious
fix regresses the other stack. A maintainer who reads that first will read the rest differently.

### Then — the OCSP chain, in the only order that works

**9.** [`everest-evse-security-ocsp-dropped`](everest-evse-security-ocsp-dropped.md) — the dropped
struct member, measured off their own MQTT reply.
**10.** [`everest-d20-ocsp-absent`](everest-d20-ocsp-absent.md) — and lead it with **their own log
line**, `<n> certificates != <n> OCSP responses`, which we measured firing once per TLS session. That
is the shortest route into the issue and it is their record, not our reading.

### Then — the two entropy findings, apart

**11.** [`everest-d20-rng-entropy`](everest-d20-rng-entropy.md) — 49 of 49 SessionIDs recovered from a
32-bit seed space. This is the strongest measurement in the whole directory and it deserves its own
week.

**12.** [`evdriveflow-session-id-entropy`](evdriveflow-session-id-entropy.md) — **as a PR**, and see
the dormant-project section. Not the same week as (11).

### Then — the bigger arguments, one at a time

These need a maintainer with attention, which is what the first ten were for.

**13.** [`everest-loop-shutdown`](everest-loop-shutdown.md) — re-pitched around the loop rather than the
handshake, citing two of their own fixes as agreement. Do not lead with the TLS handshake; it is fixed.
**14.** [`everest-isomux`](everest-isomux.md) — four issues in one module. Decide the shape first: one
issue with four headings or four issues. §1 and §2 have different answers available, which argues for
splitting, and the report says so.
**15.** [`everest-d20-trust-anchor`](everest-d20-trust-anchor.md) — needs a change outside
`libiso15118` (`CaCertificateType` has no `OEM`), so it is a conversation, not a patch.
**16.** [`libcbv2g-grammar-deviations`](libcbv2g-grammar-deviations.md) — three grammars in one
generator. **Lead with C**: three types no caller can encode is the one finding here that is not a
difference of opinion. A is written as a question on purpose.

### Then — the rest of EVerest, in no particular order among themselves

**17.** [`everest-evsev2g-metering-chain`](everest-evsev2g-metering-chain.md) — two issues, different
severities, file separately.
**18.** [`everest-evsev2g-renegotiation-cablecheck`](everest-evsev2g-renegotiation-cablecheck.md) —
**carries the `-2` document caveat harder than any other**: every quotation is from the 2022 DIS.
Re-read the 2014 wording before posting. That is a gate, not a footnote.
**19.** [`everest-d20-meter-info`](everest-d20-meter-info.md)
**20.** [`everest-evsev2g-session-log-responses`](everest-evsev2g-session-log-responses.md)
**21.** [`everest-evsev2g-certificate-update`](everest-evsev2g-certificate-update.md) — source only, and
its own checklist wants it on the wire first.
**22.** [`pyevjosev-manifest-services`](pyevjosev-manifest-services.md) — one line, and the smallest
thing in the directory. Good filler between two hard ones.

### Then — SwitchEV, what is left

**23.** [`josev-iso20-renegotiation`](josev-iso20-renegotiation.md) — §1 to both trees, §2 upstream
only, and say in the upstream issue that the fork already carries §2's fix.
**24.** [`josev-iso20-pki-curve`](josev-iso20-pki-curve.md) — fork first, then upstream, per dependency 4.
**25.** [`josev-iso20-pause-resume`](josev-iso20-pause-resume.md)

### Then — tux-evse and V2Gdecoder

Both semi-dormant but not dead. Issues, not PRs, and expect a slow answer.

**26.** [`tux-evse-spin`](tux-evse-spin.md) — C and D. The only tux finding that was *measured*, and the
reproduction is one line: one connection, one message, disconnect.
**27.** [`tux-evse-tls`](tux-evse-tls.md) — A and B, separately; A is a bug, B is a question.
**28.** [`tux-evse-capture-fidelity`](tux-evse-capture-fidelity.md) — E and F; F is the one-liner a
maintainer merges in a minute, so consider sending F first.
**29.** [`v2gdecoder-fuzzy-grammar`](v2gdecoder-fuzzy-grammar.md) — A and B. **Lead with what worked**:
285 of 287 frames round-tripped.

### Last — eVDriveFlow, as pull requests

`main` has not moved since **2023-04-17** — three years and four months. Six issues, and an issue is a
question nobody is there to answer. **Send patches instead**, in one batch, and expect nothing back.

**30.** issue **1** (`stdin` EOF) — the wall everything else is behind.
**31.** issue **2** (`hasattr` on an `Optional`) — only makes sense after 1.
**32.** issue **3** (`SupportedServiceIDs` dereferenced unconditionally) — independent of both, and the
one that needs no misbehaviour from the other side at all.
**33.** issue **4** (PnC alongside EIM raises `NotImplementedError`).
**34.** issue **5** (`[V2G20-460]` unimplemented — fifteen handlers, none compares).
**35.** issue **6** (26,6 bits where 58 are required) — **and it buys nothing until 5 is fixed**; say so,
as the report does.

---

## What is deliberately not in this order

- **The two withdrawn reports.** [`everest-iso20-ac-contactor-latch`](everest-iso20-ac-contactor-latch.md)
  and [`everest-d20-ac-namespace`](everest-d20-ac-namespace.md) are fixed on `main` and are not sent at
  all. They stay in the directory because the runs behind them are facts about `2026.02.1`.
- **Anything that needs a decision only you can make.** Several entries above have one — whether to
  include §1's `2026.02.1` replay, what shape `IsoMux` takes, whether the `-2` caveat survives a look at
  the 2014 text. Those are gates on individual filings, not on the order.

## Before the first one goes out

Re-run [`tools/reports-audit/`](../../tools/reports-audit/README.md). It takes two minutes and it has
already caught, in one week: three findings fixed upstream before they were sent, two whose argument
had been overtaken, and a dead mirror that would have received three filings nobody would have read.
**`main` moved three times while these were being written.**
