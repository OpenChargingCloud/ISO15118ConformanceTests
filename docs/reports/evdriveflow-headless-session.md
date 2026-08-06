# Draft report to EDF Lab (eVDriveFlow) — the documented no-GUI path cannot complete a session, for two independent reasons

Status: **draft, not sent.** Found 2026-08-06 against `EDF-Lab/eVDriveFlow` @ **`60249c3`**, their
published code **unpatched**, driven by a third-party ISO 15118-20 SECC. Post under your own name; see
*Before sending* at the bottom.

Both issues below are on the path their README describes at line 224 — *"To execute without GUI, run
`start_ev.py`. To stop the session from EV side press Enter in the execution terminal."* — and they are
independent: fixing the first reveals the second. **File them separately.**

Evidence in this repository: [`2026-08-06-edf-stdin-wall`](../interop-runs/2026-08-06-edf-stdin-wall/notes.md),
with both of their EV's own logs side by side
([`their-ev.stdin-eof.log`](../interop-runs/2026-08-06-edf-stdin-wall/their-ev.stdin-eof.log),
[`their-ev.stdin-open.log`](../interop-runs/2026-08-06-edf-stdin-wall/their-ev.stdin-open.log)) and the
rig that produced them.

---

# Issue 1 — with stdin not a terminal, the EV stops the session it just started

**Title:** `start_ev.py`: EOF on stdin is treated as "Enter pressed", so a headless EV terminates every
session at `AuthorizationSetup`

**Version:** `60249c3`, Python 3.8.10, run in a container against a third-party SECC (plain TCP,
`SECURITY_PROTOCOL = 0x10`).

## Summary

Started the documented way but without a terminal on stdin — `docker exec -d`, `nohup`, a systemd unit,
a CI job — the EV performs SupportedAppProtocol and SessionSetup normally and then sends
`SessionStopReq` instead of `AuthorizationReq`. Their own log, timestamps unedited:

```
20:45:43,022 INFO    Sent SupportedAppProtocolReq.
20:45:43,023 WARNING Session stop has been requested.        (tcp_client.py:227)
20:45:43,096 INFO    Received SupportedAppProtocolRes.
20:45:43,151 INFO    Sent SessionSetupReq.
20:45:43,170 INFO    Sent AuthorizationSetupReq.
20:45:43,174 INFO    Received AuthorizationSetupRes.
20:45:43,177 INFO    Sent SessionStopReq.
```

The stop is requested **one millisecond after the connection is made**, before any protocol decision.

## Where it comes from

1. `TCPClientProtocol.__init__` arms the listener unconditionally —
   `self.kb_listener = KeyboardListener(self.ainput)` (`evcc/tcp_client.py:49`).
2. `ainput` awaits a blocking read in an executor and then requests the stop:
   ```python
   # evcc/tcp_client.py:210-226
   await asyncio.get_event_loop().run_in_executor(None, sys.stdin.readline)
   await self.set_stop()          # -> session_parameters.stop_session = True
   ```
   At EOF `readline()` returns `''` **immediately**. Nothing distinguishes that from a keypress.
3. `process_reaction` then replaces whatever the state machine built:
   ```python
   # evcc/tcp_client.py:154-158
   if self.session.session_parameters.stop_session and \
           self.session.is_state_exitable(self.get_current_state()):
       xml_string, message, request = self.build_session_stop_message()
   ```
4. The first exitable state is the authorization one — `exitable_states = states[2:-3]`
   (`evcc/ev_session.py:66`), index 2 being `WaitForAuthorizationSetupResponse`.

So the `AuthorizationReq` your `WaitForAuthorizationSetupResponse.process_payload` correctly builds is
discarded, and the session ends four exchanges in.

## Why we think it is worth fixing

- **It is the documented headless path**, and the failure is silent: there is no error, the session
  ends "cleanly", and to anyone watching the wire it looks like a deliberate protocol decision by the
  EV. We spent two interop runs and a documented experiment on 2026-08-01 trying to explain it as an
  authorization-services problem before reading the source.
- **Every non-interactive way of starting it is affected** — containers, service managers, CI.
- One-character difference in outcome: with stdin held open and *nothing else changed*, the same
  unpatched build ran **15 exchanges** instead of 4, through ScheduleExchange, CableCheck, PreCharge,
  PowerDelivery and into the DC charge loop.

## Suggested fix

The narrow version checks what `readline` returned — a keypress is a line, EOF is not:

```python
line = await asyncio.get_event_loop().run_in_executor(None, sys.stdin.readline)
if line == "":                    # EOF: no terminal, or stdin closed — not a stop request
    return
await self.set_stop()
```

Optionally, skip arming the listener when `not sys.stdin.isatty()` and say so in a log line, so the
absence of the "press Enter" feature is visible rather than implied.

---

# Issue 2 — the charge loop overwrites its own target SOC with the station's *omitted* optional field

**Title:** `wait_for_dc_charge_loop_response.py`: `hasattr` is always true on an xsdata `Optional[...]`
field, so a station that omits `TargetSOC` sets `data_model.target_soc = None` → `TypeError`

**Version:** as above. Reached only after issue 1 is worked around.

## Summary

On the first `DC_ChargeLoopRes`, the EV dies while building its next request:

```
File "/app/evcc/states/wait_for_dc_charge_loop_response.py", line 44, in process_payload
    self.controller.data_model.get_bpt_dynamic_dc_clreq_control_mode()
File "/app/evcc/ev_dummy_controller.py", line 187, in get_target_energy
    return self.target_soc * rational_to_float(self.battery_capacity)/100 - self.current_energy
TypeError: unsupported operand type(s) for *: 'NoneType' and 'int'
```

