# EVerest sends no price schedule, deliberately

**Matrix cell:** EVCC · ISO 15118-20 · Signed tariffs · EVerest

Back to the [interop matrix](../../README.md).

---

**Established from their source and the frames already here, without a session — and no session would
have moved it.** `create_default_scheduled_control_mode` builds the Scheduled response under the comment
*"Providing no price schedule!"*, citing an agreement on `iso15118.elaad.io` that `[V2G20-2176]` — a
*shall*: the SECC provides either an `AbsolutePriceSchedule` or a `PriceLevelSchedule` — *"is not
required and should be ignored"*; the call site adds `// TODO(sl): Adding price schedule`. The Dynamic
branch sets only departure time and SOC. Their **codec** converts `AbsolutePriceSchedule` in both
directions, so grepping the type name is not an answer; their **station** never produces one. The bytes
agree and predate the question: every `ScheduleExchangeRes` recorded from them, across six runs, is
**28–44 bytes**. Deliberately **not filed** — a documented decision attributed to an industry agreement
is not a defect to report back. [`…-d20-price-schedule-audit`](docs/interop-runs/2026-08-11-everest-d20-price-schedule-audit/notes.md).
