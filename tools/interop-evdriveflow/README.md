# eVDriveFlow interop (Tier 2)

Interop between **our** EVCC/SECC and **[EDF-Lab/eVDriveFlow](https://github.com/EDF-Lab/eVDriveFlow)**
(MIT), EDF R&D's Python implementation of **ISO 15118-20 Edition 1**.

**This file is how to run it.** For what has already run and what each session caught — including the
`ResponseCode = FAILED` our EVCC used to ignore, this counterparty's largest contribution — see
[`docs/evdriveflow-cross-validation.md`](../../docs/evdriveflow-cross-validation.md).

Like the other two harnesses this is **opt-in and never part of the offline CI**. The automated hook is
the `[Explicit] [Category("Interop")]` fixture
[`ISO15118ConformanceTests.Simulation/Interop/EvDriveFlowInteropTests.cs`](../../ISO15118ConformanceTests.Simulation/Interop/EvDriveFlowInteropTests.cs),
gated on environment variables — `dotnet test -c Release` skips it entirely.

*Written against their repository as of 2026-08-01. Lines marked **confirm on first contact** could not be
checked from their documentation and are questions for the first run, not statements.*

---

## Why this one, and why now

It goes straight at the combination we have the least outside evidence for: **-20 Ed. 1 + DC
bidirectional power transfer + Dynamic control mode + mutual TLS 1.3**. The app's
[`docs/pki-model.md`](../../libs/EVSimulatorApp/docs/pki-model.md) pins -20 to
TLS 1.3 with a mutual handshake, and until now our own tests have been the only thing that ever checked
we do it right. A second implementation that *requires* it is an oracle rather than a second opinion from
ourselves.

Two further things it has that the others do not:

**Its EXI is OpenEXI** — the Java library, which is why their install needs a JDK. That is a *third*
independent lineage after our cbV2G/cbexigen corpus and Josev's EXIficient, and it means a byte
disagreement here is a real finding. (tux-evse's is cbexigen, so a byte disagreement there is close to
impossible by construction — see [`../interop-tux-evse/README.md`](../interop-tux-evse/README.md).)

**Dynamic control mode drives schedule renegotiation.** Our recorded corpus touches renegotiation only
where we chose to record it, and "where we chose to record it" is exactly the blind spot a second
implementation exists to find. Their EV adjusts power during the session and their station sets departure
time and SoC targets, so a run exercises the paths a fixed schedule never reaches.

**What it will not tell us:** nothing about -2. eVDriveFlow is -20 only, so a `V2G_INTEROP_PROTOCOL=2`
session stops at the SupportedAppProtocol handshake. The -2 counterparties are Josev and tux-evse.

---

## The short path: a TCP relay, no discovery

**Read this before the setup below — for a forward run you may need much less of it.**

After the SupportedAppProtocol handshake an ISO 15118 session is a plain TCP stream. The only part that
needs interfaces, zones and multicast is **SDP**, the discovery step. Everything after it does not care
how it was reached, and the Josev harness has always skipped it, connecting to `host:port` directly.

This counterparty makes the shortcut easier than the others, because **their station's port is
configured rather than ephemeral**: `evse_config.ini` sets `tcp_port = 49152`.

```bash
# on the machine running their SECC
socat TCP6-LISTEN:49152,fork,reuseaddr 'TCP6:[fe80::…%enp0s3]:49152'
```

```bash
# from anywhere that can reach it, including a Mac
./live-evcc-iso20-dc.sh '' vm.local:49152
```

No zone, no multicast, no interface names on our side; `--connect` and `V2G_INTEROP_SECC` take an
ordinary `host:port`, so the whole harness works unchanged.

**What this does not do.**

- **Only the forward direction.** In a reverse run *their* EV is the one discovering, and a relay
  cannot tell it where to look. That direction needs SDP answered on a shared link.
- **SDP is not exercised.** A covered loss: every recorded Josev run drives `--sdp` both ways.
- **TLS is the one place to be careful here** — and it matters more for this counterparty than for the
  others, because mutual TLS 1.3 is its headline feature. A TCP relay is transparent to TLS unless a
  certificate is bound to the address it was reached at; -20 binds identities rather than addresses, so
  it *should* pass, but this is untested and the honest order is: plain TCP through the relay first,
  then TLS on the real topology.

Their station still has to run, so the setup below still applies to *their* side.

## Setup

Nothing here is installed for you. Their stack is Python + a JDK, brought up with conda.

```bash
git clone https://github.com/EDF-Lab/eVDriveFlow && cd eVDriveFlow
conda env create -f environment.yml
conda activate edf15118-20
cd shared/certificates && sh generateCertificates.sh && cd ../..
```

`./prepare-evdriveflow.sh <their-checkout>` checks what is present (conda, a JDK, the environment, the
generated certificates) and prints the next step; it installs nothing and needs no password.

### The three settings that decide whether a run can work at all

| Where | Setting | For interop |
|---|---|---|
| `secc/evse_config.ini` | `[NETWORK] interface`, `tcp_port = 49152` | the interface must be the one we are on; the port is where their SECC listens |
| `evcc/ev_config.ini` | `[NETWORK] interface`, `udp_port = 49153`, `tcp_port = 49154` | same interface; see the SDP question below |
| both | `[SETTINGS] virtual_mode = true` | **check this first.** Their documentation describes it as simulating the communication card. A run against a foreign peer over a real interface is the case it is most likely to get in the way of — **confirm on first contact** |

### TLS

Their default is TLS 1.3 with **mutual** authentication, disabled by editing `SECURITY_PROTOCOL` in
`shared/global_values.py`. Do the first run **without** it: a failed session tells you far less when it
could have been the handshake, and the plain-TCP run establishes that the flow works before the PKI does.

Then turn it on, and expect to spend the time on cipher suites and curves rather than on ISO 15118. Our
side wants the **BouncyCastle** backend for a faithful -20 mutual handshake — `.NET`'s `SslStream` cannot
do TLS 1.3 on macOS at all, and their certificates come from their own `generateCertificates.sh` rather
than from our PKI builder:

```bash
dotnet run --project ../../libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_EVCC -c Release -- \
    --connect '[fe80::…%enp0s3]:49152' --protocol 20 --mode dc \
    --tls-backend bc --pki-dir <dir>
```

**Confirm on first contact:** which curve and suite their GnuTLS/Python side actually negotiates, whether
our root has to be added to their trust store by hand, and whether they do Plug & Charge at all — their
documentation does not mention contract certificates.

---

## Running

Headless entry points exist on both sides, which is what makes repeatable runs possible:
`secc/start_evse.py` and `evcc/start_ev.py` (the GUIs are `evse_gui.py` and `ev_gui.py`, and are worth
having open for the first run — the EV's power slider and the station's departure time are the Dynamic
inputs).

### Their EV → our SECC  ([`reverse-iso20-dc.sh`](reverse-iso20-dc.sh))

The direction that tests what we **accept**, and the one where the control mode matters.

```bash
./reverse-iso20-dc.sh enp0s3 55000
```

It starts our station with `--protocol 20 --mode dc --dynamic --sdp`, then waits. `--dynamic` offers the
Dynamic parameter set **first**, so an EV that takes the first offered set runs a Dynamic session — which
is what their stack is built around. Without it, expect the session to stop shortly after service
selection.

Through the fixture, which records the run and compares the flow:

```bash
V2G_INTEROP_LISTEN=55000 V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=dc V2G_INTEROP_DYNAMIC=1 \
V2G_INTEROP_RECORD=/tmp/edf-run \
V2G_INTEROP_SCENARIO=../../ISO15118ConformanceTests.Simulation/Vectors/Session.iso20-dc-eim.trace.json \
  dotnet test ../../ISO15118ConformanceTests.Simulation -c Release \
    --filter "FullyQualifiedName~EvDriveFlowInteropTests.TheirEvcc"
```

**Confirm on first contact:** how their EV finds an external station. `ev_config.ini` has a `udp_port`,
which suggests SDP, but standard SDP is UDP/15118 multicast to `ff02::1` — if theirs is not, our
`--sdp --interface` advertisement will not be seen and the fallback is
[`../interop-josev/sdp-responder.py`](../interop-josev/sdp-responder.py), which is
counterparty-agnostic, or hard-coding the endpoint on their side.

### Our EVCC → their SECC  ([`live-evcc-iso20-dc.sh`](live-evcc-iso20-dc.sh))

The direction that tests what we **send**, against a station that runs Dynamic mode by design.

```bash
./live-evcc-iso20-dc.sh enp0s3                              # SDP-discover their station
./live-evcc-iso20-dc.sh enp0s3 '[fe80::…%enp0s3]:49152'     # or connect to its configured port
```

The endpoint must be bracketed with the zone inside the brackets. A link-local address without its zone
does not say which interface to use, and the platform's own parsers discard a zone naming an interface
this machine does not have — `V2GEndpoint` refuses those forms rather than connecting to something that
cannot work.

### Scenario order

1. **-20 DC, EIM, no TLS, Dynamic** — both directions. Establishes the flow.
2. **-20 DC BPT** — their headline feature; `V2G_INTEROP_MODE=dc` with our BPT parameter set.
3. **Renegotiation** — the reason this counterparty is worth the setup. Change the departure time or the
   SoC target mid-session on their side and see what our stack does with the new schedule.
4. **Mutual TLS 1.3**, once 1–3 are clean.
5. **Plug & Charge** — only if they do it; see above.

---

## Reading a run

There is no scenario file here to compare against: eVDriveFlow is a state machine, not a replayer. So the
reference for the flow report is **one of our own recorded sessions**, and the comparison answers "did the
live run take the same route as ours" rather than "was it right". Against a Dynamic-mode peer it has every
reason to say no — and the divergence is the result, not a failure.

Every run leaves the usual artifacts (`V2G_INTEROP_RECORD=<dir>`): raw octets per direction, a `frames.log`
with message names and response codes, a `flow.md`, and a replayable `*.trace.json` when the session was
well-formed enough to be one. See [`../../docs/interop-runs/README.md`](../../docs/interop-runs/README.md).

Write each run up under `docs/interop-runs/<yyyy-mm-dd>-edf-<scenario>/` with their commit, ours, the
exact commands, and every divergence — including the ones that turn out to be Dynamic mode behaving as it
should. Those are worth writing down precisely because the next person will otherwise re-derive them.

## Known friction (expect these first)

- **`virtual_mode`.** See the table above. First thing to check when nothing connects.
- **Control mode.** Our SECC offers Scheduled first unless told otherwise; theirs is built around Dynamic.
- **The JDK.** Their EXI is OpenEXI, so a missing or wrong Java is an EXI failure that looks like a codec
  bug. `prepare-evdriveflow.sh` checks for one.
- **Link-local addressing with zones**, as everywhere: write `[fe80::…%iface]:port`.
- **Timing.** A Dynamic session with a real GUI in the loop is slower than anything a loopback sees; the
  fixture's per-message timeout is 5 s and its whole-session budget 3–4 minutes.
