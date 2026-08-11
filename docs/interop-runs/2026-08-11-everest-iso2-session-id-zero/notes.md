# 2026-08-11 — a SessionID of zero walks past `EvseV2G`'s `[V2G2-460]` check

The [sequence-error probe](../2026-08-11-everest-iso2-sequence-error/notes.md) earlier the same day
found their `-2` station **correct**, and listed what it had not tried:

> `FAILED_UnknownSession` (`[V2G2-460]`) — a wrong SessionID rather than a wrong message. Same probe,
> one more arm, not run.

This is that arm, and this time there is something.

| | |
|---|---|
| Counterparty | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12b8`), `EvseV2G`, `config-dc2-ours.yaml`, plain TCP — and the line is unchanged on upstream `main` (`a22c7e1c`) |
| Ours | [`sidprobe.py`](sidprobe.py), replaying our own recorded `-2` DC frames with one field rewritten |
| Outcome | **`[V2G2-460]` is skipped for SessionID = 0** — the request is served exactly as if it carried the right id. Filed: [`everest-evsev2g-session-id-zero.md`](../../reports/everest-evsev2g-session-id-zero.md) |
| Artifacts | [`probe.correct.log`](probe.correct.log) · [`probe.wrong.log`](probe.wrong.log) · [`probe.zero.log`](probe.zero.log) · [`their-charger.correct.log`](their-charger.correct.log) · [`their-charger.wrong.log`](their-charger.wrong.log) · [`their-charger.zero.log`](their-charger.zero.log) · [`sidprobe.py`](sidprobe.py) |

## The source, read first

```cpp
// modules/EVSE/EvseV2G/iso_server.cpp:79-82
/* [V2G2-460]: check whether the session id matches the expected one of the active session */
*v2g_response_code =
    ((conn->ctx->current_v2g_msg != V2G_SESSION_SETUP_MSG) && (conn->ctx->ev_v2g_data.received_session_id != 0) &&
     (conn->ctx->evse_v2g_data.session_id != conn->ctx->ev_v2g_data.received_session_id))
        ? iso2_responseCodeType_FAILED_UnknownSession
        : *v2g_response_code;
```

The middle conjunct exempts zero. The first already excludes `SessionSetupReq`, which is the message
where zero legitimately means *"new session"*, so the exemption is not protecting that case.

**The order mattered.** The two attempts before this one ran a rig first and found nothing; this began
with two hours of reading and put a prediction on the wire afterwards. That is the cheaper order when
the last two rig runs came back negative.

## Three arms, one variable

Fresh station per arm. Each sends `SupportedAppProtocolReq`, `SessionSetupReq`, then
`ServiceDiscoveryReq` — the message the station is waiting for — differing only in its SessionID.

| arm | SessionID sent | their log | response |
|---|---|---|---|
| **correct** | `8c04a714dff52c76`, the id just issued | *(no failure line)* | 27 B `…91c0 **01** 2004820324` |
| **wrong** | `32612d51ca5f78ee` — one bit flipped | `Failed response code detected for message "Service Discovery", error: Unknown Session` | 27 B `…91c0 **e1** 2004820324` |
| **zero** | `0000000000000000` | *(no failure line)* | 27 B `…11c0 **01** 2004820324` |

`correct` is the baseline: the request is valid, in sequence, and served. `wrong` is the control that
makes the result mean something — the station **does** check, so `zero` being served is not
"this station never looks". `zero` and `correct` produce responses differing in no byte but the echoed
session id.

## The mistake in the first attempt, which is the transferable part

The first run of this probe reported all three arms refused and looked like a clean negative. It was
wrong, and the way it was caught is worth more than the finding.

The probe assumed the SessionID was **byte-aligned** at payload offset 3. Their station's log printed

```
Created new session with id 0x8c04a714dff52c76
```

while the probe read `0x2301...` off the same `SessionSetupRes` — the same value **shifted right by
two bits**. The field is 64 bits at payload **bit** 26. So the "zero" arm had actually sent
`0x…0001`, which is not zero, and was refused entirely correctly.

Two checks now stand in the way of that happening again:

- **In the probe**: `SessionSetupReq` carries the all-zero id that means *new session*, and
  `SessionSetupRes` and the next request carry the same 64 bits as each other. Both are asserted on
  every invocation, and a round trip must be lossless. Independently, the id these recover from our own
  vector is `0a0b0c0d0e0f1011` — which is
  [`SessionTraceCorpusTests.RecordedSessionId`](../../../ISO15118ConformanceTests.Simulation/Traces/SessionTraceCorpusTests.cs)
  verbatim, whereas the byte-aligned read gave a meaningless `0282c3034383c404`.
- **In the run script**: after each arm, the id the probe read off the wire is compared against the id
  the station's own log says it created. All three arms matched.

The lesson generalises past this probe: **when a measurement depends on where a field sits, check the
field position against something the counterparty says out loud.** A negative result from a probe that
is silently pointing two bits to the left is indistinguishable from a conformant peer.

## Where they are right, which the report leads with

- Their **DIN** twin (`din_server.cpp:101-105`) has no such guard.
- Their **ISO 15118-20** implementation has none either — `validate_and_setup_header` is a plain
  equality, applied in ten states.
- Their **sequence** check was measured correct hours earlier.

So this is one conjunct in one file, not a habit — and saying so is what makes the report worth reading.

## And our own side is worse

`FAILED_UnknownSession` appears **nowhere** in our live code: our `-2` station does not implement
`[V2G2-460]` at all, and our `-20` station has no `[V2G20-460]` either. No run of this suite could have
caught either, because our EVCC has no way to send a SessionID other than the one it was given — the
same shape as the MeterInfo gap on 2026-08-10, where a missing question in our car hid a missing answer
in their station. Recorded in [`open-work.md`](../../open-work.md) under *Ours to fix*; it did not block
this filing because the probe is raw Python and owes our state machines nothing.

## Reproduce

```bash
# their station, fresh per arm; EvseV2G logs its TCP port at startup
~/everest/dist/bin/manager --config ~/everest/configs-ours/config-dc2-ours.yaml &
CP_AT_PLUGIN=1 bash ~/everest/sil-car.sh &

python3 sidprobe.py <host> <port> correct     # baseline: served
python3 sidprobe.py <host> <port> wrong       # control:  FAILED_UnknownSession
python3 sidprobe.py <host> <port> zero        # the finding: served
```

Then compare the id the probe prints against `Created new session with id 0x…` in their log. If those
two disagree, no arm means anything.

## Not tested here

- **DIN 70121.** Their DIN path has no guard in the source, so it should refuse a zero id — unverified
  on the wire.
- **The `-20` station.** `validate_and_setup_header` is a plain equality, so `Evse15118D20` should
  refuse it — also unverified.
- **Whether anything downstream of `ServiceDiscovery` behaves differently.** One message was probed;
  the guard sits in a function every ISO-2 message passes through, so the expectation is that all of
  them behave alike, but only this one was measured.
- The `-2` document caveat in [`normative-basis.md`](../../normative-basis.md) applies to
  `[V2G2-460]`: the text to hand is the 2022 DIS revision. `[V2G20-460]` is word-for-word the same
  requirement in the `-20` document, which is some comfort that the 2014 wording was not different.
