# Draft report to IoT.bzh (tux-evse) — two things that stop an ISO 15118-2 session over TLS

Status: **draft, not sent.** Found 2026-08-06 against `iso15118-simulator-rs` **`main` @ `fc51088`**
built from source (their `oci-15118/Dockerfile_almalinux_source` recipe, natively, plus
`injector-binding-rs` @ `5fb66e4` which that recipe omits). Post under your own name; see
*Before sending* at the bottom.

**Two separate issues below, filed separately.** Issue A is a defect with a two-line fix that blocks
their own documented TLS quick-start; issue B is a conformance question that may well be a deliberate
choice on their side. Four more — C and D, E and F — are unrelated to TLS and live in
[`tux-evse-spin.md`](tux-evse-spin.md) and [`tux-evse-capture-fidelity.md`](tux-evse-capture-fidelity.md).

Evidence in this repository: [`2026-08-06-tux-tls`](../interop-runs/2026-08-06-tux-tls/notes.md) —
in particular [`their-pair.injector.log`](../interop-runs/2026-08-06-tux-tls/their-pair.injector.log)
(their own injector against their own responder),
[`their-cipher-suites.txt`](../interop-runs/2026-08-06-tux-tls/their-cipher-suites.txt) and
[`pinned-no-shared-cipher.txt`](../interop-runs/2026-08-06-tux-tls/pinned-no-shared-cipher.txt).

---

# Issue A — over TLS, the EVCC signs every `AuthorizationReq`, so EIM scenarios fail with `no_challenge`

**Title:** EVCC signs `AuthorizationReq` whenever a `pki` block is configured, not when the session
selected Contract — the shipped EIM scenarios cannot run over TLS

**Version:** `iso15118-simulator-rs` `main` (`fc51088`), `iso15118-encoders-rs` `fe6c0aa`,
`injector-binding-rs` `5fb66e4`, GnuTLS 3.8.9, Debian 13.

## Summary

Running your own quick-start command from the README —

```bash
binding-start-evcc --pki_tls_sim_dir ./temp/ \
                   --scenario_file /usr/share/iso15118-simulator-rs/audi-dc-iso2-compact.json
```

— the session stops at the fourth transaction:

```
--[pkg:51] Check    iso2:session_setup_req
--[pkg:56] Check    iso2:service_discovery_req
--[pkg:61] Check    iso2:payment_selection_req
--[pkg:68] SimulationStatus::Fail  iso2:authorization_req
           error: {"uid":"iso2-pki-sign-sign","info":"error:no_challenge"}
CRITICAL: binding start fail: unexpected status for uid:pkg:68
```

`audi-dc-iso2-compact.json` selects `"option":"external"` — an EIM session. There is no
`PaymentDetailsReq`, so no `PaymentDetailsRes` ever delivers a `GenChallenge`, so there is nothing for
a signature to echo. The EVCC signs anyway.

## Where it comes from

Two consecutive calls on the send path treat the same missing challenge differently.

`encode_to_stream` **already knows the challenge may be absent** and guards on it:

```rust
// exi-15118/src/net-exi.rs:260-265
MessageTagId::AuthorizationReq => {
    // Add the challenge from the session if needed
    if jsonc.optional::<String>("challenge")?.is_none()
        && session.challenge.len() > 0
    {
        jsonc.add("challenge", &base64_encode(session.challenge.as_ref()))?;
    }
}
```

…and then hands the body to `iso2_encode_payload`, which signs with no such guard:

```rust
// exi-15118/src/net-exi.rs:187-196
if let Some(pki) = self.pki_conf {
    match tag_id {
        MessageTagId::CertificateInstallReq
        | MessageTagId::CertificateUpdateReq
        | MessageTagId::CertificateUpdateRes
        | MessageTagId::AuthorizationReq
        | MessageTagId::MeteringReceiptReq => {
            exi_doc.pki_sign_sign(tag_id, &pki.get_private_key()?)?
        }
        _ => {}
    }
}
```

The condition is *"a `pki` block is configured"*. `session.challenge` is only ever filled from a
`PaymentDetailsRes` (`net-exi.rs:367`), and `binding-simu15118-evcc.yaml` always carries a `pki`
block — it is how `--pki_tls_sim_dir` enables TLS. So over TLS every `AuthorizationReq` is signed,
including in sessions that never authenticated by contract.

## Why we think it is worth fixing rather than configuring around

