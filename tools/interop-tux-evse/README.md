# tux-evse interop (Tier 2)

Interop between **our** EVCC/SECC and **[tux-evse/iso15118-simulator-rs](https://github.com/tux-evse/iso15118-simulator-rs)**
(Apache-2.0), a Rust ISO 15118 simulator that plays either end and is driven by scenarios generated from
packet captures.

**This file is how to run it.** For what has already run — both directions now, five sessions at their
HEAD on 2026-08-06 including the full captured-Audi DC route against our SECC — see
[`docs/tux-evse-cross-validation.md`](../../docs/tux-evse-cross-validation.md).

Like [`../interop-josev`](../interop-josev/README.md) this is **opt-in and never part of the offline CI**.
The automated hook is the `[Explicit] [Category("Interop")]` fixture
[`ISO15118ConformanceTests.Simulation/Interop/TuxEvseInteropTests.cs`](../../ISO15118ConformanceTests.Simulation/Interop/TuxEvseInteropTests.cs),
gated on environment variables — `dotnet test -c Release` skips it entirely.

*Written against the repository as of 2026-08-01; revised 2026-08-06 after building and running their
`main` from source. The published `v0.1` image still ships `iso15118-simulator-rs-0.2` and predates
their session-id fixes and the AC captures — prefer the source build below.*

---

## What this counterparty is worth, precisely

**It is not a second EXI oracle.** Their encoders crate says it "relies on cbexigen iso15118-encoder
library for low level EXI binary encoding" — cbexigen is the generator behind libcbv2g, which is where
*our own* byte-exact vector corpus comes from. Two implementations of the same generated codec agreeing
about bytes is close to a tautology. The counterparties whose bytes are independent are Josev
(EXIficient) and eVDriveFlow (OpenEXI), and both already have a harness.

**What it is worth is the layer our corpora cannot see.** `docs/CONCEPT.md` §1.3 puts the number on it:
the ~15 real conformance fixes came from live interop and from nothing else, and they lived in the state
machines. Sequencing, timing, field semantics, what a station does when a car does something it has never
seen — none of that is in a vector file.

And one thing no other counterparty offers: **their side is a replayer, not a state machine.** Their
scenarios are transactions lifted out of a real capture — an Audi against an ABB charger, in the file they
ship — each with a recorded request to send and a recorded response to compare. That is the same
construction as our `Vectors/Session.*.trace.json`, arrived at independently. A reverse run therefore puts
*a real car's* messages in front of our station, rather than our own idea of a car's, which is the half a
self-consistent implementation is worst at.

Their `pcap-iso15118` also turns any capture into a scenario, so a run against *any* counterparty can be
replayed by both sides afterwards — theirs from the scenario, ours from the trace.

**What it cannot tell us yet:** -20 and DIN are announced rather than shipped, so it exercises the -2 half
of our stack only.

---

## Reading the verdict (read this before the first run)

Their injector's pass/fail is **not** our pass/fail, and expecting otherwise costs an afternoon.

Each transaction carries an `expect` block, and it holds *the captured station's* values:

```json
"expect": { "id": "DE*PNX*E12345*1", "rcode": "new_session", "tagid": "session_setup_res", "msgid": 1 }
```

`DE*PNX*E12345*1` is the EVSE ID of the charger in their capture. Ours is not that charger, so that
comparison fails, correctly, on a session that is otherwise perfect. The same goes for schedules, EVSE
status flags, and anything else a station chooses for itself.

So the verdicts are:

| Signal | What it means |
|---|---|
| **The flow report** (`*.flow.md`, below) | which messages crossed, in which order, with which response codes, and how that compares with the sequence their scenario declares — **this is the verdict worth reading** |
| Our fixture's assertion (`isDone`) | our SECC reached its terminal state. Necessary, and coarse: a session can end correctly and still have taken a route no car would take |
| Their transaction log | which of *their* recorded expectations matched; a field mismatch is a lead, not a defect |
| The recording | what actually crossed the wire — the only thing worth arguing from afterwards |

The scenario file is not only a list of things to send: it is **a declared flow**, lifted from a real
capture, with the real gaps between the messages. Point the fixture at it and the flow report says
where our session and that capture's session diverge:

```bash
V2G_INTEROP_SCENARIO=/usr/share/iso15118-simulator-rs/audi-dc-iso2-compact.json
```

Consecutive repeats are collapsed on both sides before the comparison, and that is not a nicety: a
charging session polls CurrentDemand while their `basic`/`strong` compaction names it once, so an
uncollapsed diff would report the poll loop as forty insertions and bury whatever the real difference
was. Counts are reported separately, as counts.

Their verb vocabulary is a hand-kept table in `TuxEvseScenario.Vocabulary`, not a snake_case
conversion — `payment_selection_req` is `PaymentServiceSelectionReq`, `param_discovery_req` is
`ChargeParameterDiscoveryReq`, `app_proto_req` is the SupportedAppProtocol handshake. A verb not in
the table is named in the report and left out of the comparison, rather than guessed at.

Run [`scenario-expectations.py`](scenario-expectations.py) over the scenario file **before** the run to
list which expectations are station-specific, so the noise is known up front:

```bash
./scenario-expectations.py /usr/share/iso15118-simulator-rs/audi-dc-iso2-compact.json
```

**No `compact` mode makes the player tolerant** — that hope did not survive first contact. `compact`
acts at *pcap-import* time (which transactions the scenario contains); the player's expect check is
`Jequal::Partial` against whatever the scenario carries, and the scenario job propagates the first
`Fail`, aborting the whole replay (`injector-binding-rs/src/controller.rs`, confirmed on the wire
2026-08-06: the shipped Audi scenario dies at `SessionSetupRes.id`, three messages in). A transaction
with **no** `expect` block is not checked at all.

So for a run against our station, reduce the expects to the protocol fields first — message type and
response code stay checked, the captured charger's own values stop aborting the replay:

```bash
./scenario-relax.py audi-dc-iso2-compact.json audi-relaxed.json --autorun
```

---

## The short path: a TCP relay, no discovery

**Read this before the setup below — for a forward run you may need much less of it.**

After the SupportedAppProtocol handshake an ISO 15118 session is a plain TCP stream. The only part
that needs interfaces, zones and multicast is **SDP**, the discovery step: UDP multicast to
`ff02::1:15118`, answered with the station's link-local address and TCP port. Everything after that
does not care how it was reached — and the Josev harness has always skipped it, connecting to
`host:port` directly.

So for **our EVCC → their responder**, put a relay in front of their station and connect to it like
any other server:

```bash
# in the VM/host running their responder — find the port first, it is not fixed
ss -6 -tlnp | grep afb            # e.g. [fe80::ac52:27ff:fef3:d0d7%evse-veth]:64109
socat TCP6-LISTEN:15118,fork,reuseaddr 'TCP6:[fe80::ac52:27ff:fef3:d0d7%evse-veth]:64109'
```

```bash
# from anywhere that can reach it, including a Mac
./live-evcc-iso2-dc.sh '' vm.local:15118
```

No zone, no multicast, no interface names on our side — and the whole harness works unchanged, because
`--connect` and `V2G_INTEROP_SECC` take an ordinary `host:port`.

**What this does not do.**

- **Only the forward direction.** In a reverse run *their* injector is the one discovering, and a relay
  cannot tell it where to look. That direction needs SDP answered on a shared link — our SECC's
  `--sdp --interface`, or [`../interop-josev/sdp-responder.py`](../interop-josev/sdp-responder.py) as
  a shim.
- **SDP is not exercised.** A real loss, but a covered one: every recorded Josev run drives `--sdp` in
  both directions. Discovery is not what this counterparty is here to test.
- **TLS through a relay is untested here.** A TCP relay is transparent to TLS unless a certificate is
  bound to the address it was reached at; for the plain-TCP runs below the question does not arise.

You still need their responder running, so the setup below still applies to *their* side — but only to
their side, and only inside one Linux box.

## Setup

Nothing here is installed for you, and nothing is vendored. Their stack is ~40k lines of Rust with C
dependencies; they recommend the packages or the container over building it — but the published
image is a `0.2` from 2024, and everything after it (session-id fixes, -2 PnC, the AC captures)
lives only on `main`.

### Building their HEAD from source (what the 2026-08-06 runs used)

Their `oci-15118/Dockerfile_almalinux_source` is the recipe; it works outside a container too
(WSL2 Debian 13, ~10 min). Two things it does not say:

- **`injector-binding-rs` is missing from it**, although every shipped scenario config loads
  `${INJECTOR_BINDING_DIR}/libafb_injector.so`. Build it like the workspace.
- The tux-evse crates pull `iso15118-encoders-rs` and `iso15118-network-rs` as **cargo git
  dependencies pinned to `main`**, so one `cargo build` really is the whole family at HEAD.

```bash
# C prerequisites, each configured against the previously installed one:
# afb-binding → rp-lib-utils → afb-libafb (their WITH_* flags) → afb-binder → iso15118-encoders
#   cmake -DCMAKE_BUILD_TYPE=Release .. && make -j && sudo make install   (each), then ldconfig
# Rust, sharing one CARGO_TARGET_DIR so the crates see afb-librust's rlib:
export CARGO_TARGET_DIR=~/tux-evse/cargo
export RUSTFLAGS="-L$CARGO_TARGET_DIR/release -Adead_code -Aunused_imports"
(cd afb-librust             && cargo build --release)
(cd iso15118-simulator-rs   && cargo build --release)
(cd injector-binding-rs     && cargo build --release)
# artifacts: $CARGO_TARGET_DIR/release/{libafb_sim15118_evcc.so,libafb_sim15118_evse.so,
#                                        libafb_injector.so,pcap-iso15118}
```

Run their binder with `CARGO_BINDING_DIR` and `INJECTOR_BINDING_DIR` pointed at
`$CARGO_TARGET_DIR/release` instead of the packaged `/usr/redpesk/...` paths; everything else in
their start scripts applies unchanged. The binder wants
`/usr/share/afb-ui-devtools/binder` to exist for its `/devtools` alias — `mkdir -p` is enough when
the devtools RPM is not installed.

### One host, two stacks: their side needs a network namespace

Their `nettls` binds the SDP socket **without `SO_REUSEADDR`** and to the interface's link-local
address; our SDP server binds `[::]:15118` wildcard. On one host those conflict **in either order**
(wildcard-vs-specific shares a port only when both sockets set the flag). Their own demos never see
it — two containers, or two veth ends, are two different specific addresses. Give their binder its
own stack and the shared-host case becomes their intended two-hosts case:

```bash
sudo ip netns add tuxev
sudo ip link add evse-veth type veth peer name evcc-veth
sudo ip link set evcc-veth netns tuxev
sudo ip link set evse-veth up
sudo ip netns exec tuxev ip link set lo up
sudo ip netns exec tuxev ip link set evcc-veth up
# theirs: sudo ip netns exec tuxev env IFACE_SIMU=evcc-veth ... afb-binder ...
# ours:   V2G_INTEROP_SDP=evse-veth (the fixture advertises where their multicast arrives)
```

### Container (the published image — 21+ months behind main)

```bash
./prepare-tux-evse.sh            # fetches their network script, shows what it will do, pulls the image
sudo ./client-server-bridge.sh   # creates evse-tun / evse-veth / evcc-veth  (their script, run by you)
```

The image is `registry.redpesk.bzh/tux-evse/afb-iso15118:v0.1`. Their own two-terminal example:

```bash
podman run --rm --name podman_evcc --network=host --cap-add=NET_ADMIN -it \
  registry.redpesk.bzh/tux-evse/afb-iso15118:v0.1 bash -c \
  "binding-start-evcc --simulation_conf /usr/share/iso15118-simulator-rs/binding-simu15118-evcc-no-tls.yaml \
                      --scenario_file  /usr/share/iso15118-simulator-rs/audi-dc-iso2-compact.json --no-clean"
```

…and the same with `evse` for the responder. Web UIs: injector on <http://localhost:1234/devtools/>,
responder on <http://localhost:1235/devtools/>.

### Packages

Fedora / openSUSE / Ubuntu via the redpesk SDK — see their README. `iso15118-simulator-rs`,
`iso15118-simulator-rs-test`, and `dsv2gshark` (the Wireshark dissector, which is useful here regardless).

### Their network model

Their simulators talk over **virtual interfaces**, not loopback: `evcc-veth` (car side), `evse-veth` /
`evse-tun` (charger side), created by their `client-server-bridge` script. Their config sets
`sdp_port: 15118` and an `iface`, and the EV side multicasts an SDP request before connecting.

This matters for us in one specific way: the address you will be handed is a **link-local IPv6 with a
zone**, e.g. `[fe80::ac52:27ff:fef3:d0d7%evcc-veth]:64109`. Write the zone, always, and write it inside
the brackets. `V2GEndpoint` refuses the forms that cannot work rather than letting the platform silently
drop the zone — which it does, without an error, whenever the interface name is one this machine does not
have (a typo, or the veth pair not created yet, or a container that does not see it).

---

## Running

Both scripts derive their own paths and take the interface names as arguments. Neither installs anything.

### Their injector → our SECC  ([`reverse-iso2-dc.sh`](reverse-iso2-dc.sh))

The direction that tests what we **accept**.

```bash
./reverse-iso2-dc.sh evse-veth 55000
```

It starts our SECC with `--sdp --interface <iface>` so their injector's SDP discovery finds it, then waits.
Start their injector in the other terminal (the `podman_evcc` line above, or `binding-start-evcc`).

Or through the fixture, which additionally **records the session** and, with `V2G_INTEROP_SDP`,
answers their injector's discovery itself (this is the combination the 2026-08-06 runs used —
relax the scenario first, see "Reading the verdict"):

