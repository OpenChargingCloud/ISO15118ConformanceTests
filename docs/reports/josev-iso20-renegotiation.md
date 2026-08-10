# Draft report to SwitchEV — the ISO 15118-20 EVCC asks for renegotiation and then kills its own session

Status: **draft, not sent.** Observed live on 2026-07-22 against **`SwitchEV/iso15118` @ `d645255`**, with
their own error message naming the invariant that broke; source re-read on **2026-08-10** against
upstream `master` and against EVerest's fork **`EVerest/ext-switchev-iso15118` @ `26f7988`**. Post it
under your own name; see *Before sending* at the bottom.

**File it twice**, as with [`josev-iso20-pki-curve.md`](josev-iso20-pki-curve.md): §1 is present in both
trees, §2 only upstream — **the fork has already fixed §2**, and pointing at that fix is half the value
of sending this.

Evidence in this repository:
[`2026-07-22-renegotiation`](../interop-runs/2026-07-22-renegotiation/notes.md) — the run notes, with
[`evcc-reneg-20.log`](../interop-runs/2026-07-22-renegotiation/evcc-reneg-20.log), which is *their* EVCC's
log of the session.

One other report goes to the same project:
[`josev-iso20-pause-resume.md`](josev-iso20-pause-resume.md) (the `-20` session context is never filled,
so a resume degrades to a new session), and
[`josev-iso20-pki-curve.md`](josev-iso20-pki-curve.md) goes to `create_certs.sh` in this tree and in the
fork. They are independent of this one and of each other.

---

## §1 — `SessionStop` sets `next_state = ServiceDiscovery` without building the request that state needs

**Title:** In `evcc/states/iso15118_20_states.py`, the renegotiation branch of
`SessionStop.process_message` assigns `next_state = ServiceDiscovery` but never calls
`create_next_message(...)`, so the framework's own check — *"Field `next_v2gtp_msg` is None but must be
set because next state is not Terminate"* — raises `FaultyStateImplementationError` and the data link is
torn down. `[V2G20-1477]` renegotiation is therefore unreachable from this EVCC even when the station
supports it and answers correctly

**Versions:** upstream `master` (read 2026-08-10) and `d645255` (the commit the live run met). The
EVerest fork `26f7988` carries the same code with a `PAUSE` branch added.

### What we saw

Our SECC advertised `ServiceRenegotiationSupported: true`, ran an AC `-20` session, and put
`EVSENotification: ServiceRenegotiation` in a charge-loop response — `[V2G20-1477]`. Their EVCC did
exactly the right thing and sent the request:

```json
{"SessionStopReq": {"Header": {…}, "ChargingSession": "ServiceRenegotiation"}}
```

Our station answered `OK` **without ending the session**, which is the point of the mechanism. Their
EVCC then logged, 91 ms later:

```
The data link will terminate in 2 seconds and the TCP connection will close in 5 seconds.
Reason: FaultyStateImplementationError occurred while processing message in state SessionStop :
        Field 'next_v2gtp_msg' is None but must be set because next state is not Terminate.
…
terminated the data link
```

That is your own framework refusing your own transition. Everything up to it is correct: the flag is
read, the request is built, the value reaches the wire, the branch is taken.

### Where it comes from

`iso15118/evcc/states/iso15118_20_states.py`, `SessionStop` (upstream `:1124`), in `process_message`:

```python
# :1148-1157
session_stop_reason = self.comm_session.charging_session_stop_v20.lower()
if session_stop_reason == "pause":
    session_stop_action = SessionStopAction.PAUSE
else:
    session_stop_action = SessionStopAction.TERMINATE
self.comm_session.stop_reason = StopNotification(
    True,
    f"Communication session " f"{session_stop_reason}d",
    self.comm_session.writer.get_extra_info("peername"),
    session_stop_action,
)

# :1159-1167
if (
    self.comm_session.service_renegotiation_supported
    and self.comm_session.renegotiation_requested
):
    self.comm_session.renegotiation_requested = False
    self.next_state = ServiceDiscovery          # ← and nothing else
else:
    self.next_state = Terminate
```

**Two things, and the first is the one that fires.**

1. **No next message.** `ServiceDiscovery` is a state that has to be entered *with* a
   `ServiceDiscoveryReq`. There are 28 `create_next_message(...)` calls in this file and every other
   `next_state` assignment is paired with one; this branch is the exception. Hence the error your own
   log names.
2. **The stop was already decided, two lines earlier.** `session_stop_reason` is derived from a
   **two-way** test — `"pause"` or everything else — and `ChargingSession.SERVICE_RENEGOTIATION` falls
   into "everything else", so `SessionStopAction.TERMINATE` is posted before the branch that means *do
   not terminate*. The tell is in the log text the same line produces: **"Communication session
   service_renegotiationd"**. Fixing (1) alone would leave the session marked for termination.

### Why we think it is worth fixing

- **`[V2G20-1477]`** — the SECC signals `ServiceRenegotiation` via `EVSENotification`, and the EVCC ends
  the current service session with `SessionStopReq(ServiceRenegotiation)` in order to **re-enter service
  negotiation**, not to stop charging. Your EVCC performs the whole protocol and then cannot act on it.
