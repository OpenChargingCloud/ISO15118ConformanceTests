# 2026-08-10 — asking their `-20` station for the meter reading, and getting none

`[V2G20-1081]` gives the EV one way to be told the meter reading: set `MeterInfoRequested` in the
charge-loop request. `[V2G20-1082]` is the station's half — *having been asked, it shall respond with the
`MeterInfo` element*. We asked, in a complete 70-exchange ISO 15118-20 DC session against their SIL, and
**none of the three charge-loop responses carried it**.

Their station's answer is not merely absent. It is **byte-identical to the run in which we did not ask**,
which is the sharpest form this finding takes: the response does not depend on the question.

| | |
|---|---|
| Counterparty | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12b8`), source build in WSL2 Debian 13 |
| Their module | `Evse15118D20` / `libiso15118`, `config-d20-ours.yaml`, plain TCP, DC power supply and their `EvseManager` power meter |
| Ours | `WWCP_ISO15118` EVCC, `-20` DC, Scheduled, `V2G_INTEROP_METER=1` — a switch that did not exist this morning |
| Outcome | **70 exchanges, three charge loops, `MeterInfoRequested = true` on every one, zero `MeterInfo` elements back** |
| Artifacts | [`frames.log`](frames.log) · [`flow.md`](flow.md) · [`session.trace.json`](session.trace.json) · [`session.control-no-ask.trace.json`](session.control-no-ask.trace.json) · [`their-charger.log`](their-charger.log) · [`sil-car.log`](sil-car.log) · [`config-d20-ours.yaml`](config-d20-ours.yaml) |
| Filed | [`everest-d20-meter-info.md`](../../reports/everest-d20-meter-info.md) |

## The measurement, and its control

Two complete sessions against the same station, minutes apart, differing in one bit of one field.

**Our request, with and without the ask** — the first `DC_ChargeLoopReq` of each session, session id and
timestamp aside:

```
MeterInfoRequested = false   …8d3062 81 0012006464003c02002400ca
MeterInfoRequested = true    …8d3062 a1 0012006464003c02002400ca
                                    ↑↑
```

`0x81 → 0xa1`. One bit, same 38-byte frame, everything else identical — so the ask is demonstrably on
the wire and not merely set in our object model.

**Their response, with and without the ask** — charge loops 1 and 2, session id aside:

```
loop 1, both runs   00640000020000000080810a01100fe1a01e03f8680780fe1508c0
loop 2, both runs   00640000020000000080810a01100fe1a01e03f0481580fe1508c0
```

Byte for byte the same. Loop 3 differs between the runs only in the delivered-energy counter. The
station's answer is independent of whether it was asked.

Our EVCC's own line from the run:

```
MeterInfo: asked in every charge-loop request ([V2G20-1081]); 0 response(s) carried the element ([V2G20-1082]).
```

## Where it comes from

`lib/everest/iso15118/src/iso15118/d20/state/dc_charge_loop.cpp`. The request field **is** read — it is
forwarded to the module as feedback at `:261`:

```cpp
m_ctx.feedback.dc_charge_loop_req(req->meter_info_requested);
```

and the response type has somewhere to put an answer —
`include/iso15118/message/dc_charge_loop.hpp:96`, `std::optional<datatypes::MeterInfo> meter_info`. What
is missing is the line between them. The whole of the response's metering is one comment, at `:178`:

```cpp
// TODO(sl): Setting EvseStatus, MeterInfo, Receipt, *_limit_achieved
```

`ac_charge_loop.cpp:157` carries the same TODO, so AC is in the same position — untested here because
their AC SIL walls elsewhere ([`open-work.md`](../../open-work.md)).

Nothing in `d20/` ever assigns `meter_info`: the only occurrences in that tree are the declaration, the
request field, and that comment.

## The requirements

- **`[V2G20-1081]`** — if the EVCC wants the `MeterInfo` element, it sets `MeterInfoRequested` to TRUE
  in `ChargeLoopReq`. The EV's only mechanism, and now ours.
- **`[V2G20-1082]`** — *if `[V2G20-1081]` applies*, the SECC **shall** respond with the `ChargeLoopRes`
  including the `MeterInfo` element. Conditional on the ask, unconditional once asked.
- **`[V2G20-1833]`** — and independently of any ask: an EVSE *equipped with metering technology*
  supporting the capability shall provide initial `MeterInfo` in the **very first** charge-loop response.
  Their SIL has a power meter — `EvseManager` is wired to `powersupply_dc`'s `powermeter` in the config
  used here — so the antecedent is not obviously unmet, though "supports the capability" is theirs to
  answer.
- **`[V2G20-902]`** — what the element means: energy charged during the current service session.
- **`[V2G20-1083]`** — the SECC's own use for it: to trigger a `MeteringConfirmationReq/Res` it must
  include `MeterInfo` and set `EVSENotification` to `MeteringConfirmation`. With no `MeterInfo` there is
  no confirmation exchange to have, so the `-20` metering-receipt flow is unreachable at this station.
- **`[V2G20-1919]`** — a receipt based on kWh measurements has to carry the associated `MeterInfo`.

All `-20` identifiers; no document caveat. Recorded in
[`normative-basis.md`](../../normative-basis.md).

## Our own half, which had to be built first

**Until this morning our `-20` EVCC could not ask.** `Evcc20Dc` and `Evcc20Ac` both passed the literal
`false` for `MeterInfoRequested`, so `[V2G20-1081]` — a mechanism the standard gives the *car* — was
unreachable from here, and no run of this suite had ever put any station's `[V2G20-1082]` to the test.

`Evcc20Base.RequestMeterInfo` now exists, opt-in and defaulting to `false` so every recorded session and
every vector keeps the bytes it was recorded with — the same shape as `Battery` and
`TransportSecurity.Unknown`. `Evcc20Base.MeterInfoResponses` counts what came back;
`Secc20Base.MeterInfoRequestedByEv` records what was asked, so a loopback can see a field that is
otherwise invisible from both ends. Four tests in
[`Iso20MeterInfoTests`](../../../ISO15118ConformanceTests.Simulation/E2E/Iso20MeterInfoTests.cs), and
**two of the four fail** when the plumbing is put back to the literal `false`, which is how it was
checked.

Worth stating plainly: this is the third finding this month that needed a capability of *ours* before
the counterparty's behaviour could even be looked at. A test suite cannot find a station ignoring a
question its own car never asks.

## How it was run

```bash
mosquitto -d -p 1883
~/everest/dist/bin/manager --config ~/everest/configs-ours/config-d20-ours.yaml &
CP_AT_PLUGIN=1 bash ~/everest/sil-car.sh &                       # plug in, so authorization completes
SECURITY=10 bash ~/everest/sdp-probe.sh eth0                     # → [fe80::…%eth0]:50000
socat TCP4-LISTEN:15599,reuseaddr,fork "TCP6:[fe80::…%eth0]:50000" &

V2G_INTEROP_SECC=127.0.0.1:15599 V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=dc \
V2G_INTEROP_METER=1 V2G_INTEROP_RECORD=<dir> \
  dotnet test -c Release --filter "…EverestInteropTests.OurEvcc"
```

The control run is the same line without `V2G_INTEROP_METER=1`.

One rig note, and it cost a whole session: **`--no-build` after editing the stack runs the old
binaries.** The first attempt was made straight after a counter-check that had deliberately neutered the
new code; the recorded request came back with `MeterInfoRequested` still `false` and the frame
byte-identical to the 2026-08-03 run — which read exactly like "the flag does not change the wire" and
was in fact "the flag was never compiled in". Rebuild between a counter-check and a live run.
