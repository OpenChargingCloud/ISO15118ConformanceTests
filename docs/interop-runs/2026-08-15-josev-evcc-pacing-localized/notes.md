# 2026-08-15 — the ≈0,5 s, localized far enough to change who the report is addressed to

[The pacing filing](../../reports/josev-iso20-evcc-charge-loop-pacing.md) measured **≈532 ms** per AC
charge-loop turnaround on 2026-08-13 and left §4 open in as many words: *"Where the ≈0,5 s actually goes
— we did **not** localize it."* It named one suspect first: every message crosses a py4j gateway into a
JVM, and *"two inter-process round-trips per exchange is a plausible fixed cost of this order."*

**The suspect is exonerated, and the cost is not in the Josev EVCC at all.**

| | |
|---|---|
| Measured | [SwitchEV/iso15118](https://github.com/SwitchEV/iso15118) **`d645255`**, their own EVCC container, against our SECC |
| Scenario | the 2026-08-13 one, unchanged: `-20` AC, EIM, plain TCP, SDP discovery |
| Outcome | **AC charge-loop turnaround: median 43 ms** (min 32, max 58) — against ≈532 ms through EVerest's `PyEvJosev` |

## The codec is not the cost

Josev logs three timestamped lines per message, and between them they bracket the whole of what a peer
experiences as *"the time that side took"*. [`message-timing.py`](../../../tools/interop-josev/message-timing.py)
splits them:

| | their EVCC, `-20` AC | their EVCC, `-20` DC PnC | their EVCC, `-2` AC PnC | their **SECC**, `-20` DC |
|---|---:|---:|---:|---:|
| `SENT` → `Decoded` — peer + read + **decode** | 31 ms | 25 ms | 36 ms | — |
| `Decoded` → to-encode — handling | 1 ms | 1 ms | 1 ms | 0 ms |
| to-encode → `SENT` — **encode** + write | 30 ms | 24 ms | 32 ms | 28 ms |

Medians, four sessions, both roles. **Both codec halves cross the py4j gateway, and both cost tens of
milliseconds** — the same order in their station as in their car, which is what one would expect of a
fixed per-message cost and is an order of magnitude short of what has to be explained.

Their own state machine's handling is **1 ms**.

## And the ≈532 ms is not a property of their EVCC

Same scenario as the run that produced it — `-20` AC, EIM, plain TCP, our station as the peer, SDP
discovery, one host:

| what drives the Josev EVCC | AC charge-loop turnaround |
|---|---:|
| EVerest's `PyEvJosev` module, fork `26f7988` (2026-08-13) | **≈532 ms** |
| their own EVCC container, upstream `d645255` (today) | **43 ms** |

A factor of **twelve**, with the same codec, the same protocol, the same transport and the same peer on
the other end. Whatever the half second is, the Josev EVCC does not have it when it runs by itself.

**So the report is addressed to the wrong project.** It was written to SwitchEV with EVerest's fork named
as a second recipient; on this evidence the measurement belongs to `PyEvJosev` — EVerest — and the
SwitchEV half of it shrinks to nothing.

## Where it stops, and why we did not force it further

The obvious next step is to split the 532 ms the same way, inside the module. That needs their
Josev-internal INFO lines, and they do not exist: `PyEvJosev` installs its own logging handler and
**never sets a level** —

```python
# modules/EV/PyEvJosev/utilities.py:30-42
logging.getLogger().handlers.clear()
handler = EverestPyLoggingHandler()
logging.getLogger().addHandler(handler)
```

— so the root logger stays at Python's default `WARNING` and every `logger.info` in Josev is dropped. A
re-run of the 08-13 scenario today confirmed it: **0 of the expected lines** reached the manager log
while the session negotiated service 1 and charged normally.

**We could patch one line and get them. We deliberately did not**, and the reason is the measurement
itself: turning on INFO logging adds per-message formatting and an MQTT-bound log call to *exactly* the
quantity under measurement. A number obtained that way would be a number about a modified tree, and this
report already has one correction too many to spend credibility on a fourth.

So the honest state is: **not in the codec, not in the EVCC, somewhere in the module or the fork — and
the instrument that would say which is disabled by the module itself.**

## What this changes in the filing

- §4's *"where we would look first"* becomes *"we looked, and it is not there"*, with the split above.
- §4's *"it is not a deliberate pacing"* needs a caveat it did not have. It is true of the fork. It is
  **false of upstream**, which has a charge-loop delay hook the fork does not: `charge_loop_delay()`
  awaited as `asyncio.sleep(delay)` in the `-20` AC and DC loops, the `-2` loop and DIN, logging
  *"Next ChargeLoop Req in N seconds"*. It resolved to **0** in our run, so it caused nothing here — but
  a report that says a mechanism does not exist should not be pointing at a tree where it does.
- The addressee changes, which is the substantive part.

## What is unchanged

The **≈532 ms itself**, and everything §1–§3 concluded from it: 2 of 2 strict runs died on the first
charge-loop pair against our conformant 0,5 s timer, the setup phase cost ≈573–600 ms per exchange in
the same logs, and Table 216 gives the EVCC 0,25 s (`[V2G20-1499]`). The number is not in question. Who
should read about it is.

## Reproduce

```bash
docker run -d --rm --name redis-interop --network host redis:6.2.6-alpine
sed 's/"useTls": true/"useTls": false/' \
  ~/josev-src/iso15118/shared/examples/evcc/iso15118_20/evcc_config_ac.json > /tmp/evcc_ac_plain.json
dotnet WWCP_ISO15118_SECC.dll --listen 55000 --protocol 20 --mode ac --sdp --interface eth0
docker run --rm --network host -e NETWORK_INTERFACE=eth0 -e SECC_ENFORCE_TLS=False \
    -e EVCC_CONFIG_PATH=/tmp/evcc_ac_plain.json -e REDIS_HOST=localhost -e LOG_LEVEL=INFO \
    -v /tmp/evcc_ac_plain.json:/tmp/evcc_ac_plain.json:ro iso15118-evcc:latest
```

```bash
python3 tools/interop-josev/message-timing.py --repeat 'Sent AC_ChargeLoopReq' <their evcc log>
```

**Not port 15118.** Their EVCC refuses an SDP response naming it — *"The port 15118 does not match the
mandatory UDP server port 15118"* — and then raises `AttributeError: 'SDPResponse' object has no
attribute 'ip_address'` inside the `__repr__` of the very message it is refusing. The refusal is theirs
to explain; the crash in the error path is a separate small bug and is **not** filed here.

## Artifacts

[`josev-standalone/`](josev-standalone/) — their EVCC's log, our station's, and the config as run.

Offline gate: **1 405 green**, four assemblies, exit code 0.

## Next

- **Re-aim the filing at EVerest**, or split it. That is a writing decision on a draft that has never
  been sent, and it is the filing's own *"decide fork or upstream first"* box, now answerable.
- **The remaining 480 ms** would need their module to log, which needs a patch that changes what is
  being measured. Whoever picks it up should say which of those two costs they are willing to pay.
