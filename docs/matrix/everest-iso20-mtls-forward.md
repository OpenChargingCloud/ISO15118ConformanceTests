# Mutual TLS 1.3 against EVerest, from Windows

**Matrix cell:** EVCC · ISO 15118-20 · Mutual TLS 1.3 · EVerest

Back to the [interop matrix](../../README.md).

---

59 and 68 exchanges to `SessionStop` from Windows, once the app let a session name its TLS backend.
The session is real; **the curve is not the one -20 asks for, and that is theirs**:
`create_certs.sh -v iso-20` emits P-256 — with their own `TODO` beside it — where ISO 15118-20
prescribes secp521r1 or Ed448 for the PKI *and* the key exchange. Josev's -20 PKI is P-256 too. So for a
long time this project's -20 TLS met only -2-grade material from counterparties; eVDriveFlow is the
first that ships what the standard says (footnote ²¹).
