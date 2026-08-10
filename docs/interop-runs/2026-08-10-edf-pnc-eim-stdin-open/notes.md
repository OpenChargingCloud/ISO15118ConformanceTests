# 2026-08-10 — the missing cell: **PnC and EIM, with stdin held open**

**One run, to take one explanation off the table.** Every previous observation of eVDriveFlow's
`NotImplementedError` was made while their `stdin` bug was also active, so a maintainer could
reasonably ask whether the crash was ever anything but that. It is: with stdin held open — the
configuration in which their EV otherwise charges — an `AuthorizationSetupRes` offering **PnC and EIM**
still ends in a traceback, at the line the twenty-second filing names.

```
[2026-08-10 03:57:45,342] INFO (tcp_client): Received AuthorizationSetupRes.
Traceback (most recent call last):
  File "/app/evcc/../evcc/tcp_client.py", line 134, in process_incoming_message
    reaction = self.get_current_state().process_payload(xml_object)
  File "/app/evcc/states/wait_for_authorization_setup_response.py", line 36, in process_payload
    raise NotImplementedError
NotImplementedError
```

| | |
|---|---|
| Counterparty | [eVDriveFlow](https://github.com/EDF-Lab/eVDriveFlow) `60249c3` (2023-04-17, `main`), **unpatched** |
| Their EV | `edf-ev-unpatched` container, started with a fifo on stdin (`run.sh open`) |
| Ours | `EvDriveFlowInteropTests.TheirEvcc`, `-20` DC, Dynamic, plain TCP, **PnC and EIM offered** |
| The one variable | `V2G_INTEROP_NO_PNC` **left unset** — everything else identical to the 2026-08-06 run |
| Artifacts | [`flow.md`](flow.md), [`frames.log`](frames.log), [`their-ev.pnc-eim.log`](their-ev.pnc-eim.log) |

## What they were offered, from their own decoder

```xml
<ResponseCode>OK</ResponseCode>
<AuthorizationServices>PnC</AuthorizationServices>
<AuthorizationServices>EIM</AuthorizationServices>
<CertificateInstallationService>true</CertificateInstallationService>
<PnC_ASResAuthorizationMode><GenChallenge>0jYhfRmqR6bwWUpoOtUlRQ==</GenChallenge></PnC_ASResAuthorizationMode>
```

A legal offer: `[V2G20-2566]` says the SECC may offer EIM, or PnC, or both, and `[V2G20-1219]` is why
the `PnC_ASResAuthorizationMode` challenge is there.

## The three-way contrast, which is the point

Same rig, same commit, one variable per row:

| Run | stdin | Offer | Their EV |
|---|---|---|---|
| [2026-08-01](../2026-08-01-edf-iso20-dc-dynamic-reverse/notes.md) | EOF | PnC, EIM | `NotImplementedError` |
| 2026-08-01, EIM-only control | EOF | EIM | 4 exchanges, clean `SessionStopReq` |
| [2026-08-06](../2026-08-06-edf-stdin-wall/notes.md) | **open** | EIM | **15 exchanges**, into `DC_ChargeLoop` |
| **this run** | **open** | **PnC, EIM** | **`NotImplementedError`** |

The bottom row is the cell nobody had filled. It separates the two defects completely: stdin open is
the configuration that makes their EV work, and the crash happens there anyway.

**A detail worth keeping, because it is a signature.** The stdin wall ends a session *cleanly* — the
state machine is intact and emits `SessionStopReq`, so the flow has four exchanges. This crash kills
the connection mid-handler, so the flow has **three** and no `SessionStopReq` at all:

```
| 0 | SupportedAppProtocolReq | SupportedAppProtocolRes | OK_SuccessfulNegotiation |
| 1 | SessionSetupReq         | SessionSetupRes         | OK_NewSessionEstablished |
| 2 | AuthorizationSetupReq   | AuthorizationSetupRes   | OK                       |
```

Their log carries no *"stop has been requested"* line either — the marker every stdin-wall run has.
Two different failures with two different fingerprints, and this run has the second one only.

## Consequence

The first item on the twenty-second filing's checklist is ticked:
[`reports/evdriveflow-authorization-setup.md`](../../reports/evdriveflow-authorization-setup.md) no
longer rests on a reading of their source for the part that matters. What it says now is *observed*,
in the configuration where the other known bug is not in the way.

## What it took to run at all, and what that cost

Two SECC runs timed out at nothing before this one, both my doing: I waited for the fixture's
`Waiting for their EV` line before handing over, and the 240 s window had gone by the time anybody
could act on it. The third attempt handed over first and waited afterwards, which is the right order
when the other half of the rig is a person.

The rig also had to be rebuilt on the spot. `dockerd` here is started by hand and lives only as long
as the WSL instance; between the check that found it running and the attempt to use it, the instance
was recycled and the daemon went with it — the socket was not permission-denied any more, it was
**gone**. Restarted as root; the `edf-ev-unpatched` image and the `edfnet` network survived, since
those are on disk rather than in the daemon. **Keep a WSL shell open for the duration of an EDF run**
is the practical rule, and it belongs next to *dockerd must be started by hand* rather than being
rediscovered.

## Not a finding, but our own line, and it fired here

```
SAP: ISO 15118-20 on a plain TCP connection — [V2G20-1237] (car) and [V2G20-2356] (station) both
forbid it, Table 5 puts -20 in the TLS 1.3 row alone. This run does it anyway, deliberately; the claim
it supports is not a conformance claim.
```

That line was added to the interop fixtures earlier the same day, after the nineteenth filing found us
on the wrong side of `[V2G20-1237]` against EVerest. This is the first live run to print it, and it is
doing exactly its job: the run is deliberate, the transcript says so, and nobody reading this note in
six months has to work out whether the plain-TCP `-20` session was a conformance claim. It was not.

## Reproduce

```bash
# dockerd by hand, and keep a WSL shell open afterwards
sudo dockerd > /tmp/dockerd.log 2>&1 &

# our station: -20 DC, Dynamic, and NO V2G_INTEROP_NO_PNC, so PnC and EIM are both offered
V2G_INTEROP_LISTEN=55000 V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=dc V2G_INTEROP_DYNAMIC=1 \
V2G_INTEROP_RECORD=/tmp/edf-pnc \
  dotnet test -c Release ISO15118ConformanceTests.Simulation \
    --filter "FullyQualifiedName~EvDriveFlowInteropTests.TheirEvcc"

# their EV, unpatched, stdin held open by a fifo nobody writes to
sudo bash ~/edf/run.sh open
```

`run.sh` is the one from [`2026-08-06-edf-stdin-wall`](../2026-08-06-edf-stdin-wall/run.sh), unchanged.
