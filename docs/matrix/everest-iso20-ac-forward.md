# ISO 15118-20 AC against EVerest

**Matrix cell:** EVCC · ISO 15118-20 · AC · EVerest

Back to the [interop matrix](../../README.md).

---

> **On 2026.10.0 (2026-11-26/27):** ✅ again, 5 of 5 sessions: AC ×2, AC_BPT, Dynamic, and AC over mutual
> TLS 1.3. Two changes were needed first:
> - Our car's `EVPowerProfile` now has to be anchored in SECC time and lie inside the offer. The release
>   refuses a Scheduled profile outside it, as `[V2G20-1559]` allows
>   ([finding 1](../interop-runs/2026-11-26-everest-2026.10.0-forward/notes.md#finding-1--our-evpowerprofile-was-anchored-in-1970-and-2026100-is-the-first-to-check); fixed in WWCP_ISO15118#53).
> - The release now also waits 1 400 ms for the contactor to *open* at `PowerDelivery(Stop)`. A second
>   watcher therefore moves the SIL car to state B on `Waiting for contactor is opened`, which is what
>   `[V2G20-848]` asks of a car ([finding 3](../interop-runs/2026-11-26-everest-2026.10.0-forward/notes.md#finding-3---20-ac-now-waits-for-the-contactor-to-open-at-powerdeliverystop)).
>
> The close window described below is unchanged: `PowerOn` arrives 0,8 s into it.

**Green since 2026-08-13, after four months of reading the wall wrong.** Their `-20`
`PowerDelivery` waits for a `ClosedContactor` *event* inside a 3 s window — `power_delivery.cpp:118`,
gated on `is_ac_charger()`, which is why `-20` DC never meets it and `-2` does not either (`EvseV2G`
latches the value in a loop that re-tests it, so an early `true` is remembered; `libiso15118` remembers
nothing). Raising the car's CP line at plug-in put their own `PowerOn` **4,948 s before** the window,
where it was produced and discarded. Firing it *into* the window instead gives `PowerOn` at +783…1005 ms
against 3 000 ms and a complete session — five of them, with a control between that still fails at
3,047 s. Nothing injected, nothing patched: their IEC layer, their `EvseManager`, their conclusion.
[`…-d20-ac-contactor-window`](docs/interop-runs/2026-08-13-everest-d20-ac-contactor-window/notes.md).
<br>**And then over mutual TLS 1.3 the same day**, which is what makes the cell conformant rather than
merely complete: every session up to that point was plain TCP, and `[V2G20-1237]` and `[V2G20-2356]`
both forbid that. Two `AC` and two `AC_BPT` sessions with their `Handshake complete!` and
`Verify certificate result is okay`, the window unchanged at +832…1048 ms because the handshake is
spent before `PowerDelivery`
([`…-d20-ac-tls13`](docs/interop-runs/2026-08-13-everest-d20-ac-tls13/notes.md)).
Reading their source to explain the wall had already turned up something that *is* theirs, on the same
code path and not the cause of it:
[`everest-iso20-ac-contactor-latch.md`](docs/reports/everest-iso20-ac-contactor-latch.md).