```bash
V2G_INTEROP_LISTEN=55000 V2G_INTEROP_SDP=evse-veth V2G_INTEROP_PROTOCOL=2 V2G_INTEROP_MODE=dc \
V2G_INTEROP_RECORD=/tmp/tux-run \
V2G_INTEROP_SCENARIO=/tmp/audi-relaxed.json \
  dotnet test ../../ISO15118ConformanceTests.Simulation -c Release \
    --filter "FullyQualifiedName~TuxEvseInteropTests.TheirInjector"
```

The fixture's listener binds `[::]` dual-stack. An IPv4 wildcard cannot accept a link-local IPv6
connection at all — it waits out its timeout and reports that no car ever came.

### Our EVCC → their responder  ([`live-evcc-iso2-dc.sh`](live-evcc-iso2-dc.sh))

The direction that tests what we **send**, against answers a real charger gave.

```bash
./live-evcc-iso2-dc.sh evcc-veth                       # discover their responder via SDP
./live-evcc-iso2-dc.sh evcc-veth '[fe80::…%evcc-veth]:64109'   # or connect to a known endpoint
```

Or through the fixture:

```bash
V2G_INTEROP_SECC='[fe80::…%evcc-veth]:64109' V2G_INTEROP_PROTOCOL=2 V2G_INTEROP_MODE=dc \
V2G_INTEROP_RECORD=/tmp/tux-run \
V2G_INTEROP_SCENARIO=/usr/share/iso15118-simulator-rs/audi-dc-iso2-compact.json \
  dotnet test ../../ISO15118ConformanceTests.Simulation -c Release \
    --filter "FullyQualifiedName~TuxEvseInteropTests.OurEvcc"
```