**It is reachable from your own default configuration, with your own shipped scenario, following your
own README.** We first met it with a third-party SECC on the other end, then reproduced it with
**your responder** — `binding-start-evse` with the same PKI and the same scenario — and the failure is
identical, same transaction, same message. Nothing of ours is implicated:
[`their-pair.injector.log`](../interop-runs/2026-08-06-tux-tls/their-pair.injector.log).

As far as we can tell that means **no scenario you ship currently runs over TLS**: all five are EIM
captures. The plain-TCP path is unaffected, which is presumably why it has not surfaced.

## Suggested fix

The narrow version is one condition, and it is the one your own code two functions earlier already
uses — sign only when there is something to sign against:

```rust
MessageTagId::AuthorizationReq if session.challenge.len() > 0 => { … }
```

The semantically precise version is to key it on the selected payment option (Contract vs
ExternalPayment) rather than on the challenge being present; that is your call, and it may matter for
`MeteringReceiptReq`, which has the same shape. Either way the asymmetry between the two calls is
what we would fix: right now one of them tolerates a missing challenge and the next one does not.

---

# Issue B — the shipped TLS profile contains neither cipher suite ISO 15118-2 prescribes

**Title:** `SECURE128` priority string omits `TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA256` /
`TLS_ECDH_ECDSA_WITH_AES_128_CBC_SHA256`, so a profile-conformant SECC and your EVCC share no cipher

**Version:** as above.

## Summary

Both shipped configs — `afb-evcc/etc/binding-simu15118-evcc.yaml` and
`afb-evse/etc/binding-simu15118-evse.yaml` — pin:

```yaml
proto: SECURE128:-VERS-SSL3.0:-VERS-TLS1.0:-ARCFOUR-128:+PSK:+DHE-PSK
```

Expanded with `gnutls-cli --list --priority …` on GnuTLS 3.8.9, its ECDSA half is:

```
TLS_ECDHE_ECDSA_AES_256_GCM_SHA384   0xc0,0x2c   TLS1.2
TLS_ECDHE_ECDSA_CHACHA20_POLY1305    0xcc,0xa9   TLS1.2
TLS_ECDHE_ECDSA_AES_256_CBC_SHA1     0xc0,0x0a   TLS1.0
TLS_ECDHE_ECDSA_AES_256_CCM          0xc0,0xad   TLS1.2
TLS_ECDHE_ECDSA_AES_128_GCM_SHA256   0xc0,0x2b   TLS1.2
TLS_ECDHE_ECDSA_AES_128_CBC_SHA1     0xc0,0x09   TLS1.0
TLS_ECDHE_ECDSA_AES_128_CCM          0xc0,0xac   TLS1.2
```

ISO 15118-2 requires `TLS_ECDH_ECDSA_WITH_AES_128_CBC_SHA256` (0xC0,0x25) and
`TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA256` (0xC0,0x23). Neither is in that list.

A station that pins exactly the standard's two suites — ours does — cannot complete a handshake with
your EVCC at all:

```
error:0A0000C1:SSL routines::no shared cipher
```

## Why we are reporting it rather than just working around it

We *did* work around it, deliberately and visibly, by unpinning our suite list for the run — that is
how everything in issue A above was observed. So this is not a request to unblock us.

It is worth raising because your simulator is used to check other people's stacks, and this makes one
particular result impossible to obtain with it: a SECC that implements the -2 TLS profile exactly
looks broken against your EVCC, and the failure arrives as `no shared cipher`, which reads as the
tester's misconfiguration rather than as a profile mismatch. Two stacks that are each internally
consistent — yours with a modern curated list, theirs with the standard's list — cannot talk.

We think the honest framing is that this is an **era gap rather than a mistake**: GnuTLS's curated
`SECURE128` dropped the SHA-256 CBC suites as legacy, and it is entirely defensible not to want them
back. If that is your position, saying so in the README next to the TLS instructions would save the
next tester the afternoon it cost us. If you would rather be able to meet a profile-conformant peer,
appending them to the priority string (`:+AES-128-CBC:+SHA256` or the explicit suite names) restores
the intersection without changing your defaults for anything else.

We are not claiming ISO 15118-2's list is the better engineering choice in 2026. Only that a stack
that follows it exists, and currently cannot connect.

---

## Also seen, secondary