`target_soc` is `80` in a freshly constructed `EVEmulator` (we checked in your own container). It
becomes `None` fourteen lines earlier, in the handler for the station's response:

```python
# evcc/states/wait_for_dc_charge_loop_response.py:30-33
if hasattr(payload.bpt_dynamic_dc_clres_control_mode, "departure_time"):
    self.controller.data_model.departure_time = payload.bpt_dynamic_dc_clres_control_mode.departure_time
if hasattr(payload.bpt_dynamic_dc_clres_control_mode, "target_soc"):
    self.controller.data_model.target_soc = payload.bpt_dynamic_dc_clres_control_mode.target_soc
```

`bpt_dynamic_dc_clres_control_mode` is an xsdata dataclass whose `target_soc` is `Optional[int]`: the
attribute **always exists**, so `hasattr` is always `True`. When the station legitimately omits the
element, the EV copies `None` over its own configured value — and the next request build multiplies by
it.

## Why we think it is the station's right to omit it

In `BPT_Dynamic_DC_CLResControlMode` (ISO 15118-20 Ed. 1), only the EVSE limits are mandatory;
`DepartureTime`, `MinimumSOC`, `TargetSOC` and `AckMaxDelay` are optional. The station we ran against
sends the limits and omits those four, which is why this surfaced at all — a station that always
populates `TargetSOC` hides the defect indefinitely.

We are not asking anyone to send more than the schema requires. We are reporting that the EV cannot
survive a station that sends exactly what it must.

## Suggested fix

```python
mode = payload.bpt_dynamic_dc_clres_control_mode
if mode is not None:
    if mode.departure_time is not None:
        self.controller.data_model.departure_time = mode.departure_time
    if mode.target_soc is not None:
        self.controller.data_model.target_soc = mode.target_soc
```

`hasattr` on generated dataclasses is worth a grep beyond this file: the same pattern would silently
null any other configured value the peer leaves out.

---

## Also seen — the shipped certificates expired in 2022

Not a code defect, but it stops any TLS run today and the error blames the peer. `shared/certificates/`
holds material generated 2022-08-07:

| | expired |
|---|---|
| `seccCert.pem` | **2022-10-06** — 60 days, which is exactly what ISO 15118 asks of a SECC leaf |
| `cpoSubCA2Cert.pem` | 2023-08-07 |
| `cpoSubCA1Cert.pem` | 2026-08-06 |
| `v2gRootCACert.pem` | 2047 |

A fresh clone therefore cannot do TLS in either role, and the first symptom is
`CERTIFICATE_VERIFY_FAILED: certificate has expired` raised by whichever side is verifying — which
reads as the other implementation's problem. The remedy is already in the repository
(`shared/certificates/generateCertificates.sh`); a line next to the TLS instructions saying *"run this
first, the checked-in certificates are short-lived by design"* would be enough. The 60-day SECC leaf is
the standard's requirement, not a choice of theirs — which is precisely why checked-in material cannot
stay valid.

With regenerated certificates, everything worked: TLS 1.3, `TLS_AES_256_GCM_SHA384`, mutual
authentication, secp521r1 on both sides, 15 exchanges
([run notes](../interop-runs/2026-08-07-edf-mutual-tls13/notes.md)). Worth saying in the same issue, so
it reads as "your TLS path is fine, its certificates are stale".

---

## Also seen, secondary — not filed here

Three findings from our 2026-08-01 runs, each with its own file and each filable separately. We
worked two of them around **inside a throwaway container**, never in our own stack, purely to see what
lay behind them.

- **`secc/states/process_service_discovery_request.py`** reads `payload.supported_service_ids.service_id`
  unconditionally. `SupportedServiceIDs` is optional and omitting it means *"no filter, list
  everything"*; an EV that omits it — legally — takes your SECC down with
  `AttributeError: 'NoneType' object has no attribute 'service_id'`. **Same shape as issue 2, on the
  station side.**
- **`secc/states/process_dc_charge_loop_request.py:128`** reads
  `payload.dynamic_dc_clreq_control_mode.evmaximum_charge_current` without checking which control mode
  the request carries. A **Scheduled** charge loop — which your own `ScheduleExchange` had just
  answered `OK` — raises `AttributeError` there.
- **`evcc/states/wait_for_authorization_setup_response.py`** walks the offered authorization services
  and `raise NotImplementedError` on the first one it does not support, even when the one it *does*
  support is the next entry in the same list. A station offering PnC alongside EIM is the ordinary
  case in the field.

---

## Before sending

- [x] **Reproduce it yourself.** Both issues were reproduced on the unpatched published commit, in an
      A/B run whose only difference is what `start_ev.py` gets on file descriptor 0.
- [ ] **File issues 1 and 2 separately**, and consider the three secondary ones as a third filing —
      two of them are the same `hasattr`/`None` shape on the SECC side and might be fixed in one pass.
- [ ] **Say what was on the other end.** A third-party ISO 15118-20 SECC in Dynamic control mode, EIM,
      plain TCP — not a fuzzer, and not your own stack talking to itself. That is the part that tells a
      maintainer these are interop defects rather than exotic inputs.
- [ ] **Offer the patches only if they want them.** Issue 1 has two reasonable shapes (check the return
      value vs. gate on `isatty`), and issue 2 touches a pattern that may appear elsewhere in the tree —
      both are their call.
- [ ] **Thank them for the codec.** Their OpenEXI/Nagasena path decoded every one of our -20 messages
      without complaint across both directions; it is one of only two independent codecs this project
      has, and that is worth saying in the same breath as the defects.