**Confirm on first contact:** whether their *responder* answers SDP requests. Their shipped scenario marks
the SDP transaction `injector_only`, so the responder may expect a direct connection to the TCP port in
its log instead. If so, use the second form above.

### Scenario order

Work up, and record each one:

1. **-2 DC, EIM, no TLS** — their shipped `audi-dc-iso2-compact.json`, both directions. Ran
   2026-08-06 (reverse, expect-relaxed): the full session, cable check to welding detection.
2. **-2 AC** — from the captures that exist **only at HEAD**: `afb-test/trace-logs/vw-ac-iso2.pcap`
   and two Porsche Taycan pcaps, through their own converter. Ran 2026-08-06 with the VW capture:
   `--compact=none` preserves the double Authorization poll (and found our sequence-guard gap),
   `--compact=basic` folds it and runs to `SessionStop`.
   ```bash
   pcap-iso15118 --pcap_in=afb-test/trace-logs/vw-ac-iso2.pcap --json_out=vw-ac.json --compact=basic
   ./scenario-relax.py vw-ac.json vw-ac-relaxed.json --autorun
   ```
3. **TLS.** Ran 2026-08-06 (`←SECC`), and the expectation that the time would go on cipher-suite
   alignment rather than ISO 15118 was right — for a sharper reason than anticipated. Their PKI is
   `mkcerts.sh`'s (ECDSA P-256; root → `_server` sub-CA → `_client`/`_contract` ends), and the
   station-side material is **theirs**: our SECC presents their `_server.pem`, which their EVCC
   trusts by construction.

   ```bash
   sudo cp iso15118-simulator-rs/afb-test/certs/templ-*.cfg /usr/share/iso15118-simulator-rs/
   bash iso15118-simulator-rs/afb-test/certs/mkcerts.sh -i ~/tux-evse/run/pki   # needs gnutls-bin
   cd ~/tux-evse/run/pki && openssl pkcs12 -export -inkey _server_key.pem -in _server.pem \
       -certfile _root.pem -passout pass:interop -out server.pfx
   ```

   Then `V2G_INTEROP_TLS_SERVER=<pfx>[:password]` on the reverse fixture — it derives the listener's
   TLS, what SDP advertises, and the recording name from that one value. **Their profile contains
   neither cipher suite ISO 15118-2 prescribes**, so a profile-pinned handshake ends in `no shared
   cipher`; `V2G_INTEROP_TLS_SUITES=platform` unpins deliberately to reach the layers above, and
   `V2G_INTEROP_TLS_REQUIRE_CLIENT=1` makes their car show its `CN=eMaid` certificate. Read
   [`2026-08-06-tux-tls`](../../docs/interop-runs/2026-08-06-tux-tls/notes.md) before running it: a
   second wall on their side stops every shipped scenario four exchanges in.
