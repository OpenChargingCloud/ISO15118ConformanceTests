# 2026-08-06 — eVDriveFlow: the authorization wall was `stdin`, and behind it lies a real one

The open question from [`2026-08-01-edf-iso20-dc-dynamic-reverse`](../2026-08-01-edf-iso20-dc-dynamic-reverse/notes.md)
— *"their EV terminates the session after `AuthorizationSetupRes`, and the root cause inside their state
machine has not been identified"* — settled by reading their state machine, then proved with an A/B run.

**It was never a protocol matter.** Their EV interprets *"stdin is at EOF"* as *"the operator pressed
Enter to stop the session"*. The 2026-08-01 rig started it with `docker exec -d`, which is exactly that.

| | |
|---|---|
| Counterparty | [`EDF-Lab/eVDriveFlow`](https://github.com/EDF-Lab/eVDriveFlow) @ **`60249c3`** — the same commit every earlier run met, **unpatched** this time |
| Ours | conformance suite @ `9db41cc`, `EVSimulatorApp` @ `d7a5ee4` |
| Direction | `←SECC` — their EV against our station, ISO 15118-20 DC, **Dynamic**, EIM only (`V2G_INTEROP_NO_PNC=1`), TLS off |
| Outcome | stdin at EOF: **4 exchanges** (the old wall, reproduced exactly). stdin held open: **15 exchanges**, through `ScheduleExchange`, `CableCheck`, `PreCharge` ×3, `PowerDelivery` and into `DC_ChargeLoop` — where a second, genuine defect of theirs ends it |

Artifacts: `eof.flow.md` / `eof.frames.log`, `open.flow.md` / `open.frames.log`,
`their-ev.stdin-eof.log`, `their-ev.stdin-open.log`, and the rig (`build.sh`, `run.sh`).

## Wall 1, in their source

Four steps, all in their EV, all `60249c3`:

1. **`TCPClientProtocol.__init__` unconditionally starts a keyboard listener** —
   `self.kb_listener = KeyboardListener(self.ainput)` (`evcc/tcp_client.py:49`), which
   `asyncio.create_task`s a job that awaits `ainput()`.
2. **`ainput` is a blocking `readline` in an executor** — `await loop.run_in_executor(None,
   sys.stdin.readline)` then `set_stop()` (`tcp_client.py:210-226`). At EOF `readline()` returns `''`
   **immediately**, so `set_stop()` runs at once: `session_parameters.stop_session = True`.
3. **`process_reaction` then substitutes the message** the state machine built:
   ```python
   # evcc/tcp_client.py:154-158
   elif isinstance(reaction, SendMessage):
       if self.session.session_parameters.stop_session and \
               self.session.is_state_exitable(self.get_current_state()):
           xml_string, message, request = self.build_session_stop_message()
   ```
4. **The first exitable state is the authorization one** — `exitable_states = states[2:-3]`
   (`evcc/ev_session.py:66`), and index 2 is `WaitForAuthorizationSetupResponse`.

So the EV runs normally through SAP and SessionSetup (not exitable), builds a perfectly good
`AuthorizationReq` in `process_payload` — and `process_reaction` throws it away for a `SessionStopReq`.
That is why the EIM-only experiment on 2026-08-01 changed nothing: the offered-services logic had
already done its job correctly.

Their own log said so all along, one line after the connection was made:

```
20:45:43,022 INFO    Sent SupportedAppProtocolReq.
20:45:43,023 WARNING Session stop has been requested.        (tcp_client.py:227)
…
20:45:43,174 INFO    Received AuthorizationSetupRes.
20:45:43,177 INFO    Sent SessionStopReq.
```

## The A/B run

Same image, same scenario, same station; the only difference is what `start_ev.py` gets on file
descriptor 0.

| | stdin at EOF (`docker exec -d`) | stdin held open (a fifo nobody writes to) |
|---|---|---|
| `Session stop has been requested` | 1 ms after connect | never |
| Exchanges | **4** | **15** |
| Ends with | `SessionStopReq` after `AuthorizationSetupRes` | `TypeError` in their charge-loop handler |

The 15-exchange flow (`open.flow.md`), every response code OK:

```
SupportedAppProtocol → SessionSetup → AuthorizationSetup → Authorization →
ServiceDiscovery → ServiceDetail → ServiceSelection → DC_ChargeParameterDiscovery →
ScheduleExchange → DC_CableCheck → DC_PreCharge ×3 → PowerDelivery → DC_ChargeLoop
```

Four of the five capabilities this counterparty was chosen for sat behind that wall. Dynamic control
mode is now exercised end to end, and their EV selected the **BPT** service on the way, so the
bidirectional cell is reachable too.

## Wall 2 — `hasattr` on an optional field that is always present

The session dies on the first `DC_ChargeLoopRes`:

```
File "/app/evcc/states/wait_for_dc_charge_loop_response.py", line 44, in process_payload
    self.controller.data_model.get_bpt_dynamic_dc_clreq_control_mode()
File "/app/evcc/ev_dummy_controller.py", line 187, in get_target_energy
    return self.target_soc * rational_to_float(self.battery_capacity)/100 - self.current_energy
TypeError: unsupported operand type(s) for *: 'NoneType' and 'int'
```

`target_soc` defaults to `80` and is `80` in a freshly constructed `EVEmulator` (checked in their own
container). It becomes `None` two lines earlier, in the handler for **our** response:

```python
# evcc/states/wait_for_dc_charge_loop_response.py:30-33
if hasattr(payload.bpt_dynamic_dc_clres_control_mode, "departure_time"):
    self.controller.data_model.departure_time = payload.bpt_dynamic_dc_clres_control_mode.departure_time
if hasattr(payload.bpt_dynamic_dc_clres_control_mode, "target_soc"):
    self.controller.data_model.target_soc = payload.bpt_dynamic_dc_clres_control_mode.target_soc
```

`bpt_dynamic_dc_clres_control_mode` is an xsdata dataclass: `target_soc` is `Optional[int]` and the
attribute **always exists**, so `hasattr` is always `True`. Ours is `None` — legally: in
`BPT_Dynamic_DC_CLResControlMode` only the EVSE limits are mandatory, and `Secc20Dc` sends
`DepartureTime: null, MinimumSOC: null, TargetSOC: null, AckMaxDelay: null` with the limits filled in.
So their own configured target SOC is overwritten with `None`, and the *next* request build divides by
it.

The guard they need is `is not None`, not `hasattr`. Nothing on our side is wrong here, and nothing
should change: omitting an optional element is what optional means, and a station that always sends
`TargetSOC` would hide this forever.

It is the same shape as [`docs/assumed-values-sweep.md`](../../assumed-values-sweep.md), seen from the
other side: **their** code assumed a value the protocol makes optional, and it took a peer that omits
it to find out.

## How to reproduce

```bash
# their EV, unpatched, from the published commit
bash build.sh                       # python:3.8 + their pins + a JRE for Nagasena

# an IPv6-capable docker network (their EV multicasts SDP over IPv6 or dies with ENETUNREACH)
docker network create --ipv6 --subnet fd00:edf::/64 --subnet 172.30.0.0/16 edfnet

# our station: -20 DC, Dynamic, EIM only
V2G_INTEROP_LISTEN=55000 V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=dc V2G_INTEROP_DYNAMIC=1 \
V2G_INTEROP_NO_PNC=1 V2G_INTEROP_RECORD=/tmp/edf \
  dotnet test -c Release ISO15118ConformanceTests.Simulation \
    --filter "FullyQualifiedName~EvDriveFlowInteropTests.TheirEvcc"

bash run.sh eof     # the old result: 4 exchanges
bash run.sh open    # the new one:   15
```

`run.sh open` is the whole trick, and it is one redirection:

```bash
mkfifo /tmp/kb; (sleep 600 > /tmp/kb &); python3 start_ev.py < /tmp/kb
```

An interactive `docker exec -it` would do as well. What must not happen is stdin at EOF.

## Next

- **Report wall 2** — a two-line fix (`is not None` on both copies) and it blocks their own charge
  loop against any station that omits the optional fields.
- **Wall 1 is a usability report at most**, but a sharp one: their EV is unusable headless, silently,
  and the failure looks like a protocol decision rather than a closed file descriptor. A check for
  `sys.stdin.isatty()` before arming the listener would end it.
- **Now reachable**, with the fifo in place: mutual TLS 1.3 (their `SECURITY_PROTOCOL` switch back to
  `0x00`), the DC-BPT cell their EV already selects, and — past wall 2 — a complete charge loop.
