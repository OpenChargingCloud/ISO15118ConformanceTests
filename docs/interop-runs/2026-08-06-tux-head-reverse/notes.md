# 2026-08-06 — tux-evse at HEAD, source build: the reverse direction, finally

The 2026-08-01 run met a 21-month-old image and closed with *"the reverse direction is untouched, and
is the direction their design actually favours."* This run is that direction — their injector, a
captured car, driving **our** SECC — against a **source build of their current HEAD**, native in WSL2
(no container, no qemu). Five sessions: the shipped Audi DC scenario stock and expect-relaxed, the
forward direction re-checked twice against their HEAD responder, and the **VW AC capture** that only
exists at HEAD, in both of their own compaction forms.

| | |
|---|---|
| Counterparty | [tux-evse/iso15118-simulator-rs](https://github.com/tux-evse/iso15118-simulator-rs) `main` @ **`fc51088`** (2025-03-07, their newest) — built from source with `injector-binding-rs` @ `5fb66e4`, `iso15118-encoders-rs` @ `fe6c0aa`, `iso15118-network-rs` @ `f1ab338`, `iso15118-encoders` (C, cbexigen) @ `710839a`, on afb @ HEAD (`afb-libafb` 5.7.5 `e8f6e32`, `afb-binder` `e1de49a`, `afb-binding` `7dccf14`, `afb-librust` `0344891`, `rp-lib-utils` 0.3.1 `39ae7a4`) |
| Ours | conformance suite @ `51bd003`, `EVSimulatorApp` @ `6035e68` — the `TuxEvseInteropTests` fixtures, run under .NET 10 **in WSL** so their SDP multicast and our listener share one Linux host |
| Rig | WSL2 Debian 13, amd64 native. Their binder in a network namespace `tuxev` (`evcc-veth`), our fixture on the host side (`evse-veth`) — see workaround 5 |
| Directions | **`←SECC` ×4** (their injector → our fixture) · `EV→` ×2 (re-check against their HEAD responder) |
| Outcome | **The full captured-Audi DC session ran against our SECC — 25 exchanges to `SessionStopRes`, every response code OK, order exactly the declared flow.** The VW AC capture ran to `SessionStop` in its `basic`-compacted form and found one gap of ours in its uncompacted form. The forward wall is unchanged at HEAD, and the old run's one-shot mystery is resolved: it does not reproduce natively. Two busy-loop defects of theirs measured and excerpted. |

Artifacts: `dc-stock.*`, `dc-relaxed.*`, `ac-vw.*` (uncompacted), `ac-vw-basic.*` (their `basic`
compaction), `forward.*`, `forward2.*`, `their-injector.*.log`, `their-responder.log`. The two
multi-hundred-MB logs are excerpted with the repeat counts in their headers; everything else is as
written.

## Why HEAD, and what it took

Their repo's only tags are `0.1` and `0.2`; the `v0.1` OCI image (the thing the 2026-08-01 run met)
still ships `iso15118-simulator-rs-0.2` on `afb-binder 5.1.8`. `main` is 47 commits past `0.2` and
carries, among others, *"Fix session ID"* (`7e11f6e`), *"Fix empty session ID in iso2"* (`27749e0`),
-2 PnC support (Feb 2025), and **the AC captures** (`165c0bd`) — `vw-ac-iso2.pcap` and two Porsche
Taycan AC pcaps that no released artifact contains.

The build mirrors their `oci-15118/Dockerfile_almalinux_source` natively (their afb stack from
source, then the Rust workspace, whose tux-evse siblings are cargo git-dependencies pinned to
`main` — so one `cargo build` really is "the whole family at HEAD"), **plus
`tux-evse/injector-binding-rs`, which that Dockerfile forgets** although every shipped scenario
config loads `libafb_injector.so` from it. Their scenario player, their EVCC/EVSE bindings and
`pcap-iso15118` all come out of `~/tux-evse/cargo/release/`.

## Workaround 5 (joining the four from 2026-08-01): one host needs two network stacks

Their `nettls` binds the SDP socket **without `SO_REUSEADDR`**
(`iso15118-network-rs/src/ipv6-udp.rs` — no reuse flag anywhere in the crate), and binds it to the
interface's link-local address at `:15118`. Our SDP server binds `[::]:15118` wildcard (with reuse).
On one Linux host those collide in either order — a wildcard and a specific bind may share a port
only if *both* carry `SO_REUSEADDR`. Their own demos dodge this by accident of topology (two
containers, or two veth ends whose link-locals differ — specific-vs-specific never conflicts); a
harness that wants *our* stack on the same host does not get that accident. So their binder runs in
a network namespace:

```bash
ip netns add tuxev
ip link add evse-veth type veth peer name evcc-veth
ip link set evcc-veth netns tuxev
ip link set evse-veth up
ip netns exec tuxev ip link set lo up
ip netns exec tuxev ip link set evcc-veth up
```

Their side sees `evcc-veth`, ours advertises on `evse-veth` (`V2G_INTEROP_SDP=evse-veth`), and SDP
multicast crosses the veth like the two-hosts setup their design assumes.

## Run 1 — Audi DC, scenario as shipped (`dc-stock.*`)

Their injector with `audi-dc-iso2-compact.json` exactly as their repo ships it, plus `autorun: 1`
(workaround 4, still required at HEAD). Their SDP discovery found the fixture's advertisement —
`InteropSdp`'s first live use against this counterparty — the SupportedAppProtocol handshake ran,
and the third transaction died on its `expect` block:

```
received: {"id":"DE*ABC*E1","rcode":"new_session","stamp":1600000000,...}
expected: {"id":"DE*PNX*E12345*1","rcode":"new_session",...}
--[pkg:51] SimulationStatus::Fail ... jsonc-match invalid value
CRITICAL: binding start fail:unexpected status for uid:pkg:51
```

`DE*PNX*E12345*1` is the EVSE ID of the charger in their capture; ours is not that charger. One
station-specific field, and `job_scenario_exec` propagates the `Fail` with `?`, so the whole
scenario aborts — **the injector-mode mirror of the responder finding from 2026-08-01, now
confirmed at HEAD**: in both directions their player compares against the captured peer's own
values and stops at the first legitimate difference. (`compact` cannot save it: `basic`/`strong`
act at pcap-import time; the player's expect check is `Jequal::Partial` against whatever the
scenario carries, and a transaction with **no** `expect` block is simply not checked.)

The TCP connection stays open after the abort — their binder holds it until killed, which is why
our fixture's recording ends in an `EndOfStreamException` rather than a `SessionStopRes`.

## Run 2 — Audi DC, expects relaxed (`dc-relaxed.*`): **the result**

Same scenario, with every `expect` reduced to its **protocol fields** — `rcode`, `tagid`, `proto`,
`msgid`, `stamp`; exactly the classification `scenario-expectations.py` prints — so their injector
still verifies *which* message came back and *with which response code*, and stops comparing the
captured ABB charger's identity, schedules and measurements against a station that is not that
charger. 161 station-specific fields stripped; the tool for it is now
[`tools/interop-tux-evse/scenario-relax.py`](../../tools/interop-tux-evse/scenario-relax.py).

**The full session ran.** 25 request/response pairs, every one of their checks `Check` (matched),
every response code OK, and the recorded order against the declared flow:

```
SDP → SupportedAppProtocol → SessionSetup → ServiceDiscovery → PaymentServiceSelection(external) →
Authorization → ChargeParameterDiscovery(dc_extended) → CableCheck → PreCharge →
PowerDelivery(start) → CurrentDemand ×13 → PowerDelivery(stop) → WeldingDetection → SessionStop
```

> **The order matches the declared flow exactly.** — `dc-relaxed.flow.md`

The fixture's own verdict: `Passed — our SECC drove their injector's session to the terminal
state`, in 36 s. A real Audi's message sequence, with the capture's own EVCCID
(`00:7D:FA:07:5E:4A`) and charging profile, against a station it never met — the route no
specification-derived test can produce, which is what this counterparty was kept for.

Two details worth the wire bytes:

- **Their session-ID handling at HEAD works.** Our `SessionSetupRes` issues a fresh session id, and
  every subsequent request of theirs carries it (the byte run `0238a1510f…` repeats from
  `dc-relaxed.frames.log` frame 2 onward). The two session-id fixes on their `main` are the
  difference; the image the last run met predates both.
- **Why the folded polling loops don't bite here:** our SECC answers `Authorization` and
  `CableCheck` with `EVSEProcessing=Finished` immediately, so a scenario that names each poll once
  (their compaction folds the loops) walks straight through. A station that answered `Ongoing`
  would leave their single-shot transactions out of step — see the AC run.

## Runs 3+4 — forward re-checked against their HEAD responder (`forward.*`, `forward2.*`)

The 2026-08-01 forward run left one question it could not answer: after its single session, every
further connection was *accepted and immediately closed*, and whether that was the image, the
scenario state, or qemu was unknowable from a Mac driving an emulated container. Re-run natively
against the HEAD responder, twice in a row against one binder instance:

- **The query wall is unchanged, and now source-located**: SAP answered, then our `SessionSetupReq`
  refused — `jsonc-match invalid value received:"[ab,cd,ef,01,02,03]" expected:"[00,7d,fa,07,5e,4a]"`
  (`injector-binding-rs/src/verbs.rs:284`, the responder matching the *incoming request* against the
  captured car's `query`). Same wall, same field, same message as v0.1.
- **The one-shot behaviour does not reproduce.** The second connection got a fresh SAP answer and
  the same clean refusal — no accept-and-close, no wedge, on the same binder instance. The old
  mystery was an artifact of the v0.1-image/qemu rig, not of their design; the run notes that
  guessed "one-shot scenario, wedged state machine, or qemu" can close with the third.
- **New finding, their side — the responder busy-loops on a refused request.** One unanswered
  `SessionSetupReq` per connection produced **1,125,779** `responder-req-fail` retries in the
  binder's 240 s lifetime (~4,700/s, 572 MB of log): the failed query match is retried immediately,
  forever, with no backoff and no answer on the wire. `their-responder.log` is the excerpt with the
  counts; the EV on the other end sees only silence.

## Run 5 — VW AC, the HEAD-only capture (`ac-vw.*`, `ac-vw-basic.*`)

`vw-ac-iso2.pcap` exists only in their repo, not in any released artifact, and no scenario JSON is
shipped for it — their own `pcap-iso15118` (also built at HEAD) generates one. Uncompacted it is 11
transactions and preserves something `basic` folds away: **the VW polls `Authorization` twice**,
because the real charger's first answer was `EVSEProcessing=Ongoing_WaitingForCustomerInteraction`.

**Uncompacted (`ac-vw.*`) — one finding, ours.** Our SECC answered the first `AuthorizationReq`
with `Finished` and moved to `ChargeParams`; the replayed second `AuthorizationReq` then hit our
sequence guard, which **throws and closes instead of answering**:

```
SessionAborted: SECC sequence guard: AuthorizationReq not allowed in phase ChargeParams
                (would be ResponseCode.FAILED_SequenceError)          — Secc2.cs:237
```

The parenthesis is the finding: ISO 15118-2 answers an out-of-sequence request with
`FAILED_SequenceError` *on the wire* and then terminates; ours names the right code in an exception
message and sends nothing. Every previous counterparty either polls only while we say `Ongoing`
(so the pair never desynchronizes) or is our own EV; it took a replayer, which repeats what its car
did rather than react to what our station said, to reach that arm. **The fix belongs in the app**
(`Secc2.Dispatch`'s wildcard arm) and is not made here; this run is the evidence
(`ac-vw.frames.log`: 6 requests, 5 responses).

Their side of the same moment is the second busy-loop: waiting for an `AuthorizationRes` that never
came, their EVCC binding re-decoded a stale buffer — `unexpected exi message
expected:Iso2(AuthorizationRes) got:Iso2(AuthorizationReq)`, *their own last outbound message* —
**10,939,791 times in ~70 s, 2.1 GB of log**, no backoff (`their-injector.ac.log`, excerpted).

**`basic`-compacted (`ac-vw-basic.*`) — the rest of the route, and a divergence worth keeping.**
Their own compaction folds the double poll, and the session runs: `ChargeParameterDiscovery
(ac_three_phase)` → `PowerDelivery(start)` → … and then the capture does something no test of ours
would write: the VW sends **`SessionStopReq` straight from the charging phase** — no
`ChargingStatus`, no `PowerDelivery(stop)`. Our station answers `OK` and terminates cleanly
(`Passed`, session to `Done`); their injector's last check then fails honestly:

```
rcode: received "ok", expected "sequence_error"
```

**The real charger in the capture refused that early `SessionStopReq` as `FAILED_SequenceError`;
ours accepts it.** Two defensible readings of the -2 sequence rules, live on the same route: our
`(_, SessionStopReqType)` arm treats a stop as legal in any phase, the captured station did not.
Recorded as a divergence, not a defect — but it is exactly the kind of fact about real chargers
this column exists to collect.

And a third face of their no-backoff loop, after this session *completed*: with the scenario over
and the connection still open, their EVCC binding spun on `Received iso2 message while pending=None`
— **7,502,782 times** in the binder's remaining ~25 s, 1.29 GB of log
(`their-injector.ac-basic.log`, excerpted). Failure path, timeout path, idle path: same defect.

## What this run changes

- **The tux-evse column's reverse direction exists** — `←SECC` for -2 DC (complete, 25 exchanges)
  and -2 AC (complete under `basic` compaction; blocked one message earlier uncompacted, by us).
- **Versions met** gains `main @ fc51088` from source, next to the v0.1 image.
- The 2026-08-01 open question is closed; two of its "confirm on first contact" items are now
  measured defects (the busy-loops) worth reporting upstream.
- One conformance gap of ours (sequence guard answers with silence) and one leniency-vs-strictness
  divergence (any-phase `SessionStop`) are on the record with bytes.

## How to reproduce

```bash
# build (WSL2 Debian 13 or any Linux; ~10 min): their afb stack + workspace + injector binding
tools/interop-tux-evse/README.md      # "Building their HEAD from source"

# namespace (workaround 5)             # "One host, two stacks"

# relax the shipped scenario's expects to protocol fields
tools/interop-tux-evse/scenario-relax.py audi-dc-iso2-compact.json audi-relaxed.json --autorun

# our side: fixture with SDP advertisement, recording, in WSL
V2G_INTEROP_LISTEN=55000 V2G_INTEROP_SDP=evse-veth V2G_INTEROP_PROTOCOL=2 V2G_INTEROP_MODE=dc \
V2G_INTEROP_RECORD=/tmp/tux-run V2G_INTEROP_SCENARIO=$PWD/audi-relaxed.json \
  dotnet test -c Release ISO15118ConformanceTests.Simulation \
    --filter "FullyQualifiedName~TuxEvseInteropTests.TheirInjector"

# their side, in the namespace
sudo ip netns exec tuxev env CARGO_BINDING_DIR=~/tux-evse/cargo/release \
  INJECTOR_BINDING_DIR=~/tux-evse/cargo/release IFACE_SIMU=evcc-veth SIMULATION_MODE=injector \
  afb-binder --name afb-evcc \
    --config=.../afb-evcc/etc/binding-simu15118-evcc-no-tls.yaml --config=audi-relaxed.json
```

The AC scenario comes from their converter first:
`pcap-iso15118 --pcap_in=afb-test/trace-logs/vw-ac-iso2.pcap --json_out=vw-ac.json --compact=basic`
(or `--compact=none` for the unfolded polls), then the same relax + run with
`V2G_INTEROP_MODE=ac`.