4. **-20 / DIN** — not yet possible on their side (-20); DIN is theirs alone until our stack
   speaks it.

---

## Recording a run

Set `V2G_INTEROP_RECORD=<dir>` on any of the fixtures. Every run leaves:

| File | What it is |
|---|---|
| `*.ev-to-station.bin`, `*.station-to-ev.bin` | the raw octets of each direction, always written |
| `*.frames.log` | frame by frame: index, **message name**, response code, payload type, length, hex — including any trailing bytes that never became a frame |
| `*.flow.md` | the session as a flow: the paired sequence, every response code that was not OK, and the comparison against `V2G_INTEROP_SCENARIO` when one was given |
| `*.trace.json` | a `SessionTrace`, **if** the session was well-formed enough to be one |
| `*.trace-not-built.txt` | why it was not, when it was not |

The point of the split is that the run which *fails* is the interesting one, and it is exactly the run
whose recording a strict corpus builder refuses. Bytes first, always — and the frame log and the flow
report are built from the frames rather than from the trace, so a session that stopped in the middle
still says which message it stopped on. That was the first thing this harness got wrong: the artifact
a failed run leaves behind was the one without names.

A `trace.json` from a run is not just an artifact: it is in the format all four back ends replay, so a
session captured against their simulator can become a corpus entry that C#, Kotlin, Swift and TypeScript
are all held to from then on — and the conformance fix it produced cannot silently regress. That path is
described in [`../../docs/interop-runs/README.md`](../../docs/interop-runs/README.md).