- We are citing requirement identifiers and paraphrasing what they oblige rather than quoting; the rule
  is [`docs/normative-basis.md`](../normative-basis.md). `-20` identifier, no document caveat.
- **And because the capability is otherwise complete.** `service_renegotiation_supported` is read from
  `ServiceDiscoveryRes` (`:443-445` upstream), `renegotiation_requested` is set from the charge loop, the
  request carries the right enum value, and the branch exists. One missing call stands between all of
  that and a working renegotiation.

### Suggested direction

1. **Build the request in the branch**, as every other transition in the file does — a
   `ServiceDiscoveryReq` with the session id and a fresh timestamp, through
   `create_next_message(ServiceDiscovery, …, Timeouts.SERVICE_DISCOVERY_REQ, Namespace.ISO_V20_COMMON_MSG, …)`.
2. **Make the stop action three-way**, or set it after the branch. Comparing
   `charging_session_stop_v20` against the enum rather than against the string `"pause"` would also stop
   the log line reading *"service_renegotiationd"*.
3. **Consider asserting the pairing.** A state machine in which `next_state != Terminate` requires
   `next_v2gtp_msg` already knows the rule — your error message is that rule. Checking it where the
   transition is *written* rather than where it is *executed* would have caught this at import time.

---

## §2 — the DC/MCS path cannot even ask (upstream only; the fork has fixed it)

**Title:** `DCWeldingDetection.process_message` builds its `SessionStopReq` with a hardcoded
`charging_session=ChargingSession.TERMINATE`, so on a DC, DC_BPT, MCS or MCS_BPT session the
`SERVICE_RENEGOTIATION` in `charging_session_stop_v20` is discarded before the message is encoded —
and §1 is never even reached

Upstream `:1731` (`class DCWeldingDetection`), the request at `:1764-1769`:

```python
session_stop_req = SessionStopReq(
    header=MessageHeader(…),
    charging_session=ChargingSession.TERMINATE,      # ← the variable is ignored
)
```

The AC path a few hundred lines earlier (`:950-955`) does it correctly:
`charging_session=self.comm_session.charging_session_stop_v20`. `PowerDelivery.process_message` routes DC
and MCS services through `DCWeldingDetection` and everything else straight to `SessionStop`, so the two
power modes take different paths to the same message and only one of them carries the value.

**EVerest's fork already fixed this.** In `EVerest/ext-switchev-iso15118` @ `26f7988` the same
construction (`iso15118/evcc/states/iso15118_20_states.py:1934-1940`) reads
`charging_session=self.comm_session.charging_session_stop_v20`. If the fix is not contentious, it is
sitting there.

**Our live run was AC**, so §2 is a source reading and not an observation; we did not run the DC arm
against a Josev EVCC. §1 is what the AC run demonstrated.

---

## Not part of this

- **`Pause`.** The fork added an `elif … PAUSE` branch to the same function that upstream does not have;
  we have not tested whether that path pairs its `next_state` with a message. It is a different value
  through the same gap and worth a glance while you are in there.
- **Our own side.** The branch that would keep the session alive is gated on
  `service_renegotiation_supported`, which is a flag *our* station sets — so the first thing we ruled out
  was ourselves. Our `ServiceDiscoveryRes` carries `ServiceRenegotiationSupported: true` (it is in their
  own decode line in the log), and our SECC answers `SessionStopRes(OK)` and re-enters service discovery
  rather than terminating.
- **The `-2` renegotiation path**, which works in both directions against this stack and is `✅` in our
  matrix. This is `-20` only.

---

## Before sending

- [x] **Observe it, do not infer it.** Their EVCC's own log carries the request, our `OK`, the
      `FaultyStateImplementationError` and the teardown, 91 ms apart.
- [x] **Rule out our own station.** `ServiceRenegotiationSupported: true` appears in *their* decode of
      *our* `ServiceDiscoveryRes`, and our SECC answers without ending the session — which is what makes
      their branch reachable at all.
- [x] **Check the citations against both trees.** Upstream `master`, read 2026-08-10:
      `iso15118_20_states.py:1124` (`class SessionStop`), `:1148-1157`, `:1159-1167`, `:431`
      (`class ServiceDiscovery`), `:950-955`, `:1731`, `:1764-1769`. Fork `26f7988`: `:1220`,
      `:1244-1253`, `:1255-1265`, `:1934-1940`.
- [ ] **Lead with their own error message.** *"Field `next_v2gtp_msg` is None but must be set because
      next state is not Terminate"* is the whole issue in one line, and it is theirs.
- [ ] **Re-run it before sending.** The live observation is from 2026-07-22 against `d645255`; the code
      is unchanged on `master`, but a fresh session against the current commit would make the report
      current rather than historical. EVerest's `PyEvJosev` is the same code in a wrapper and is the
      cheaper way to do it.
- [ ] **File §1 upstream and in the fork; file §2 upstream only**, and say in the upstream issue that the
      fork already carries the fix.
- [ ] **Do not overstate §2.** Source reading, not observed — our run was AC.
- [ ] **Post under your own name, in your own words.**
