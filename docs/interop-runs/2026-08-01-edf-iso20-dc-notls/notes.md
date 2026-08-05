# 2026-08-01 — eVDriveFlow, ISO 15118-20 DC, EIM, no TLS

**The first live session against a counterparty other than Josev.** Our EVCC against EDF-Lab's SECC.
Thirteen exchanges, four message sets, and three findings — one of them ours, and it is the one worth
reading.

| | |
|---|---|
| Counterparty | [EDF-Lab/eVDriveFlow](https://github.com/EDF-Lab/eVDriveFlow) @ `60249c3` (2023-04-17) |
| Ours | `Vanaheimr.V2G.Exi` @ `2b99137`, `EVSimulatorApp` @ `3efcad7` |
| Direction | our EVCC → their SECC |
| Session | ISO 15118-20, DC, EIM, plain TCP (their `SECURITY_PROTOCOL = 0x10`) |
| Outcome | **aborted at exchange 13**, `DC_ChargeLoopReq` unanswered |
| Artifacts | [`flow.md`](flow.md), [`frames.log`](frames.log), [`Dockerfile`](Dockerfile), [`finding1-workaround.py`](finding1-workaround.py) |

## What worked

```
 0  SupportedAppProtocolReq          → OK_SuccessfulNegotiation
 1  SessionSetupReq                  → OK_NewSessionEstablished
 2  AuthorizationSetupReq            → OK
 3  AuthorizationReq                 → OK
 4  ServiceDiscoveryReq              → OK          (after finding 1)
 5  ServiceDetailReq                 → OK
 6  ServiceSelectionReq              → OK
 7  DC_ChargeParameterDiscoveryReq   → OK
 8  ScheduleExchangeReq              → OK
 9  DC_CableCheckReq                 → FAILED      ← finding 3
10  DC_PreChargeReq                  → OK
11  PowerDeliveryReq                 → OK
12  DC_ChargeLoopReq                 → (no answer) ← finding 2
```

**Every frame we sent was decoded by an independent EXI implementation.** Theirs is OpenEXI
(Nagasena — the jars are in their `shared/lib/`, which is why their install needs a JDK), sharing no
lineage with the cbV2G/cbexigen corpus ours is generated against. Twelve of our -20 messages across
CommonMessages and the DC set were read without a single decoding complaint, and their responses
round-tripped through our decoder the same way. That is the second independent codec cross-check this
project has, after Josev's EXIficient, and the first for -20 at session level.

## Finding 1 — theirs: an optional element dereferenced

`secc/states/process_service_discovery_request.py` reads `payload.supported_service_ids.service_id`
unconditionally. In ISO 15118-20 that element is optional:

```xml
<xs:element name="SupportedServiceIDs" type="ServiceIDListType" minOccurs="0"/>
```
(`V2G_CI_CommonMessages.xsd`, `ServiceDiscoveryReqType`)

Omitting it means "no filter, list everything". Our EVCC omits it — which is legal — and their session
dies:

```
AttributeError: 'NoneType' object has no attribute 'service_id'
```

Worked around in *their* copy inside the throwaway container to get past it; see
[`finding1-workaround.py`](finding1-workaround.py). **Our stack was not changed.**

Note on the workaround itself, because it produced a false finding first: the fallback has to be `[2]`
(plain DC), not `[6, 2]`. Choosing 6 makes their `DC_ChargeParameterDiscovery` handler read BPT-only
fields out of a plain-DC request and fail — a failure caused by the patch rather than by them. A
workaround that manufactures the next error is worse than none.

## Finding 2 — theirs: the charge loop assumes Dynamic control mode

```
secc/states/process_dc_charge_loop_request.py:128
    ev_max_charge_current = payload.dynamic_dc_clreq_control_mode.evmaximum_charge_current
AttributeError: 'NoneType' object has no attribute 'evmaximum_charge_current'
```

`DC_ChargeLoopReq` carries a choice of Scheduled / BPT_Scheduled / Dynamic / BPT_Dynamic control-mode
parameters. Ours sends the **Scheduled** variant; their handler reads the Dynamic one without checking.
Consistent with what `docs/counterparties.md` already says about this counterparty — Dynamic control
mode is what it is built around — but the session up to here negotiated Scheduled and their own
ScheduleExchange answered OK, so the inconsistency is internal to their side.

Not worked around: this is the natural end of a first contact, and the next step is a Dynamic-mode run
(`V2G_INTEROP_DYNAMIC=1` is for our SECC; on this side it means our EVCC choosing Dynamic), not more
patching of theirs.

## Finding 3 — **ours: a FAILED response code is ignored**

The one that justifies the whole harness.

`DC_CableCheckRes` came back with **`ResponseCode = FAILED`** and our EVCC carried on — PreCharge,
PowerDelivery, into the charge loop. The cable-check loop looks only at `EVSEProcessing`:

```csharp
var res = Expect<Dc20.DC_CableCheckRes>(set, message, MessageSet.Iso20DC);
if (res.EVSEProcessing == Dc20.Processing.Finished) break;
```
(`EVSimulatorApp/simulation/Vanaheimr.V2G.Simulation/StateMachines/Iso20/Evcc20Dc.cs`)

And `Expect<T>` checks the message *set* and *type* only:

```csharp
if (actualSet != expectedSet || message is not T typed)
    throw new SessionAborted(...);
```

There is **no `ResponseCode` check anywhere in the -20 EVCC path**. A station can answer FAILED to every
message of a session and our car will drive it to completion.

Why the loopback suite cannot see this: our own SECC never answers FAILED, so the recorded corpus has no
such response and the trace replay has none either. It took a station that says FAILED — for whatever
reason of its own, in virtual mode with no hardware — to make it visible. This is precisely the class
`docs/CONCEPT.md` §1.3 describes: a conformance gap that lives in the state machine, does not announce
itself, and is invisible to any oracle built from our own output.

**Fixed on 2026-08-01, in all three languages.** `Evcc20Base.RefuseOnFailure` sits in the one place
every -20 response passes through (`ExchangeRaw` / `exchangeRaw` / `exchangeRaw`), in C#, Kotlin and
Swift alike. `OK*` and `WARNING*` continue — a warning is explicitly the code for "something is off and
the session goes on" — and `FAILED*` ends the session with the message and the code in the error.

It aborts rather than sending SessionStop: a FAILED response is the station saying it is done, and a
further message invites a second error on a session that already has one.

Each language got the same three tests, because the corpus cannot check any of this — no recorded
response is a failure. Two of them are the behaviour; the third pins the enum's family ordering, since
the check is a range test (`>= FAILED`) and a regenerated enum that interleaved the families would
quietly turn failures back into successes.

**The -2 EVCC had the same hole, and it is closed too** (2026-08-01, same three languages). `Evcc2`
only ever recorded `SessionSetupCode`; nothing else was checked.

-2 needed a different shape: it has no common response base, so every `*ResType` declares its own
`ResponseCode` and there is nothing to pattern-match on. A hand-written switch over the response types
would have been **fail-open** — the one forgotten, or the one added later, goes unchecked, which is the
failure being fixed. So the code is read by property name, and
`Evcc2FailureHandlingTests.EveryResponseTypeIsCheckable` enumerates the generated assembly to prove
that every response type carries one. That test is what makes the reflective read trustworthy rather
than hopeful; the Kotlin and Swift ports use the same read, and all three back ends are emitted from
the same schema plan.

-2 also has only two families: four `OK*` values, then `FAILED` onwards. No `WARNING`.

## How to reproduce

Their `environment.yml` is conda and **linux-64 pinned** (`libgcc-ng`, `ld_impl_linux-64`), so it cannot
resolve on an ARM Mac at all. [`Dockerfile`](Dockerfile) reproduces what matters instead: Python 3.8.10
as they pin it, a JVM for Nagasena, and the subset of their pip list the headless SECC actually imports
(PyQt5/matplotlib/superqt/numpy are GUI-only). `lxml` is unpinned — their `4.6.3` does not build against
current libxml2; `xsdata` stays pinned at `21.8` because it is the data binding the messages are
marshalled through.

```bash
docker network create --ipv6 --subnet fd00:beef::/64 v2gnet
docker build -f Dockerfile -t edf-secc .
docker run -d --name edf-secc --network v2gnet -p 15118:15118 edf-secc
docker exec -d edf-secc socat TCP4-LISTEN:15118,fork,reuseaddr 'TCP6:[fd00:beef::2]:49152'

V2G_INTEROP_SECC=127.0.0.1:15118 V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=dc \
V2G_INTEROP_RECORD=/tmp/edf-run \
V2G_INTEROP_SCENARIO=../../ISO15118ConformanceTests.Simulation/Vectors/Session.iso20-dc-eim.trace.json \
  dotnet test ../../ISO15118ConformanceTests.Simulation -c Release \
    --filter "FullyQualifiedName~EvDriveFlowInteropTests.OurEvcc"
```

Two things this depends on, both documented in [`../../../tools/interop-evdriveflow/README.md`](../../../tools/interop-evdriveflow/README.md):

- **A container needs an IPv6 network.** On the default bridge `eth0` has no IPv6 at all, and their
  `get_tcp_server_address()` reads `netifaces.ifaddresses(iface)[AF_INET6][0]` — a `KeyError` before
  anything starts. `--ipv6 --subnet fd00:beef::/64` gives it one.
- **The relay path is what makes this runnable from a Mac.** No zone, no multicast, no interface names
  on our side: `socat` in front of their station, and our EVCC connects to `127.0.0.1:15118`. SDP is not
  exercised, which is the documented cost.

Re-run twice from a fresh container with identical results. `docker restart` is *not* enough — a
restarted container answered the handshake and nothing else; recreate it.

## Deviations from a clean-room run

Recorded so the next person does not mistake any of them for results:

1. Their code was patched (finding 1) to get past exchange 4.
2. `SECURITY_PROTOCOL = 0x10` — TLS off, their own testing switch. The TLS 1.3 mutual run is the
   interesting one for this counterparty and has not happened.
3. `interface = eth0`, not their `enp0s3`.
4. `lxml` unpinned, `python-dotenv` and the GUI packages omitted.
5. Their station bound a ULA (`fd00:beef::2`) rather than a link-local, because `netifaces` returns the
   first address and the docker network hands out both. Irrelevant to a TCP-only run; it would matter
   for SDP.

## Next

- **Decide on finding 3** — the FAILED family in the -20 EVCC, and in the Kotlin and Swift ports.
- A **Dynamic control-mode** run, which is what this counterparty is actually for (finding 2 is the
  wall in front of it).
- The **reverse** direction — their EV against our SECC — which needs SDP on a shared link and cannot
  use the relay.
- **TLS 1.3 mutual**, the reason this counterparty was picked.