Write up each run under `docs/interop-runs/<yyyy-mm-dd>-tux-<scenario>/` with their image tag or commit,
ours, the exact commands, and every disagreement — including the ones that turned out to be their
capture's station rather than a defect. Those are worth writing down precisely because the next person
will otherwise re-derive them.

---

## Reading a capture without running anything ([`v2gtp-from-pcap.py`](v2gtp-from-pcap.py))

Their `trace-logs/` are packet captures, and their `pcap-iso15118` turns one into a scenario. Sometimes
the useful thing is smaller than that: the bytes, out of the pcap, per direction, with no rig and no
counterparty process.

```bash
./v2gtp-from-pcap.py path/to/some.pcap 3      # first 3 V2GTP frames per direction, as hex
```

It parses libpcap directly — no scapy, no tshark — reassembles each TCP direction by sequence number
and splits on the V2GTP header. About 100 lines, and worth having for one specific reason:

**The `SupportedAppProtocol` handshake is readable in every capture, whatever protocol follows it.**
That schema is its own document type and is deliberately protocol-independent — it is how the two sides
agree which protocol to speak, so it cannot presuppose one. Our codec decodes it out of a DIN 70121
capture as happily as out of an ISO one, which is how `tesla-3-din.pcap` stopped being unusable
([2026-08-07](../../docs/interop-runs/2026-08-07-tesla-din-handshake/notes.md): a real Tesla offering a
vendor-proprietary protocol at priority 1).

