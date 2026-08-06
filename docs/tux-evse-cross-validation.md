# tux-evse cross-validation, in detail

The long form of the tux-evse column in the [interop matrix](../README.md#the-interop-matrix--who-we-test-against-and-what-happened):
what has run against **tux-evse/iso15118-simulator-rs**, what it caught, and what stands in the way.

Two contacts so far, and they are night and day. **2026-08-01** met the 21-month-old `v0.1` image in
the direction their design is worst at, and came back with two exchanges. **2026-08-06** built their
`main` from source and ran the direction their design favours — their injector, a captured car,
driving *our* SECC — five sessions, including the complete Audi DC route and an AC capture that
exists only at HEAD. Read alongside [Josev](josev-cross-validation.md) and
[eVDriveFlow](evdriveflow-cross-validation.md) (the two independent codecs) and
[EVerest](everest-cross-validation.md) (the independent charger).

Tooling: [`tools/interop-tux-evse/`](../tools/interop-tux-evse/README.md). Runs:
[`2026-08-01-tux-iso2-dc-notls`](interop-runs/2026-08-01-tux-iso2-dc-notls/notes.md) ·
[`2026-08-06-tux-head-reverse`](interop-runs/2026-08-06-tux-head-reverse/notes.md).

---

## What it is worth, precisely — and what it is not

**It is not a second EXI oracle**, and that was known before the first run rather than discovered by it.
Their encoders crate says it *"relies on cbexigen iso15118-encoder library for low level EXI binary
encoding"* — cbexigen is the generator behind libcbv2g, which is where **our own** byte-exact vector corpus
comes from. Two implementations of the same generated codec agreeing about bytes is close to a tautology.

What it is instead, and why it earned a harness anyway:

| | |
|---|---|
| **A real car's captured route** | Their scenarios are generated from packet captures. The 2026-08-06 runs put an actual Audi's DC session and an actual VW's AC session in front of our station — messages no specification-derived test would write, including one (an early `SessionStopReq`) that a real charger answered differently than we do. |
| **The only DIN 70121 material here** | They ship `tesla-3-din.json` and `tesla-3-din.pcap`. Nothing else in this project has met DIN. |
| **A stack that plays either end** | Responder (station) and injector (car), driven by the same scenario files. The injector is the half that works against a foreign peer. |

Their published artifact is still image `registry.redpesk.bzh/tux-evse/afb-iso15118:v0.1`
(`iso15118-simulator-rs-0.2`, `afb-binder 5.1.8`). Their `main` is 47 commits and ~17 months past
that tag — session-id fixes, -2 PnC, AC captures — and nothing newer is tagged or published, so
**"current" means a source build**: `main @ fc51088` with `injector-binding-rs @ 5fb66e4` and the
sibling crates cargo-pinned to `main` (the exact revisions are in the
[run notes](interop-runs/2026-08-06-tux-head-reverse/notes.md)).

---

## What has run

**2026-08-01, `EV→`, v0.1 image:** `SupportedAppProtocolReq` → `OK_SuccessfulNegotiation`, then
`SessionSetupReq` went unanswered. Two exchanges; the wall is described below.

**2026-08-06, both directions, `main` from source, native WSL2 (no container, no qemu):**

| Session | Scenario | Outcome |
|---|---|---|
| `←SECC` DC, stock expects | their `audi-dc-iso2-compact.json`, only `autorun: 1` added | SDP found our fixture's advertisement, SAP ran, and their injector **aborted at `SessionSetupRes.id`** — our EVSE ID is not the captured charger's. The injector-mode mirror of the responder finding, confirmed at HEAD |
| `←SECC` DC, expects relaxed to protocol fields | same file through [`scenario-relax.py`](../tools/interop-tux-evse/scenario-relax.py) | **The full captured-Audi session: 25 exchanges to `SessionStopRes`, every response code OK, order exactly the declared flow.** Their HEAD carries our freshly-issued session id through every request — the session-id fixes their `main` has and the image predates, visible on our wire |
| `EV→` ×2, HEAD responder | stock + autorun | Same wall as v0.1, same field, now source-located — and **the 2026-08-01 one-shot mystery does not reproduce natively**: the second connection got a fresh SAP answer and the same clean refusal |
| `←SECC` AC, VW capture uncompacted | `vw-ac-iso2.pcap` → their `pcap-iso15118 --compact=none` → relax | The VW's double `Authorization` poll reached our sequence guard, which **throws instead of answering `FAILED_SequenceError`** — the first finding *against us* from this counterparty (fix filed for the app; `Secc2.Dispatch`'s wildcard arm) |
| `←SECC` AC, VW capture `basic`-compacted | their own compaction folds the poll | **The rest of the route to `SessionStopRes`, session `Done`** — and one divergence worth keeping: the VW sends `SessionStopReq` straight from the charging phase; the **captured charger refused it as `FAILED_SequenceError`, ours answers `OK`** |

