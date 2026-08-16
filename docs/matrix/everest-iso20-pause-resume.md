# ISO 15118-20 pause and resume against EVerest

**Matrix cell:** EVCC · ISO 15118-20 · Pause / Resume · EVerest

Back to the [interop matrix](../../README.md).

---

**Found broken here, fixed, and re-run the same day.** Their resume is gated on mutual TLS —
`d20/state/session_setup.cpp` matches `SHA-512(session_id ‖ vehicle_cert_hash)` from the verified TLS
peer certificate, and `ConnectionPlain` returns none — so no earlier EIM run could have reached it. It
resumed on the first attempt with their minted vehicle credential; what failed was ours, replaying the
opening sequence into a session already past it
([first run](docs/interop-runs/2026-08-08-everest-pause-resume-tls/notes.md)). After the fix, **their own
log shows the difference**: `SessionSetupReq → AuthorizationSetupReq → … → ServiceSelectionReq →
DcChargeParameterDiscoveryReq` in the first half, `SessionSetupReq → DcChargeParameterDiscoveryReq` in
the resumed one — the five skipped messages, counted by the counterparty
([re-run](docs/interop-runs/2026-08-08-everest-pause-resume-tls-rerun/notes.md)). The station's binding
is still only checked by them; ours computes the same construction but the two are never compared,
because in this direction only their SECC's value is consulted.
