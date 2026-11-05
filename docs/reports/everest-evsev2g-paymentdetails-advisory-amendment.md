# Draft amendment to a published EVerest advisory — GHSA-qg7p-grpm-cg3f understates its own consequence

Status: **draft, not sent.** This is **not a new finding.** The defect is
[GHSA-qg7p-grpm-cg3f](https://github.com/EVerest/EVerest/security/advisories/GHSA-qg7p-grpm-cg3f)
(medium, CVSS:3.1 6.5, credited to `Mehrrun`, published 2026-11-05), and it is **fixed** — by their
PR #3047, merged 2026-10-27, released in **2026.02.2** on 2026-11-05. What this document asks for is a
correction to the advisory's *description*, to one of its two patched versions, and a CVSS 4.0 vector,
on the strength of three measurements against running stations that the advisory does not have. Post it
under your own name; see *Before sending*.

We held the same finding as an unsent draft since 2026-08-11
([`everest-evsev2g-paymentdetails-crash`](everest-evsev2g-paymentdetails-crash.md)). That is context for
where the logs come from, **not** a claim on the credit — `Mehrrun` reported it, it is theirs.

Evidence in this repository:
[`2026-08-15-everest-paymentdetails-crash-live`](../interop-runs/2026-08-15-everest-paymentdetails-crash-live/notes.md)
(two runs on 2026.02.1) and
[`2026-11-05-everest-rc-paymentdetails-crash`](../interop-runs/2026-11-05-everest-rc-paymentdetails-crash/notes.md)
(the A/B: `2026.10.0-rc1` against `main`, same probe, same machine, forty minutes apart), and
[`2026-11-05-everest-advisory-cross-check`](../interop-runs/2026-11-05-everest-advisory-cross-check/notes.md)
— all twenty-four EVerest reports against their seventy-two published advisories and the
2026.02.2 tag, which is where the release-list and advisory claims above were checked.

---

**Title:** GHSA-qg7p-grpm-cg3f is scored and described as a module-level charging interruption; on a
running station one frame from an unauthenticated peer ends **every** module and the station does not
come back

## What the advisory already has right, and why this is short

Everything technical. The ordering defect in `handle_iso_payment_details`, the null `contract_crt`
reaching `certificate_subject`, the `assert`-only guard on `conn->pubkey` that disappears under
`-DNDEBUG`, CWE-476, and the affected ranges. The fix is right too, and in both halves: the `err != 0`
test now precedes `getEmaidFromContractCert`, and the pubkey slot is an `if` rather than an `assert`.
Nothing below disputes any of that.

Four of the five CVSS 3.1 metrics are also, in our reading, at the correct and most severe value
already: `AV:A` (the peer is on the charging cable or the same L2 — the same access vector the rest of
the 2026-11-05 batch uses for ISO 15118 findings), `AC:L`, `PR:N`, `UI:N`, `A:H`. **6.5 is the
arithmetic ceiling** for an availability-only finding with `AV:A` and `S:U`; we are not asking for a
bigger number by re-rating what is already maxed.

## The one sentence we would change

> *"This is a null dereference (CWE-476) that stops charging."*

Measured, it is not the charge that stops. It is the station. On 2026-08-15, everest-core **2026.02.1**
(`b61bb12`), your `-2` Plug & Charge SIL config with one line changed (`tls_security: allow` →
`force`), twice, identical but for timestamps and pid — your own log:

```
13:48:46  [INFO] iso15118_charge :: SelectedPaymentOption: Contract
          iso15118_charger:EvseV2G: …/lib/everest/tls/src/openssl_util.cpp:775:
              openssl::certificate_subject(const X509*): Assertion `cert != nullptr' failed.
13:48:47  [CRIT] manager :: Module iso15118_charger (pid: 2802) exited with status: 134.
                            Terminating all modules.
13:48:47  [CRIT] manager :: Exiting manager.
```

And on **`2026.10.0-rc1`** (`f45ba0d0`) on 2026-11-05, where the shutdown machinery is new and the
outcome is not:

```
16:33:22  [INFO] iso15118_charge  :: SelectedPaymentOption: Contract
          iso15118_charger:EvseV2G: …/lib/everest/tls/src/openssl_util.cpp:799:
              openssl::certificate_subject(const X509*): Assertion `cert != nullptr' failed.
16:33:23  [CRIT] manager :: Module iso15118_charger exited unexpectedly, terminating remaining modules...
16:33:23  [WARN] manager :: Crash recovery shutdown completed in 63 ms. Modules that did not shut down
                            cleanly:  iso15118_charger (signal=6) api (signal=15) ...
16:33:23  [CRIT] manager :: Unexpected module exit; manager is exiting.
```

So the blast radius is the whole module set — `api` among them, and with it the OCPP link and any
second connector — and **there is no recovery**: a liveness arm three seconds later, sending the
*control* certificate that the same station had just answered `OK`, got `Connection refused`. Both
runs, both versions.

**The three arms, and why the first and third carry the claim.** The only variable between arm 1 and
arm 2 is whether the bytes parse:

| arm | `ContractSignatureCertChain.Certificate` | 2026.02.1 / `2026.10.0-rc1` | `main` (`01f36de1`) |
|---|---|---|---|
| **control** | a well-formed self-signed P-256 leaf, chaining to nothing | answered `OK`, station up | answered `OK` |
| **probe** | 64 bytes, leading `0xFF` — cannot begin a DER `SEQUENCE` | **SIGABRT**, manager exits | answered **`FAILED_CertChainError`** |
| **liveness** | the control certificate again, 3 s later | **`Connection refused`** | answered `OK` |

Five frames from TCP connect, no credential of any kind: `-2` TLS is unilateral, we presented no
client certificate, and the crash is during parsing — before any signature or chain check. The
advisory's own *"A valid contract chain is not required"* is therefore right, and its PoC note
(*"I did not send this on the wire"*) is the gap these arms close.

**The `main` column is also the part that is good news for you**: it is an end-to-end confirmation
that the fix works against the bytes, not just in review — and `FAILED_CertChainError` is the answer
the message table provides for, which neither Josev nor our own station gets right for the
specifically unparseable case.

## What we would ask for, in order of how much we think it is ours to ask

1. **Replace "stops charging"** with the measured consequence: the module aborts, the manager
   terminates every remaining module and exits, and the station refuses connections until it is
   restarted. An operator triaging thirty-four advisories reads that sentence, not the vector, and
   "stops charging" is the one phrase that makes a 2026.02.1 backport look deferrable.
2. **The `patched: 2026.10.0` row names a version that does not exist yet.** The advisory's affected
   ranges read `< 2026.02.0 → 2026.10.0` and `>= 2026.02.0, < 2026.02.2 → 2026.02.2`. The second is
   released. For the first, the newest 2026.10 artifact is **`2026.10.0-rc1`**, tagged 2026-10-22 —
   **five days before** PR #3047 merged — and it is the station we measured aborting above. So an
   operator on, say, 2025.12.1 who follows the advisory to its named fix arrives at a build that still
   has the defect. Either the row wants a note that 2026.10.0 is not yet released, or that line's
   readers want pointing at 2026.02.2 instead.
3. **Add a CVSS 4.0 vector.** `cvss_v4` is empty on the advisory, and 4.0 has the metric 3.1 is
   missing here — **`R:U`**, recovery requires user intervention. Our reading:
   `CVSS:4.0/AV:A/AC:L/AT:N/PR:N/UI:N/VC:N/VI:N/VA:H/SC:N/SI:N/SA:N` with `R:U`. The supplemental
   metric exists for exactly the behaviour the liveness arm measured; the base score is yours to set.
4. **Reconsider `S:U`, knowing that it is a convention call and not ours to make.** `S:C` on the
   existing vector gives **7.4 (high)**. The argument for it is that the vulnerable component is
   `EvseV2G` while the impacted components are `manager`, `api` and every other module. The argument
   against it is yours and we think it is a fair one: `GHSA-4c89-v794-gr7v` is described as *"crashes
   entire charging station"* and is also `S:U`, reaching 7.5 only through `AV:N`. So your batch is
   internally consistent, and if the answer is "`S:U`, by convention" then only points 1 to 3
   matter. We raise it because the global Advisory Database has **no entry for this GHSA yet** (the
   REST endpoint 404s) and no CVE is assigned, so whatever stands in the advisory now is what will
   propagate.

## What we are not claiming

- **Not a controllable write.** The pointer is null, not attacker-chosen. Availability, not integrity,
  and no path to code execution. The severity argument is entirely about scope and recovery.
- **Not an ISO 15118 violation.** There is no *shall not crash* clause; the code's own intent for this
  case is `FAILED_CertChainError`, which it now sends.
- **Not an exposure claim for the 2026.02 line.** 2026.02.2 carries the fix. What this affects is how
  operators still on 2026.02.0/2026.02.1 — and on anything older, whose named fix version is not out —
  and anyone reading the CVE record later, judge the urgency.
- **Not a credit request.** `Mehrrun` found it independently of us and reported it, which we did not;
  we are offering the station measurements, nothing else.

## Before sending

- [x] **Measure the consequence, not just the crash.** Three arms incl. a control that differs only in
      whether the certificate parses, and a liveness arm after the crash. Twice on 2026.02.1
      (2026-08-15) and once on `2026.10.0-rc1` (2026-11-05).
- [x] **Verify the fix on the wire, not in review.** `main` (`01f36de1`), same probe, same machine,
      forty minutes after the RC run: `FAILED_CertChainError`, station alive, liveness `OK`.
- [x] **Quote their logs, not our summary of them.** Both station logs above are verbatim, including
      the assert line moving `:775` → `:799`.
- [x] **Check both patched versions against their own release list.** `2026.02.2` was released
      2026-11-05 and carries the reordering — read in the tag, not inferred from the notes. There is no
      `2026.10.0`: the newest 2026.10 tag is `2026.10.0-rc1`, a prerelease from 2026-10-22, and it is
      the station that aborted above.
- [x] **Check the advisory's own claims before contradicting any.** The ordering, the pubkey `assert`,
      CWE-476 and the ranges are right; four of five CVSS metrics are at their most severe value and
      6.5 is the 3.1 ceiling for `AV:A` + availability-only. Verified in the 2026.02.2 tag:
      `if (err != 0)` precedes `getEmaidFromContractCert`, and `if (conn->pubkey != nullptr)` replaced
      the assert.
- [x] **Separate what is measured from what is a judgement.** Points 1 to 3 are measurement or a
      checkable fact about their own release list. Point 4 is their convention, and the
      counter-argument from their own batch is stated in the text rather than left for them to find.
- [ ] **Decide the channel.** There is no public comment box for a non-collaborator on a published
      repository advisory (the thread exists — the API reports two comments — but write access to it
      does not come with reading the page), and the global Advisory Database has no entry yet, so its
      *"suggest an improvement"* form does not exist for this GHSA. That leaves: their
      [security policy](https://everest.github.io/nightly/project/governance/security.html) as a
      private follow-up referencing the GHSA id, or asking the publisher (`Pietfried`) to add you as a
      collaborator on the advisory and then posting in its own thread. The first needs no favour and is
      the default.
- [ ] **Decide whether to mention our own draft at all.** The text above does, in one sentence,
      because it explains where three measurements on two versions come from. It is removable without
      touching anything else, and the amendment stands without it.
- [ ] **Post under your own name, in your own words.**
