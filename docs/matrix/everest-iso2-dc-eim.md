# ISO 15118-2 DC EIM, against EVerest

**Matrix cell:** EVCC · ISO 15118-2 · DC, EIM · EVerest

Back to the [interop matrix](../../README.md).

---

Only the **2023.10.0** demo image was an independent-codec witness (OpenV2G). Current `EvseV2G` and
`Evse15118D20` sit on **cbV2G**, our own corpus generator — so byte agreement there is agreement with
ourselves, and the value of this column is behavioural. The independent byte judgement for `-2` comes
from elsewhere: since 2026-08-07 the whole `-2` corpus round-trips through **EXIficient**, offline and
on demand — see [`tools/interop-v2gdecoder/`](tools/interop-v2gdecoder/README.md).
