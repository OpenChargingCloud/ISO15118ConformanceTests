# 2026-08-01 — eVDriveFlow, reverse: their EV against our SECC, Dynamic control mode attempted

**The Dynamic run did not happen.** Their EV terminates the session at the authorization step, so
ScheduleExchange — where the control mode is actually chosen — was never reached, and our SECC's
`--dynamic` never had anything to do.

What the run *did* produce: the first live SDP discovery against this project, the first `trace.json`
recorded from a real counterparty, a fourth finding, and a good demonstration of why the fixture's
own assertion is the weakest of the three verdicts.

| | |
|---|---|
| Counterparty | [EDF-Lab/eVDriveFlow](https://github.com/EDF-Lab/eVDriveFlow) @ `60249c3` (2023-04-17) |
| Ours | `Vanaheimr.V2G.Exi` @ `12201fb` |
| Direction | their EVCC → our SECC |
| Session | ISO 15118-20, DC, plain TCP, our SECC with `V2G_INTEROP_DYNAMIC=1` |
| Outcome | **green, and that is misleading** — see below |
| Artifacts | [`flow.md`](flow.md), [`frames.log`](frames.log), [`session.trace.json`](session.trace.json), [`Dockerfile`](Dockerfile), [`finding4-workaround.py`](finding4-workaround.py) |

## The session

```
0  SupportedAppProtocolReq   → OK_SuccessfulNegotiation
1  SessionSetupReq           → OK_NewSessionEstablished
2  AuthorizationSetupReq     → OK
3  SessionStopReq(Terminate) → OK
```

## Why "green" is the wrong reading

`TheirEvcc_AgainstOurSecc_RunsToCompletion` **passed**: their EV sent a well-formed
`SessionStop(Terminate)`, our SECC drove to its terminal state, `IsDone` was true. The recorder even
built a `SessionTrace`, because the session was strictly alternating and untruncated.

And nothing was charged. The car said hello, asked how to authorize, and left.

This is precisely the caveat the fixture's own documentation carries — *"a session can end correctly
and still have taken a route no car would take"* — met on its second use. The verdict that carries the
information is [`flow.md`](flow.md), where four exchanges against a sixteen-message reference is
visible at a glance. `IsDone` is necessary and nowhere near sufficient.

## Finding 4 — theirs: an EVSE that offers PnC *and* EIM breaks their EV

Our `AuthorizationSetupRes` advertises both, deliberately, PnC first:

```xml
<AuthorizationServices>PnC</AuthorizationServices>
<AuthorizationServices>EIM</AuthorizationServices>
<CertificateInstallationService>true</CertificateInstallationService>
```

Their `evcc/states/wait_for_authorization_setup_response.py` walks that list and raises
`NotImplementedError` on the first entry it does not itself support — even though EIM, which it does
support, is the very next entry:

```python
for _ in payload.authorization_services:
    if _ in self.controller.data_model.authorization_services:
        ... select EIM ...
    else:
        raise NotImplementedError
```

An EVSE offering PnC alongside EIM is the ordinary case in the field, not an exotic combination.

**What the workaround did and did not establish.** Replacing the `raise` with a `continue`
([`finding4-workaround.py`](finding4-workaround.py)) removed the crash — and their EV then terminated
the session cleanly instead of selecting EIM. Whether that second behaviour was theirs or an artefact
of the patch could not be told apart from the log: their state machine produced `SessionStopReq`
directly, with no error and no `AuthorizationReq` built.

### The clean experiment: an EIM-only offer

Our SECC gained `OfferPlugAndCharge` (CLI `--no-pnc`, fixture `V2G_INTEROP_NO_PNC=1`) so the question
could be settled without patching anything of theirs further. Default unchanged; the corpus and every
recorded session are untouched. It mirrors `PreferDynamicControlMode`, which exists for exactly this
class of reason.

Re-run with `V2G_INTEROP_NO_PNC=1`, their EV received precisely this:

```xml
<AuthorizationServices>EIM</AuthorizationServices>
<CertificateInstallationService>false</CertificateInstallationService>
<EIM_ASResAuthorizationMode/>
```

**And it still sent `SessionStopReq` after `AuthorizationSetupRes`** —
[`flow-eim-only.md`](flow-eim-only.md), [`frames-eim-only.log`](frames-eim-only.log).

Two things follow. The workaround is **exonerated**: the termination is their EV's own behaviour, not
something the patch introduced. And the wall is **not** an offered-services problem — their own
`ev_dummy_controller` configures `authorization_services = [AuthorizationType.EIM]`, our list now
contains exactly and only EIM, and their EV still does not send an `AuthorizationReq`.

Root cause inside their EV: **not identified.** It is somewhere past the list comparison, and further
diagnosis means reading their state machine rather than running interop. Recorded as an open question,
because a guess here would be worth less than the honest gap.

## What worked, and is worth keeping

**SDP discovery, live, for the first time in this project's history against a non-Josev peer.** Their
EV has no fixed-endpoint option at all — `ev_session_handler.start_new_session` connects to
`udp_protocol.tcp_server_address + "%" + interface`, i.e. strictly to whatever the discovery returned.
The counterparty-agnostic shim from the Josev harness answered it unchanged:

```
>>> SDP responder ready on eth0 (ff02::1:15118); advertising [fe80::f844:a1ff:fe55:d3ef%eth0]:15118 (NO_TLS)
>>> answered SDP_Request from fe80::f844:a1ff:fe55:d3ef -> [fe80::f844:a1ff:fe55:d3ef%eth0]:15118
```

That shim plus a `socat` hop is what let a station running **on the Mac** be discovered by an EV
running **in a Linux container**: the responder advertises an address on the container's own link, and
socat forwards that port to `host.docker.internal:55000`. The reverse direction was documented as
needing SDP on a shared L2 — it does, but the station itself does not have to be there.

**The first `trace.json` from a live counterparty.** Four exchanges only, so it is not corpus material,
but it is the format all four back ends replay, produced from a session with somebody else's car.

## How to reproduce

```bash
# our SECC, Dynamic, waiting
V2G_INTEROP_LISTEN=55000 V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=dc V2G_INTEROP_DYNAMIC=1 \
V2G_INTEROP_RECORD=/tmp/dyn V2G_INTEROP_SCENARIO=../../Vanaheimr.V2G.Simulation.Tests/Vectors/Session.iso20-dc-eim.trace.json \
  dotnet test ../../Vanaheimr.V2G.Simulation.Tests -c Release \
    --filter "FullyQualifiedName~EvDriveFlowInteropTests.TheirEvcc" &

# their EV, plus the shim and the hop back to the Mac
docker run -d --name edf-ev --network v2gnet edf-secc sleep infinity
docker exec -d edf-ev socat TCP6-LISTEN:15118,fork,reuseaddr TCP:host.docker.internal:55000
docker exec -d edf-ev python3 /usr/local/bin/sdp-responder.py eth0 15118 notls
docker exec -d edf-ev sh -c "cd /app/evcc && python3 start_ev.py"
```

`numpy` had to join the image for this direction: their `ev_dummy_controller` imports it, while the
headless SECC path does not. Their pinned `1.21.1` has no aarch64 wheel for Python 3.8; pip resolves to
`1.24.4`, the last release supporting 3.8.

## Deviations

In addition to those listed for the forward run
([`../2026-08-01-edf-iso20-dc-notls/notes.md`](../2026-08-01-edf-iso20-dc-notls/notes.md)):

6. Their EV was patched (finding 4) to remove the crash. It did not make the session proceed.
7. Their EV reached our station through a socat hop rather than directly; irrelevant to the messages,
   relevant to the fact that no real link-local session was carried.

## An operational trap worth writing down

The first attempt at the EIM-only run failed in a way that looked like a protocol problem and was not:
**their own SECC container was still on the same network and answered the SDP request first.** Their EV
took that answer, tried to connect to `fd00:beef::2%eth0` — a ULA with a zone, which `getaddrinfo`
rejects — and died before reaching us at all.

For a reverse run, stop their station. Discovery does not know which of two answers you wanted, and on
a shared network the loser is silent about having lost.

(The second attempt then produced its own confusion: a still-running fixture from the previous attempt
held port 55000, so the new one failed with *Address already in use* while the session went to the old
process. The artifacts were right; the directory they landed in was not.)

## Next

- The actual Dynamic run is **still blocked**, and no longer by anything on our side: their EV does not
  get past `AuthorizationSetupRes` even when offered exactly the one service it is configured for.
  ScheduleExchange — where the control mode is chosen — sits behind that.
- Their `process_dc_charge_loop_request.py` assuming Dynamic (finding 2) is the *next* wall after this
  one, not this one.
- Our EVCC cannot drive Dynamic at all (`Evcc20Base` hard-codes `Scheduled_SEReqControlMode`), so the
  forward direction is not an alternative route to the same coverage.
