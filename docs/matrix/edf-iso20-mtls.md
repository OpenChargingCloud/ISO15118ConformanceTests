# secp521r1 both ways, against eVDriveFlow

**Matrix cell:** SECC · ISO 15118-20 · Mutual TLS 1.3 · eVDriveFlow

Back to the [interop matrix](../../README.md).

---

The capability this counterparty was chosen for, reached once the stdin wall fell:
`TLS_AES_256_GCM_SHA384` under TLS 1.3, both peers authenticated — their EV verified our station
against its own V2G root and presented `CN=VEHICLECert`, our station required and read it back — and
**secp521r1 on both sides**. That last part is ordinary in the standard and rare in the field: -20
prescribes secp521r1 (or Ed448) for the PKI and the key exchange, but **both other -20 counterparties
here ship P-256 test material** (footnote ⁶), so this is the first peer whose -20 PKI is the one -20
describes rather than -2's. There is a platform reason for the drift worth knowing: Schannel cannot do
P-521 for TLS at all, which is why the app carries a second, managed TLS backend — and why our own
Windows mutual-TLS tests use P-256. **That managed backend then carried the same session against them**
(`V2G_TLS_BACKEND=BouncyCastle`), so the -20-faithful profile — TLS 1.3, the suite pair, P-521 both
ways — has an external witness instead of only a loopback one. 15 exchanges either way, the same route
as plain TCP. Their shipped
certificates had to be regenerated with **their own** `generateCertificates.sh` first: the SECC leaf
expired in October 2022 (60 days, as the standard requires) and `cpoSubCA1` the day before the run.
