# ISO 15118-2 DC EIM, against EVerest

**Matrix cell:** EVCC · ISO 15118-2 · DC, EIM · EVerest

Back to the [interop matrix](../../README.md).

---

> **On 2026.10.0 (2026-11-26/27):** ✅ two sessions in one `EvseV2G` process (45 and 43 pairs, every
> response `OK`) and ✅ once through `IsoMux`. The station is still on cbV2G, so this remains a
> behavioural result. `EvseV2G` ships deprecated in the release, earliest removal 2027.04.0
> ([forward run](../interop-runs/2026-11-26-everest-2026.10.0-forward/notes.md)).

Only the **2023.10.0** demo image was an independent-codec witness (OpenV2G). Current `EvseV2G` and
`Evse15118D20` sit on **cbV2G**, our own corpus generator — so byte agreement there is agreement with
ourselves, and the value of this column is behavioural. The independent byte judgement for `-2` comes
from elsewhere: since 2026-08-07 the whole `-2` corpus round-trips through **EXIficient**, offline and
on demand — see [`tools/interop-v2gdecoder/`](tools/interop-v2gdecoder/README.md).