The reverse direction — the one their design is *for* — is no longer untried. It produced, in one
afternoon: external confirmation of our full -2 DC EIM and AC EIM station paths against real cars'
routes, one conformance gap of ours no self-consistent test could reach, one strict-vs-lenient
divergence against a real charger's recorded behaviour, and two measured defects of theirs.

---

## The finding — their player matches the capture, and stops at the first difference

Their own log states it exactly (responder mode, 2026-08-01, unchanged at HEAD 2026-08-06):

```
-- rec:{"id":"[ab,cd,ef,01,02,03]","tagid":"session_setup_req","proto":"iso2","msgid":0}
-- exp:{"id":"[00,7d,fa,07,5e,4a]","tagid":"session_setup_req","proto":"iso2","msgid":0}
responder-req-fail: query check return invalid value
```

`00:7D:FA:07:5E:4A` is the **captured Audi's EVCCID**. Ours is `AB:CD:EF:01:02:03`. Both are legal — an
EVCCID is the car's own identifier and no two cars share one. Their responder refuses to answer because we
are not the car in the recording.

The 2026-08-06 runs completed the picture: **the injector side is the mirror image.** In responder mode
the `query` block is matched against the incoming request; in injector mode the `expect` block is matched
against the incoming response (`Jequal::Partial`), and the scenario job propagates the first `Fail` — so
against a foreign station the shipped scenario aborts at the first field that station chooses for itself
(`SessionSetupRes.id`, three messages in). **No compaction mode helps on either side** — `CompactMode`
acts at *pcap-import* time, not at match time.

What makes the injector side usable anyway, where the responder side is not: a transaction with **no**
`expect` block is simply not checked, so the expects can be *reduced* instead of trusted —
[`scenario-relax.py`](../tools/interop-tux-evse/scenario-relax.py) keeps `rcode`/`tagid`/`proto`/`msgid`
(their injector still verifies which message came back, with which code) and drops the captured
charger's identity, schedules and measurements. That is the difference between the two-exchange column
of 2026-08-01 and the 25-exchange session of 2026-08-06. The responder has no equivalent: relaxing its
`query` blocks would mean rewriting what it *answers*, not what it checks.

---

## Five workarounds

Four are facts about the published v0.1 artifact (2026-08-01), one is a fact about their network code
(2026-08-06); each is written down so the next person does not rediscover it:

1. **Their image has no shell — by design.** A single 35 MB layer over a base the registry does not
   ship; everything under `/usr/bin`, usable with `--entrypoint /usr/bin/bash`; a log file needs a
   mounted volume. (Their `oci-15118` recipe assembles the result `FROM scratch` over a `mkTinyRootFs`
   root — deliberately minimal, not broken.)
2. **The image is amd64 only.** On ARM, qemu via `tonistiigi/binfmt` runs it slowly but correctly. *Moot
   for the source build, which is also what retired the "is qemu the confound?" question.*
3. **`binding-start-evse` hardcodes `export IFACE_SIMU=evse-veth`** — while the EV-side sibling guards
   the same line with `if test -z`. Calling `afb-binder` directly with the environment set works.
4. **`autorun: 0` in every shipped scenario means nothing runs headless.** Their documented workflow is
   a devtools click. Still true at HEAD; `scenario-relax.py --autorun` sets it.
5. **Their SDP socket binds without `SO_REUSEADDR`** (`iso15118-network-rs`, no reuse flag in the
   crate), to the interface link-local at `:15118`; our SDP server binds `[::]:15118` wildcard. On one
   host these conflict in either bind order, so their binder gets a network namespace — which is their
   own two-hosts model, minus the second host. Setup in
   [the tooling README](../tools/interop-tux-evse/README.md).

---

## What could not be explained — now it can

The 2026-08-01 run closed with a mystery: after one session, every further connection to their
responder was *accepted and immediately closed*, a `reset` verb did not restore it, and the notes
refused to guess between "one-shot scenario, a wedged state machine, or a qemu artefact".

