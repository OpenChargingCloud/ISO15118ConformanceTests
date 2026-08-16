# tux-evse's TLS configs offer neither prescribed suite

**Matrix cell:** SECC · ISO 15118-2 · TLS 1.2 (unilateral) · tux-evse

Back to the [interop matrix](../../README.md).

---

Both their shipped configs pin one GnuTLS priority string, and its ECDSA half holds AES-GCM, AES-CCM,
ChaCha20 and two SHA-1 CBC suites — **not** `TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA256` or its ECDH twin,
which is what ISO 15118-2 requires and what our station pins. Handshake: `no shared cipher`. Unpinned
(`V2G_INTEROP_TLS_SUITES=platform`, a deviation the run states rather than hides) the session runs to
`PaymentServiceSelection` and stops **on their side**: their EVCC signs the `AuthorizationReq` whenever
a `pki` block is configured rather than when Contract was selected, so an EIM scenario dies at
`no_challenge` — reproduced against **their own responder**, which means no scenario they ship runs over
TLS today. Their car does present a client certificate when asked.


## Run

```
git submodule update --init --recursive
bash libs/EVSimulatorApp/libs/WWCP_ISO15118/tools/download-schemas.sh
dotnet test -c Release
```

The middle step is not optional. The source generators run at build time from the ISO schemas in the
app's WWCP submodule (`libs/EVSimulatorApp/libs/WWCP_ISO15118/**/Schemas/`), and those schemas are ISO's —
not redistributed here, so a fresh clone carries only a placeholder `README.md` in each `Schemas/` and
the build stops at `EXIGEN001`. Running the script is you accepting the ISO Customer Licence
Agreement, which nobody can accept on your behalf; if you already have the files,
`SCHEMA_CACHE=<dir> bash …/download-schemas.sh` lays that copy out instead of fetching — `<dir>`
holding the `iso-2/`, `iso-20/` and `amd1/` directories the script would otherwise have created.

The offline run (`dotnet test`) needs no C toolchain, no Java and no network: the record-mode
cross-checks re-encode Josev's captured EXIficient frames through our codec
(`WWCP_ISO15118_EXI_Tests`), the session corpus under `Vectors/` guards our own wire output against
regression, the transport's own decisions are unit-tested in `WWCP_ISO15118_Session_Tests`, and the
loopback E2Es run both peers in-process. 1 451 tests, all four assemblies green. The **live** cross-checks against a
running Josev or EVerest are `[Explicit]` and stay out of the offline run — they need the other stack
on the wire. What each of them has proven is the matrix above.



## Deeper reading

| | |
|---|---|
| [`docs/josev-cross-validation.md`](docs/josev-cross-validation.md) | the independent **codec** (EXIficient), the counterparty with the most history here, and the only one that serves both roles well. Every -20 energy mode any independent stack implements, over TCP and TLS, plain and Plug & Charge, in both control modes. |
| [`docs/everest-cross-validation.md`](docs/everest-cross-validation.md) | the independent **charger**, the thing a car in the field actually meets, and the counterparty that has found the most defects in *this* project; almost all of them share one of two shapes, which that page names. [No unattempted cell left](docs/everest-cross-validation.md#current-state), the walls that remain named one at a time, and the drafts it produced indexed in [`docs/reports/`](docs/reports/README.md) — which is the only place their number is kept, because carrying it here is how it went four out of date. |
| [`docs/evdriveflow-cross-validation.md`](docs/evdriveflow-cross-validation.md) | the **second** independent codec (OpenEXI), and the highest yield per exchange here: one defect of ours that every other oracle was structurally blind to, and four of theirs. The wall that held all four of its capabilities [turned out to be a closed file descriptor](docs/interop-runs/2026-08-06-edf-stdin-wall/notes.md), not a state machine. |
| [`docs/tux-evse-cross-validation.md`](docs/tux-evse-cross-validation.md) | a **replayer**, not a codec: their scenarios come from packet captures, so what it offers is a real car's route and the only DIN 70121 material this project has seen. As a responder it answers the car in its recording and no other; as an **injector at their HEAD** it drove our SECC through the full captured-Audi DC session and a VW AC route — and reached the one arm of our state machine no self-consistent test had ever executed. Over TLS it produced the first external check of our TLS profile, and [two findings drafted for them](docs/reports/tux-evse-tls.md). Their Tesla DIN capture is unreadable to us past the handshake — and the handshake alone [carried a vendor-proprietary protocol at priority 1](docs/interop-runs/2026-08-07-tesla-din-handshake/notes.md), an offer shape nothing here could have written for itself. |
| [`docs/open-work.md`](docs/open-work.md) | the inverse of the matrix above: every cell that is not `✅`, why, and who it waits on. **The to-do list.** |
| [`docs/interop-runs/`](docs/interop-runs/) | one write-up per live run: configuration, frame logs, divergences. **History, not a to-do list** — each note's `Next` section is a snapshot from that day, and later runs close items without editing it |
| [`docs/reports/`](docs/reports/README.md) | findings written up for the counterparty they belong to — **forty-seven filings across six projects**, each a draft for a person to send, with the reproduction that makes it confirmable |
| [`tools/interop-*/`](tools/) | how to bring each counterparty up and drive it — [Josev](tools/interop-josev/README.md) · [EVerest](tools/interop-everest/README.md) · [eVDriveFlow](tools/interop-evdriveflow/README.md) · [tux-evse](tools/interop-tux-evse/README.md) |
| [`docs/assumed-values-sweep.md`](docs/assumed-values-sweep.md) | where our own assumptions replaced values the protocol supplies |


---

The stack all of this tests — the EXI codec, the state machines, the TLS/PKI, the CLI — is documented in
**[WWCP_ISO15118](libs/EVSimulatorApp/libs/WWCP_ISO15118)**, and the apps built on it in
**[EVSimulatorApp](libs/EVSimulatorApp)** one level above it.

This repository is only the judge.