- **Both binders spin without bound when a peer pauses or disconnects**, and a wedged binder does not
  answer SIGTERM — written up separately as issues **C** and **D** in
  [`tux-evse-spin.md`](tux-evse-spin.md), with a minimal reproduction: one connection, one
  `SupportedAppProtocolReq`, disconnect, and their responder writes 2.15 M log lines in 10 s with no
  peer connected. File C first if you file only one: it is confirmable in two minutes.
- **The responder answers only the car in its recording.** In responder mode the `query` block is
  matched field-by-field against the *incoming* request, so a foreign EV is refused at `SessionSetup`
  on its own EVCCID (`injector-binding-rs/src/verbs.rs:284`). We read this as a design property, not a
  defect — but a wildcard, or a documented note that the responder is for replaying its own capture,
  would make it obvious sooner. **Question rather than report.**
- **And the mirror of it in injector mode, which is the one that costs a tester an afternoon.** Each
  response is compared against an `expect` block lifted from the capture, `Jequal::Partial` (every
  expected field must match, `src/verbs.rs`, `injector_async_response`), and a single mismatch aborts
  the whole scenario (`src/controller.rs`, `job_scenario_exec` propagates the first `Fail`). Against
  any station that is not the recorded charger, a shipped scenario therefore dies at the first field
  that station legitimately chooses for itself — for us `SessionSetupRes.id`, three messages in. None
  of the three `--compact` modes relaxes the matching (`pcap-15118/src/pcap-import.rs`: `CompactMode`
  is `None | Reduced | Minimal`); they reduce *repetition*, not *strictness*.

  This is what stands between your captures and their most valuable use — replaying a real car at
  somebody else's station. Our workaround keeps the check rather than deleting it: reduce each
  `expect` to `rcode`/`tagid`/`proto`/`msgid`/`stamp`, so the injector still verifies **which** message
  came back and **with which response code**, and stops treating the captured charger's identity,
  schedules and measurements as requirements. (Removing the block entirely would be worse: a
  transaction with no `expect` is not checked at all — `expects.count() == 0` short-circuits to
  `Done`.) With that, the captured Audi ran 25 exchanges to `SessionStopRes` against our station, and
  both Porsche AC captures ran to completion.

  A fourth compaction mode that did this — call it `--compact=protocol` — would make every capture you
  ship usable as a conformance scenario for any stack. We are happy to hand over the 120-line script we
  used if it is a useful starting point; it is
  [`scenario-relax.py`](../../tools/interop-tux-evse/scenario-relax.py), Apache-2.0 like the rest of
  this repository. **Question rather than report**, but the one we would most like an answer to.
- **Minor, no issue filed:** `binding-start-evse.sh` hardcodes `export IFACE_SIMU=evse-veth` while
  `binding-start-evcc.sh` guards the same line with `if test -z`; the SDP socket binds without
  `SO_REUSEADDR`, so two simulators cannot share a host without network namespaces; and `autorun: 0`
  in every shipped scenario means a headless run answers nothing, which the README (devtools-driven)
  does not mention.

---

## Before sending

- [x] **Reproduce it yourself.** Issue A: done 2026-08-06 against their own responder, their PKI,
      their scenario — not only against ours. Issue B: verified by expanding their own priority string
      with `gnutls-cli`, then observed live as `no shared cipher`.
- [ ] **File two issues, A and B separately.** They have different audiences: A is a bug with a
      two-line fix, B is a policy question. Filing them together invites one answer to both.
- [ ] **Say how it was hit.** A third-party SECC driven by their injector, over their own PKI — that
      tells a maintainer the scenario is a real integration, not a fuzzer.
- [ ] **For issue B, ask before asserting.** They may know, and may have decided. The question to open
      with is "is the omission deliberate?", not "your profile is wrong".
- [ ] **Offer the patch for A only if they want it** — the choice between guarding on the challenge and
      keying on the payment option is theirs, and it touches `MeteringReceiptReq` too.
- [x] **The busy loops went to their own issues** rather than being buried here — C and D in
      [`tux-evse-spin.md`](tux-evse-spin.md), with a reproduction that needs no ISO 15118 stack on the
      other end at all.
- [ ] **Decide whether to offer `scenario-relax.py` in the same message or a separate one.** It is the
      only item here that asks them for a feature rather than reporting something broken, and it reads
      better as its own conversation — "here is what we did to replay your captures, would you want it
      upstream?" — than as a postscript to two bug reports.