Re-run natively at HEAD, twice against one responder instance: **the behaviour does not exist.** Both
connections got a SAP answer and the same clean query refusal. Whatever wedged in 2026-08-01 belonged
to the v0.1-image/qemu rig, not to their design.

What the re-run found instead is quantitative: **their binders busy-loop with no backoff, on three
different paths.** The responder retried one refused query match **1,125,779 times in its 240 s
lifetime** (~4,700/s, 572 MB of log) while sending nothing on the wire; the EVCC binding, waiting for
an `AuthorizationRes` our (buggy, see below) station never sent, re-decoded a stale buffer — its own
last outbound message — **10,939,791 times in ~70 s** (2.1 GB); and after a *completed* session with
the connection still open, the same binding spun on `pending=None` **7,502,782 times in ~25 s**
(1.29 GB). Excerpts with counts:
[`their-responder.log`](interop-runs/2026-08-06-tux-head-reverse/their-responder.log),
[`their-injector.ac.log`](interop-runs/2026-08-06-tux-head-reverse/their-injector.ac.log),
[`their-injector.ac-basic.log`](interop-runs/2026-08-06-tux-head-reverse/their-injector.ac-basic.log).
One defect class — retry-immediately-forever — on the failure, timeout and idle paths; worth an
upstream report.

---

## What the reverse runs found on our side

The reason to point a replayer at our station is that it repeats what its car did rather than react to
what we say, and the VW capture proved the point twice in one session:

