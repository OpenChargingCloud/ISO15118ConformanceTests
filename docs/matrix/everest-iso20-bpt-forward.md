# ISO 15118-20 BPT against EVerest

**Matrix cell:** EVCC · ISO 15118-20 · BPT · EVerest

Back to the [interop matrix](../../README.md).

---

> **On 2026.10.0 (2026-11-26/27):** ✅ DC_BPT, Scheduled (once our car's profile was fixed) and Dynamic.
> ✅ AC_BPT, Scheduled, with the state-B trigger at the stop. BPT over TLS was not re-run
> ([forward run](../interop-runs/2026-11-26-everest-2026.10.0-forward/notes.md)).

**Neither of their configs was changed for this**, which is the finding: their SIL had been
advertising service 6 at every -20 DC run this project ever made, and our EV could not ask for it.
The **AC_BPT** half followed on 2026-08-13 once the contactor window was understood (footnote ⁵) — two
sessions, their log reading `EV selected service: AC_BPT`, through the charge loop to `SessionStop`.
