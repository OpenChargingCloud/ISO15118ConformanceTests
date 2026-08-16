# ISO 15118-20 BPT against EVerest

**Matrix cell:** EVCC · ISO 15118-20 · BPT · EVerest

Back to the [interop matrix](../../README.md).

---

**Neither of their configs was changed for this**, which is the finding: their SIL had been
advertising service 6 at every -20 DC run this project ever made, and our EV could not ask for it.
The **AC_BPT** half followed on 2026-08-13 once the contactor window was understood (footnote ⁵) — two
sessions, their log reading `EV selected service: AC_BPT`, through the charge loop to `SessionStop`.