- **Our sequence guard answered with silence.** ✅ **Fixed 2026-08-06.** The VW polls `Authorization`
  twice (the real charger's first answer was `Ongoing`); ours answers `Finished` immediately, so the
  replayed second poll is out-of-sequence at our station — and `Secc2.Dispatch`'s wildcard arm **threw**,
  closing the connection, where ISO 15118-2 answers `FAILED_SequenceError` on the wire and then
  terminates. The exception message even named the right code. Every other counterparty polls only while
  we say `Ongoing`, so no live session had ever reached that arm.

  The app's guard now builds the response that *pairs with* the refused request, carrying
  `FAILED_SequenceError`, and ends the session with it. **Re-run against the same VW scenario**
  (`ac-vw-fixed.*`): 6 requests, **6** responses instead of 5, the last one
  `AuthorizationRes → FAILED_SequenceError` — and their injector, which had busy-looped on a stale
  buffer waiting for an answer, now decodes it and stops with a plain
  `received "sequence_error", expected "ok"`. A car can tell a refusal from a dead station again, in
  their own decoder's words.
- **Any-phase `SessionStop`, strict station vs lenient station.** The VW ends with `SessionStopReq`
  straight from the charging phase — no `ChargingStatus`, no `PowerDelivery(stop)` — and the capture
  shows the real charger answering `FAILED_SequenceError`, while ours answers `OK` and terminates
  cleanly. Two defensible readings of the -2 sequence rules, observed on the same route. Recorded as a
  divergence, not corrected: our reading is deliberate (`Secc2`'s any-phase arm cites §8.4), and now it
  has a real-world counterexample beside it.

One smaller thing their decoder made visible: our `SessionSetupRes` carries a **hardcoded**
`EVSETimeStamp` of `1600000000` (September 2020) although `Secc2` holds a clock and uses it two
messages later (`PaymentDetailsRes`, the metering receipts). Open, with the sequence-guard arm, and
in the same file.

---

## What stays out of reach, and what would move it

- **Anything past `SessionSetup`, forward.** Unchanged at HEAD: the responder matches the captured
  car's `query` against the incoming request, and a foreign EV is refused at its first own-valued
  field. Relaxing is structurally different on that side (the `query` doubles as the answer table), so
  the forward direction stays a two-exchange handshake check until their matcher learns a wildcard —
  that is a question for upstream, not for this harness.
- **DIN 70121.** They ship the only DIN material this project has seen. Nothing here speaks DIN yet, so
  it is a capability question rather than a scheduling one.
- **TLS.** Both plain-TCP directions now exist, so the precondition is met; their GnuTLS profile and
  Trialog-derived PKI against ours is the next layer. Expect cipher-suite and curve alignment work,
  not ISO 15118 work.
- **The Porsche AC captures and the Tesla DIN pcap** — same pipeline as the VW run
  (`pcap-iso15118` → relax → fixture), unblocked the day the stack speaks what they captured.
- **A byte-level codec verdict.** Structurally unavailable, forever: their codec and our corpus come
  from the same generator.

---

## Current state

**Two contact days.** The first: one direction, two exchanges, one hard finding, four workarounds, one
open question. The second, at their HEAD: **the direction their design favours works** — the full
captured-Audi DC session and the VW AC session against our SECC, their injector verifying message
types and response codes throughout — the open question resolved as a rig artefact, a fifth workaround
(namespaces for their reuse-less SDP bind), two measured busy-loop defects of theirs, one conformance
gap of ours only a replayer could reach, and one strict-vs-lenient divergence against a real charger's
recorded answer.

The honest summary is no longer that this counterparty has not been used in the direction it is good
at. It is that the direction it is good at **pays**: a real car's route found, in one afternoon, the
arm of our state machine no self-consistent test had ever executed — which is the argument for
feeding the remaining captures through the same pipeline as capabilities allow.

---

## Every claim about their side, in their source

First table re-checked on **2026-08-06** against `tux-evse/iso15118-simulator-rs` at **`main @
fc51088`** — no longer against a tag adjacent to a stale image: the artifact that ran *is* this tree
(plus `injector-binding-rs @ 5fb66e4`, whose player the second table covers).

| Claim | In their source |
|---|---|
| Their codec is cbexigen's, so no independent byte oracle | `tux-evse/iso15118-encoders-rs`, `README.md:3` — *"Relies on cbexigen iso15118-encoder library for low level EXI binary encoding"*. Every crate here depends on it (`afb-evcc/Cargo.toml` and siblings, as git dependencies pinned to `main`) |
| The `query` block carries the captured car's own values | `pcap-15118/src/pcap-import.rs` — `jsonc.add("query", body_to_jsonc(body)?)`: the block **is** the request body lifted out of the pcap |
| Their responder matches it against the incoming request | The EVSE binding decodes and forwards under `{prefix}:{proto}:{tagid}` to the `iso15118-responder` API (`afb-evse/src/controller.rs`); the match and refusal are `injector-binding-rs/src/verbs.rs:284` — seen live in both contact days' logs |
| No compaction mode relaxes matching | `pcap-15118/src/pcap-import.rs` — `CompactMode` is `None \| Reduced \| Minimal`, applied while importing the pcap |
| `autorun: 0` in the shipped scenarios | `afb-test/etc/*.json` — all five, still, at HEAD |
| `binding-start-evse` hardcodes the interface | `afb-evse/etc/binding-start-evse.sh` unconditional `export IFACE_SIMU=evse-veth`; the EV-side sibling guards it (`binding-start-evcc.sh`, `if test -z "$IFACE_SIMU"`) |
| No shell in the image · amd64 only | Properties of the pulled `v0.1` artifact (its `oci-15118` recipe builds `FROM scratch` over `mkTinyRootFs`); irrelevant to the source build |
| No `SO_REUSEADDR` on the SDP socket | `iso15118-network-rs/src/ipv6-udp.rs` — `SocketSdpV6::new()` + `bind(iface, port)`, no reuse flag anywhere in the crate (resolved rev `f1ab338`) |

And about their player (`injector-binding-rs @ 5fb66e4`), all confirmed on the wire 2026-08-06:

| Claim | In their source |
|---|---|
| An expect mismatch aborts the whole scenario | `src/controller.rs` — `job_scenario_exec` does `spawn_one_transaction(...)?`, and a `SimulationStatus::Fail` becomes an error |
| The response check is partial-match against the `expect` block | `src/verbs.rs` — `jreceived.equal(uid, jexpected, Jequal::Partial)` in `injector_async_response` |
| A transaction without `expect` is not checked | same function — `expects.count() == 0` ⇒ `SimulationStatus::Done` |
| The responder busy-loops on a refused query | observed: 1,125,779 retries / 240 s for one request ([excerpt](interop-runs/2026-08-06-tux-head-reverse/their-responder.log)); the retry sits below the `call_sync` at `afb-evse/src/controller.rs` |
| The EVCC binding busy-loops on a missing response | observed: 10,939,791 stale-buffer decodes / ~70 s ([excerpt](interop-runs/2026-08-06-tux-head-reverse/their-injector.ac.log)); the loop logs `afb-evcc/src/controller.rs:342` |
