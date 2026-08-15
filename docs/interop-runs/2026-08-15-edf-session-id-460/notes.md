# 2026-08-15 — eVDriveFlow's SECC serves a whole DC session under a SessionID it never issued

[The `[V2G20-460]` filing](../../reports/evdriveflow-session-id.md) was written from their source on
2026-08-11 and its first checklist line said what was missing: *"Run it against your station. Expect
`AuthorizationSetupRes(OK)` where EVerest answers `FAILED_UnknownSession`."* This takes it, and their
station goes considerably further than the line predicted.

| | |
|---|---|
| Counterparty | [eVDriveFlow](https://github.com/EDF-Lab/eVDriveFlow) **`60249c3`** (2023-04-17), still `origin/main` — their SECC, `-20` DC, plain TCP |
| Ours | our EVCC through `EvDriveFlowInteropTests`, three arms differing in one variable |
| Outcome | **ten message types answered `OK` under a foreign SessionID**, `PowerDelivery` among them |

## What their station did

Three arms, one variable — the SessionID our car puts in every request after `SessionSetup`:

| arm | our EV sends | their station |
|---|---|---|
| **control** | the id it was issued | 12 responses, all `OK`, to the charge loop |
| **zero** | eight zero bytes | **12 responses, all `OK`**, to the charge loop |
| **foreign** | `DEADBEEFDEADBEEF` | **12 responses, all `OK`**, to the charge loop |

The three sessions are the same shape message for message — same names, same response codes, same
lengths — and differ only in the session's own id and its timestamps. `[V2G20-460]` asks for
`FAILED_UnknownSession` on ten of those twelve; it appears nowhere.

**Their own log is the whole report**, because it prints the id it received next to the id it answers
with:

```
XML message received: …<s2:Header><s2:SessionID>DEADBEEFDEADBEEF</s2:SessionID>…</s1:PowerDeliveryReq>
Received PowerDeliveryReq.
XML message to be sent:  …<ns1:SessionID>3432363539393930</ns1:SessionID>…
                            <ns1:ResponseCode>OK</ns1:ResponseCode>
```

`3432363539393930` is their own id for that session — the ASCII digits `42659990`, which is the
[entropy finding](../2026-08-15-edf-session-id-entropy/notes.md) visible in passing. The value they
never compared is in their debug output, one line above the answer that ignores it.

## Which handlers this reaches, exactly

Their `secc/states/` holds **fifteen** `process_*_request.py`, of which `session_setup` and
`supported_app_protocol` are the two the rule excludes — so **thirteen** are answerable for
`[V2G20-460]`. The wrong-id arms reached eleven of the thirteen and were answered on ten:

`AuthorizationSetup` · `Authorization` · `ServiceDiscovery` · `ServiceDetail` · `ServiceSelection` ·
`DC_ChargeParameterDiscovery` · `ScheduleExchange` · `DC_CableCheck` · `DC_PreCharge` ·
**`PowerDelivery`** — ten, every one `OK`. The eleventh, `DC_ChargeLoopReq`, was received and killed
their station for an unrelated reason (below). `DC_WeldingDetection` and `SessionStop` were never
reached, and this note does not claim them.

**`PowerDelivery` is the one to lead with.** It is not an opening handshake message: `ChargeProgress =
Start` is the request that closes the contactor, and their station answered it `OK` to a peer whose
session id was invented. A run under a foreign id is not stopped early — it charges.

## The source, re-read at the running revision

Not from the checkout this time but from inside the container that answered:

```
grep -rn "payload.header" /app/secc/     →  (nothing)
grep -rn "MessageHeaderType(" /app/secc/states/
    14 hits, 13 of the form MessageHeaderType(self.session_parameters.session_id, …)
    process_session_setup_request.py alone uses the id it has just minted
UnknownSession  →  only in shared/xml_classes/… and the XSD; in no handler
```

So the filing's *"fifteen handlers, none compares"* is confirmed against the code that was running,
which is a stronger statement than the same grep over a clone.

## Two things that would have made this measurement wrong

**The first control failed earlier than the probes, and it was not the SessionID.** Their station's
first session after a container start answers `DC_CableCheckRes (FAILED)` — the virtual isolation test
is not ready — so the control stopped at nine messages where the two probe arms reached thirteen. Read
as it stood, that is *"the wrong id got further than the right one"*, which is nonsense. The control
was therefore **re-run after both probes** (`filtered-control2`) and is then identical to them, message
for message. Arms taken at different times against a stateful peer are not arms; ordering is a variable
until it is shown not to be.

**Our own knob was not wired, and the failure mode was silence.** `EvDriveFlowInteropTests` passed four
of the eleven parameters `InteropSession.RunEvccAsync` takes — the same gap the Josev fixture had that
morning — so `V2G_INTEROP_SESSIONID` would have gone nowhere and the session would have completed
**exactly as it does now**. Against a station that ignores the field, a broken probe and a real finding
produce the same log. The [guard added the same day](../2026-08-15-josev-charge-loop-timeout/notes.md)
catches this class and named the variable before the first arm ran.

## What the run cost in our own code, and what it found there

Four fixes, and the first two are the same defect in two more places:

| | |
|---|---|
| `EvDriveFlowInteropTests` | passed 4 of 11 parameters; `TuxEvseInteropTests` the same. Both now pass all of them |
| `InteropSession.RunEvccAsync` | the **`-2` branch dropped `sendSessionId` entirely** — `Evcc2.SendSessionId` has existed since 2026-08-11 and nothing set it, which is why the EVerest `-2` measurement of that day had to be taken with a raw Python probe. Wired, and the two `-20`-only knobs are now *refused* on `-2` rather than dropped |
| `InteropEnvironment.SendSessionId` | a malformed value returned `null`, i.e. **sent the real id** — a typo would have produced a complete session and read as *"their station accepts anything"*. Now refused |
| `InteropEnvironment.Read` | the guard's **first live firing was a false alarm**: `V2G_INTEROP_RECORD` is read by `InteropRecording`, which went to `Environment` directly, so a run that wrote four artifacts was told the variable had been ignored. Three such call sites now go through `Read` |

That last one is worth keeping. A guard that cries wolf is worse than no guard, because the warning it
gets wrong is the one that teaches everybody to skim past the next.

## The instrument, checked from the bytes rather than from our own intent

Against a station that never compares the id, *our EV sent the wrong bytes* cannot be inferred from the
session — so it is read back out of the recorded frames with no decoder, the same one-bit shift the
entropy run used, now in
[`session-id-from-frames.py --requests`](../../../tools/interop-evdriveflow/session-id-from-frames.py):

```
filtered-foreign.frames.log
  station issued: 42659990
  AuthorizationSetupReq   --expect (deadbeefdeadbeef) at bit offset 1, byte 11
  …
  DC_ChargeLoopReq        --expect (deadbeefdeadbeef) at bit offset 1, byte 11      (11 of 11)
filtered-control2.frames.log
  AuthorizationSetupReq   the station's own id (3633353439333934) at bit offset 1, byte 11
```

## Getting past their fifth message at all, which is a second finding

Every forward session this project has ever driven against their SECC ended at `ServiceDiscoveryReq`,
and [that is filed](../../reports/evdriveflow-service-discovery-filter.md): they dereference the
optional `SupportedServiceIDs` unconditionally. Our EVCC could not send the element — `Evcc20Base`
passed the literal `null` — so the only way past it was to build the filter, which is
`Evcc20Base.SupportedServiceIds` and `V2G_INTEROP_SERVICE_IDS=2,6`. **With the element present their
station runs the whole DC sequence**, which is how ten handlers were measured instead of three: the
unfiltered arms in [`sessions/`](sessions/) are the three-handler version of the same three arms.

Both settings are conformant, and the requirement side was read before the code was written: Table 38
of `[V2G20-1248]` marks the element optional and describes it as a filter the EV *may* use, omission
meaning *all services*; the filtered-list sentence in Table 39 (`[V2G20-1249]`) is attached to
`VASList`, not to `EnergyTransferServiceList`. So our own station, which lists its whole catalogue
either way and sends no VAS list at all, is **not** in the wrong — recorded in
[`normative-basis.md`](../../normative-basis.md), because the obvious next thought is that we owe
filtering and we do not.

**And it found a third instance of their own optional-element defect**, one message past the charge
loop's start: `process_dc_charge_loop_request.py:114` reads `payload.display_parameters.present_soc`,
and `DisplayParameters` is optional and omitted by our car, so their station dies with
`AttributeError: 'NoneType' object has no attribute 'present_soc'` — the same shape as the
`SupportedServiceIDs` one, in a second file, on the wire. Added to that report rather than filed
separately, because it is the same one-line pattern and a maintainer fixing one will see the other.

## Artifacts

[`sessions/`](sessions/) — seven frame logs: `unfiltered-{control,zero,foreign}` (three handlers each)
and `filtered-{control,zero,foreign,control2}` (ten). [`their-secc.log`](their-secc.log) — their full
station log across all seven sessions. [`their-secc.sessionids.log`](their-secc.sessionids.log) — the
readable extract: which id came in with which message, which went out.

Offline gate: **1 409 green**, four assemblies, exit code 0. (1 405 + the four
[`Iso20ServiceFilterTests`](../../../ISO15118ConformanceTests.Simulation/E2E/Iso20ServiceFilterTests.cs);
two of them fail when the `ServiceDiscoveryReq` plumbing is put back to `null`, checked by putting it
back.)

## Reproduce

```bash
docker run -d --name edf-secc --network edfnet edf-ev-unpatched \
    sh -c "cd /app/secc && python3 start_evse.py > /tmp/secc.log 2>&1; sleep infinity"
docker exec edf-secc ss -lnt          # their log names neither the address nor the port

SECC='[fd00:edf::2]:49152' OUT=/tmp/edf-460 SERVICE_IDS=2,6 \
  bash tools/interop-evdriveflow/session-id-arm.sh ~/i15118
```

Run the control **last as well as first**, and do not skip `SERVICE_IDS` unless three handlers is the
question. Their station takes all four sessions without a restart.

## Next

- **Nothing for this filing.** Its measurement box is closed and the remaining items are a person's:
  issue or PR, and posting it under their own name.
- The two handlers not reached — `DC_WeldingDetection` and `SessionStop` — are behind their
  `display_parameters` crash. Reaching them needs our car to send `DisplayParameters`, which is another
  optional element it cannot send, and is worth its own decision rather than being smuggled in here.
