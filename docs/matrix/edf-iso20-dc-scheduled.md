# ISO 15118-20 DC Scheduled against eVDriveFlow

**Matrix cell:** EVCC · ISO 15118-20 · DC, Scheduled, EIM · eVDriveFlow

Back to the [interop matrix](../../README.md).

---

Their defect (optional element dereferenced; one more in the charge loop), three findings filed in the
run notes — and 12 of our -20 messages decoded clean by a second independent codec.

**The `-20` `[V2G20-460]` filing, measured — and a wall of theirs turned out to be one line of ours.**
Their SECC never reads the SessionID it was sent: with `DEADBEEFDEADBEEF` and with eight zero bytes,
**ten message types were answered `OK`** — `PowerDelivery`, the request that closes the contactor, among
them — in sessions otherwise identical to the control, message for message. Their own debug log prints
the id it received three lines above the answer carrying a different one.
[`evdriveflow-session-id`](docs/reports/evdriveflow-session-id.md) was a source finding until this;
EVerest refuses the identical bytes, which is what rules out the probe.
<br>**Ten handlers rather than three, because our car learned to send a legal filter.** Every forward
session ever driven against their SECC stopped at the fifth message on their unconditional dereference of
the optional `SupportedServiceIDs` — the 2026-08-01 run got past it only by patching *their* code in a
throwaway container. `Evcc20Base.SupportedServiceIds` sends the element instead, which Table 38 of
`[V2G20-1248]` makes the EV's option, and their station then runs the whole DC sequence unpatched:
`ServiceDetail`, `ServiceSelection`, `ChargeParameterDiscovery`, `ScheduleExchange`, `CableCheck`,
`PreCharge`, `PowerDelivery`, all `OK`. So the cell above no longer rests on a modified station.
<br>The run also found **their same defect one message later** — `display_parameters` dereferenced in the
charge loop, hit by a car that has done everything right — and cost **two fixes of ours**: two fixtures
still passing four of eleven parameters, and `RunEvccAsync`'s `-2` branch dropping `sendSessionId`
entirely. The first is the class the 08-15 guard catches, and it caught it; the second is the class it
cannot see, which its own documentation says.
[`…-edf-session-id-460`](docs/interop-runs/2026-08-15-edf-session-id-460/notes.md).