So before writing a capture off as "a protocol we do not speak", take its first two frames.

---

## Afterwards ([`../rig-cleanup.sh`](../rig-cleanup.sh))

```bash
sudo bash tools/rig-cleanup.sh
```

Takes down the binders, the namespace and the run output across all three live counterparties, and
prints what it kept with what a rebuild would cost. Two details in it are load-bearing rather than
tidiness: their binder renames its process (so `pkill -x afb-binder` never matches), and a wedged one
does not answer SIGTERM — the cleanup escalates to `SIGKILL` for the reason written up as
[issue D](../../docs/reports/tux-evse-spin.md). Leaving one behind holds ports 1234/1235/61341 and
makes the next run measure garbage.

---

## Known friction (expect these first)

- **The `expect` blocks are another station's answers, and a mismatch aborts the replay.** See
  "Reading the verdict" above — `scenario-relax.py` is the way through. This is the single largest
  obstacle in the reverse direction.
- **Their binders busy-loop with no backoff — on failure, timeout and idle paths.** Measured
  2026-08-06 at HEAD: the responder retried one refused query match **1.1 M times in 240 s** (572 MB
  of log); the EVCC binding re-decoded a stale buffer **10.9 M times in ~70 s** (2.1 GB) after a
  response timeout, and spun on `pending=None` **7.5 M times in ~25 s** (1.29 GB) after a *completed*
  session. Always cap their binder with `timeout` or it will fill the disk.
- **No `SO_REUSEADDR` on their SDP socket.** Same host, same interface as our SDP server cannot
  work in either bind order — run their binder in a namespace ("One host, two stacks" above).
- **Link-local addressing with zones.** Their whole network model is veth pairs; nothing is on loopback.
  See "Their network model" above.
- **SDP.** Their EV side multicasts SDP before connecting; ours answers it with `--sdp --interface`.
  If the multicast does not cross their bridge, `../interop-josev/sdp-responder.py` is the shim that
  isolates discovery from the session — it is counterparty-agnostic and works here unchanged.
- **Timing.** Their transactions carry the capture's own inter-message delays (`"delay": 111`), and a
  scenario's `timeout` is the capture's total. A session paced by a real car is slower than any loopback
  test; our per-message timeout in the fixture is 5 s for exactly this reason.
- **Firewalld**, on Fedora: their README notes `firewall-cmd --zone=trusted --add-interface=evse-tun`.
- **Their EXI is cbexigen.** A byte disagreement here is far more likely to be a *framing or sequencing*
  difference than a codec one — do not reach for the vector corpus first.
