# tux-evse cross-validation, in detail

The long form of the tux-evse column in the [interop matrix](../README.md#the-interop-matrix--who-we-test-against-and-what-happened):
what has run against **tux-evse/iso15118-simulator-rs**, what it caught, and what stands in the way.

It is the thinnest column — **two exchanges** — and the one whose value was never going to be in the
session count. Read alongside [Josev](josev-cross-validation.md) and
[eVDriveFlow](evdriveflow-cross-validation.md) (the two independent codecs) and
[EVerest](everest-cross-validation.md) (the independent charger).

Tooling: [`tools/interop-tux-evse/`](../tools/interop-tux-evse/README.md). Run:
[`2026-08-01-tux-iso2-dc-notls`](interop-runs/2026-08-01-tux-iso2-dc-notls/notes.md).

---

## What it is worth, precisely — and what it is not

**It is not a second EXI oracle**, and that was known before the first run rather than discovered by it.
Their encoders crate says it *"relies on cbexigen iso15118-encoder library for low level EXI binary
encoding"* — cbexigen is the generator behind libcbv2g, which is where **our own** byte-exact vector corpus
comes from. Two implementations of the same generated codec agreeing about bytes is close to a tautology.

What it is instead, and why it earned a harness anyway:

| | |
|---|---|
| **A real car's captured route** | Their scenarios are generated from packet captures. `audi-dc-iso2-compact.json` is an actual Audi charging, not a specification read twice. |
| **The only DIN 70121 material here** | They also ship `tesla-3-din.json` and `tesla-3-din.pcap`. Nothing else in this project has met DIN. |
| **A stack that plays either end** | Responder (station) and injector (car), driven by the same scenario files. |

It is Rust, Apache-2.0, image `registry.redpesk.bzh/tux-evse/afb-iso15118:v0.1` —
`iso15118-simulator-rs-0.2` on `afb-binder 5.1.8`, and 21 months old at first contact.

---

## What has run

**Two exchanges.** `SupportedAppProtocolReq` → `OK_SuccessfulNegotiation`, then `SessionSetupReq` went
unanswered.

That is the whole session, and the reason is a design property rather than a bug — below. The run's other
half is the part that carried the information.

### Their scenario format, checked against their actual files

The harness's scenario parser was written from their README. This run put it in front of the files their
image really ships:

- **`TuxEvseScenario` and [`scenario-expectations.py`](../tools/interop-tux-evse/README.md) read
  `audi-dc-iso2-compact.json` correctly** — 1 scenario, 27 transactions, verbs `iso2:sdp_evse_req`,
  `iso2:app_proto_req`, `iso2:session_setup_req`, … exactly the structure the harness assumed, with **no
  unknown verbs**. A parser written from documentation met the artifact and was right.
- **The probe's verdict on the real file: 22 of 24 compared responses carry at least one field the
  captured charger chose for itself.** The harness README's central warning — that their `expect` blocks
  hold the captured *charger's* values, so their verdict on our responses is noisy — is now confirmed by
  their own shipped scenario rather than by a reconstruction of it.

---

## The finding — their responder matches the *request* against the capture

Their own log states it exactly:

```
-- rec:{"id":"[ab,cd,ef,01,02,03]","tagid":"session_setup_req","proto":"iso2","msgid":0}
-- exp:{"id":"[00,7d,fa,07,5e,4a]","tagid":"session_setup_req","proto":"iso2","msgid":0}
responder-req-fail: query check return invalid value
```

`00:7D:FA:07:5E:4A` is the **captured Audi's EVCCID**. Ours is `AB:CD:EF:01:02:03`. Both are legal — an
EVCCID is the car's own identifier and no two cars share one. Their responder refuses to answer because we
are not the car in the recording.

This is the mirror of the friction the harness README already warned about, and the more serious half.
That warning was about *their verdict on our responses* being noisy. This is the other direction: in
responder mode the `query` block is matched against the **incoming request**, so a foreign EV is refused at
the first message carrying an identifier of its own — which is the first message after the handshake.

**What it means for this counterparty.** With a shipped scenario, their responder answers the capture and
nothing else. Using it as a station for a foreign EV means relaxing or rewriting every `query` field the EV
chooses for itself; patching just the EVCCID is the first of an unknown number, and the same wall would
reappear at ServiceDiscovery, PaymentServiceSelection and every request carrying our own values. **No
compaction mode helps** — `CompactMode` is `None | Reduced | Minimal` and it acts at *pcap-import* time,
deciding which transactions are written into the scenario at all, not how strictly a received request is
matched afterwards.

---

## Four workarounds before a single byte crossed

Each is a fact about the published artifact, not about ISO 15118, and each is written down so the next
person does not rediscover it:

1. **Their image has no shell — by design, which is not obvious from outside.** `docker run … sh` fails
   with *executable file not found*: no `/bin`, no `/tmp`, no coreutils, and the pulled `v0.1` artifact is
   a single 35 MB layer over a base (`FROM 98072c178779`) the registry does not ship. Their recipe explains
   it — `oci-15118/Dockerfile` builds on almalinux and then assembles the result `FROM scratch` over a
   `mkTinyRootFs` root. So this is a deliberately minimal image, not a broken one. Everything they built
   sits under `/usr/bin`, so it is usable with an explicit `--entrypoint /usr/bin/bash`; a log file needs a
   mounted volume, because nothing is writable otherwise.
2. **It is amd64 only.** On ARM, `docker run --privileged --rm tonistiigi/binfmt --install amd64` registers
   qemu inside the VM and their binder runs — slowly but correctly.
3. **`binding-start-evse` hardcodes `export IFACE_SIMU=evse-veth`**, making their network script
   effectively mandatory even where a plain container interface would do. Calling `afb-binder` directly
   with `IFACE_SIMU=eth0` works — the exact command is printed in their own startup log.
4. **`autorun: 0` in the shipped scenario means the responder answers nothing.** The TCP server listens,
   accepts and closes. This is the single most important line to change for an automated run, and it is
   not in their README, whose workflow is to open the devtools UI and drive it by hand.

---

## What could not be explained

After the one session, **every further connection was accepted and immediately closed**, with a single
line in their log:

```
async-tcp-client: closing tcp:[fe80::…%2]:42314
```

A fresh binder in a fresh container behaved the same way — one session's worth of answers at most, and not
reliably even that. Their API has a `reset` verb documented as *"scenario sequence counter"*; calling it
returns success and does **not** restore the behaviour. Restarting the binder did not reliably restore it
either.

Whether that is a one-shot scenario, a state machine wedged by the failed request match, or an artefact of
running their Rust binder under qemu is **not known**, and the run notes say so rather than guessing. It is
the kind of question a first contact is supposed to produce.

---

## What stays out of reach, and what would move it

- **Anything past `SessionSetup`, forward.** Gated entirely on the `query` matching. Either every field a
  foreign EV chooses for itself gets relaxed, or their matcher turns out to support a wildcard — that is
  one question to them, or one read of the **`iso15118-responder` API**: their EVSE binding decodes the
  request and forwards it under `{prefix}:{proto}:{tagid}` (`afb-evse/src/controller.rs:144`,
  `target: iso15118-responder` in `afb-evse/etc/binding-simu15118-evse.yaml`), so the comparison happens
  in the scenario binding, not in the EVSE one.
- **The reverse direction — their injector against our SECC — is untouched, and is the direction their
  design actually favours.** There the captured Audi drives *our* station, and the field matching applies
  to responses they can be told to ignore, so this finding does not block it. It is the obvious next run
  against this counterparty and has not been made.
- **DIN 70121.** They ship the only DIN material this project has seen. Nothing here speaks DIN yet, so it
  is a capability question rather than a scheduling one.
- **A byte-level codec verdict.** Structurally unavailable: their codec and our corpus come from the same
  generator.

---

## Current state

**One run, two exchanges, one hard finding, four workarounds and one open question** — and a parser
validated against the real artifact, which was half the point of the exercise.

The honest summary is that this counterparty has not yet been used in the direction it is good at. Their
design is a replayer: pointed at our SECC as an injector, a captured Audi would be driving our station,
which is a route no specification-derived test can produce. Pointed at us as a responder, it answers the
car in its recording and no other. The first is untried; the second is where the two exchanges came from.

---

## Every claim about their side, in their source

Re-checked on **2026-08-06** against `tux-evse/iso15118-simulator-rs` at tag **`0.2`** — the closest match
to the `iso15118-simulator-rs-0.2` the image reports. **The artifact we ran was the `v0.1` OCI image and
21 months old**, so drift between it and this tag is possible; where a claim rests on the image rather than
the source, the table says so.

| Claim | In their source |
|---|---|
| Their codec is cbexigen's, so no independent byte oracle | `tux-evse/iso15118-encoders-rs`, `README.md:3` — *"Relies on cbexigen iso15118-encoder library for low level EXI binary encoding"*. Every crate here depends on it (`afb-evse/Cargo.toml:13` and siblings) |
| The `query` block carries the captured car's own values | `pcap-15118/src/pcap-import.rs:73,146` — `jsonc.add("query", body_to_jsonc(body)?)`: the block **is** the request body lifted out of the pcap |
| Their responder matches it against the incoming request | The EVSE binding decodes and forwards under `{prefix}:{proto}:{tagid}` to the `iso15118-responder` API (`afb-evse/src/controller.rs:144`; `target:` in `afb-evse/etc/binding-simu15118-evse.yaml`). The refusal itself is from **our run's log**, not from reading their matcher |
| No compaction mode relaxes it | `pcap-15118/src/pcap-import.rs:220` — `CompactMode` is `None \| Reduced \| Minimal`, applied while importing the pcap |
| `autorun: 0` in the shipped scenarios | `afb-test/etc/*.json:10` — all five, including `audi-dc-iso2-compact.json` |
| `binding-start-evse` hardcodes the interface | `afb-evse/etc/binding-start-evse.sh:22`, an unconditional `export IFACE_SIMU=evse-veth` — while the EV-side sibling guards the same line with `if test -z "$IFACE_SIMU"` (`binding-start-evcc.sh:58`) |
| No shell in the image | `oci-15118/Dockerfile` — builds on almalinux, then assembles the result `FROM scratch` over a `mkTinyRootFs` root. Deliberate, not broken |
| amd64 only · the dangling base layer | Properties of the pulled `v0.1` artifact; not re-checkable from the source tree |
