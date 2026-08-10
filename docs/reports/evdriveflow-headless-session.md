# Draft report to EDF Lab (eVDriveFlow) — the documented no-GUI path cannot complete a session, for two independent reasons

Status: **draft, not sent.** Found 2026-08-06 against `EDF-Lab/eVDriveFlow` @ **`60249c3`**, their
published code **unpatched**, driven by a third-party ISO 15118-20 SECC; confirmed again 2026-08-07
over mutual TLS 1.3 and in a DC_BPT session. Post under your own name; see *Before sending* at the
bottom.

Both issues below are on the path their README describes at line 224 — *"To execute without GUI, run
`start_ev.py`. To stop the session from EV side press Enter in the execution terminal."* — and they are
independent: fixing the first reveals the second. **File them separately.**

Read the short section [*What worked*](#what-worked) before the issues if you are sending this to a
maintainer cold. Three runs of theirs went further than anything else this project has talked to at
-20, and the two defects below are the only things between their EV and a complete charge loop.

Evidence in this repository: [`2026-08-06-edf-stdin-wall`](../interop-runs/2026-08-06-edf-stdin-wall/notes.md),
with both of their EV's own logs side by side
([`their-ev.stdin-eof.log`](../interop-runs/2026-08-06-edf-stdin-wall/their-ev.stdin-eof.log),
[`their-ev.stdin-open.log`](../interop-runs/2026-08-06-edf-stdin-wall/their-ev.stdin-open.log)) and the
rig that produced them; then
[`2026-08-07-edf-mutual-tls13`](../interop-runs/2026-08-07-edf-mutual-tls13/notes.md) and
[`2026-08-07-edf-dc-bpt`](../interop-runs/2026-08-07-edf-dc-bpt/notes.md).

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

**This is the one fix worth doing first.** It is the last thing standing between their EV and a
complete charge loop, and it is the same two lines over plain TCP, over mutual TLS 1.3, and in a
DC_BPT session: three runs, three transports and service catalogues, one `None * int`.

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

<a id="what-worked"></a>

## What worked — worth saying first, and not as politeness

Three separate things this project had never obtained from any other counterparty at ISO 15118-20, all
from their unpatched stack:

- **A second independent codec.** Their OpenEXI/Nagasena path decoded every one of our -20 messages,
  in both directions, across every run — including the DC_BPT envelope. Ours is cbV2G-derived, so the
  two share no ancestry. This project has exactly two such codecs.
- **A mutual TLS 1.3 handshake at -20, verified by somebody else's implementation** rather than by our
  own two sides agreeing with each other: `TLS_AES_256_GCM_SHA384`, both directions authenticated.
- **The first -20 PKI we have met that is the one -20 describes.** Their generator uses
  `secp521r1` on every line, which is simply what the standard prescribes
  (`ecdsa_secp521r1_sha512` or Ed448). We expected that to be the boring half of the run. It was not:
  the other two -20 stacks we test against both ship **P-256** test material — one by choice, one with
  a `TODO` beside it — so until this run every -20 TLS session this project had ever completed was
  carried by -2-grade keys. There is a real pull towards P-256 (Windows' Schannel cannot do P-521 for
  TLS at all, so a test PKI that must work everywhere ends up non-conformant almost by force), which
  makes theirs the exception rather than the default.

We mention the last one because the expired-certificate note above could otherwise read as criticism of
their PKI. It is the opposite: **the PKI is right, and only its clock ran out** — and the 60-day SECC
leaf that expired first is the standard's own requirement, which is exactly why checked-in material
cannot stay valid.

And a fourth, smaller: in the DC_BPT run their EV picked service **6 (DC_BPT)** out of a catalogue that
also offered the unidirectional entry, and exchanged a real bidirectional power envelope — 48 kW / 137 A
of discharge against our station's 50 kW / 200 A, each side's numbers read by the other's decoder
([run notes](../interop-runs/2026-08-07-edf-dc-bpt/notes.md)). That is a working bidirectional
negotiation, and it too ends at issue 2.

---

## Also seen, secondary — not filed here

Three findings from our 2026-08-01 runs, each with its own file and each filable separately. We
worked two of them around **inside a throwaway container**, never in our own stack, purely to see what
lay behind them.

- ~~**`secc/states/process_service_discovery_request.py`** reads `payload.supported_service_ids.service_id`
  unconditionally.~~ **Written up separately on 2026-08-10** and no longer secondary:
  [`evdriveflow-service-discovery-filter.md`](evdriveflow-service-discovery-filter.md), EDF's issue 3.
  It needs no misbehaviour from the other side — every EV that does not pre-filter hits it — and
  counting the family it belongs to is what made it worth its own file.
- ~~**`secc/states/process_dc_charge_loop_request.py:128`** ... a **Scheduled** charge loop raises
  `AttributeError` there.~~ **Overstated, corrected 2026-08-10.** The line is real, but their station
  advertises `ControlMode = 2` (Dynamic) in the only parameter set it offers for either service
  (`evse_dummy_controller.py:109-114`), so a conformant EV never selects Scheduled and never sends one.
  Reaching that line takes a car that ignored the catalogue it was given — which ours did in August,
  before it was taught to read parameter sets. What remains is that malformed input crashes instead of
  being refused, which is not something this project files against anyone.
- **`evcc/states/wait_for_authorization_setup_response.py`** walks the offered authorization services
  and `raise NotImplementedError` on the first one it does not support, even when the one it *does*
  support is the next entry in the same list. A station offering PnC alongside EIM is the ordinary
  case in the field.

---

## Before sending

- [x] **Reproduce it yourself.** Both issues were reproduced on the unpatched published commit, in an
      A/B run whose only difference is what `start_ev.py` gets on file descriptor 0. Issue 2 confirmed
      again 2026-08-07 over mutual TLS 1.3 and in a DC_BPT session — same two lines, three transports.
- [x] **Lead with what worked**, not with politeness at the end — [*What worked*](#what-worked) is
      above the fold for that reason, and every claim in it has a run behind it.
- [ ] **File issues 1 and 2 separately**, and consider the three secondary ones as a third filing —
      two of them are the same `hasattr`/`None` shape on the SECC side and might be fixed in one pass.
      If they can only take one: **issue 2**. It is the last thing between their EV and a complete
      charge loop.
- [ ] **Say what was on the other end.** A third-party ISO 15118-20 SECC in Dynamic control mode, EIM,
      over plain TCP and then over mutual TLS 1.3 — not a fuzzer, and not your own stack talking to
      itself. That is the part that tells a maintainer these are interop defects rather than exotic
      inputs.
- [ ] **Offer the patches only if they want them.** Issue 1 has two reasonable shapes (check the return
      value vs. gate on `isatty`), and issue 2 touches a pattern that may appear elsewhere in the tree —
      both are their call.
- [ ] **Decide whether the P-256 comparison goes in.** Naming what two other projects ship is fair
      — it is public code and it is the reason the observation is interesting — but it is somebody
      else's shortcoming appearing in a report to a third party. Saying "the first -20 PKI we have met
      that uses the curve -20 prescribes" makes the same point without the comparison, if you would
      rather not.
