# EVerest cross-validation, in detail

The long form of the EVerest column in the [interop matrix](../README.md#the-interop-matrix--who-we-test-against-and-what-happened):
every scenario that has run against **everest-core**, what each one caught, and what stays out of reach.
It has a page of its own for the opposite reason to [Josev's](josev-cross-validation.md). Josev is the
independent *codec*; EVerest is the independent *charger* — the thing a car in the field actually meets —
and it has found more defects in this project than any other counterparty, all of one shape.

Tooling and the per-session ritual: [`tools/interop-everest/`](../tools/interop-everest/README.md).
Per-run write-ups and frame logs: [`docs/interop-runs/`](interop-runs/) (nineteen directories, all
prefixed `*-everest-*`).

---

## What EVerest is, and why it counts differently

**everest-core** is the Linux Foundation Energy charging stack. Four modules matter here:

| Module | Role | Codec underneath |
|---|---|---|
| `EvseV2G` | station, DIN 70121 + ISO 15118-2 (C) | cbV2G at HEAD — **OpenV2G** in the 2023 demo image |
| `Evse15118D20` | station, ISO 15118-20 | libiso15118 + libcbV2G |
| `IsoMux` | one endpoint in front of both, routing on the SAP offer | its own -2-era TLS termination |
| `PyEvJosev` | **car** — Josev in a wrapper | EXIficient |

**"Works against EVerest" is closer to a market claim than to a test result**, and that is the reason to
do it. It is also why the evidence here is a different kind from Josev's: at HEAD both station modules sit
on cbV2G, the encoder our own vector corpus is generated from, so a byte agreement would be agreement with
ourselves. What EVerest gives instead is *behaviour* — sequencing, timing, state, and a station that
enforces rules nothing here had ever had to satisfy.

**The codec flips with the direction, and that decides how much reverse runs are worth.** `PyEvJosev`
wraps `EVerest/ext-switchev-iso15118` — EVerest's fork of the same SwitchEV codebase the
[Josev column](josev-cross-validation.md) tests, vendored at `26f7988` in 2026.02.1. So driving *their*
station we meet cbV2G, and being driven by *their* car we meet EXIficient. A `←SECC` run here is
therefore an independent-codec result — and also, mostly, a re-run of the Josev reverse column. That is
why the reverse direction was spent only where it buys something neither a forward run nor a Josev run
can, and why -2 reverse against EVerest has deliberately never been run.

With one accidental exception, and it is worth the paragraph:

> **An image tag is not a version.** The first three runs used
> `ghcr.io/everest/everest-demo/manager:main`, which every plan in this repository described as current.
> It is **everest-core 2023.10.0, built 2023-12-05**: `ldd` shows `libopenv2g.so.1`, and there is no
> libcbv2g anywhere in the image. So those sessions *were* independent-codec results after all — a
> hand-written C codec from the original reference work, by different authors from chargebyte's
> generator, decoding all 36 of our messages and encoding all 36 of its own for ours. The lineage note
> was wrong in the direction that makes the runs worth more. Runs are pinned by **digest** since.

**Versions met:** 2023.10.0 (demo image) · **2025.10.0** (demo image) · **2026.02.1** (built from source
in WSL2, and the whole matrix re-validated on it).

### The idea that made every wall fall

Two early runs stopped dead — one at `Authorization`, one at `CableCheck` — and both times the missing
thing was *hardware*, not protocol. `EvseManager::cable_check()` closes the contactor and waits for the
board-support module to report it closed; in the SIL that report comes because a simulated car walks the
CP line A→B→C, and a V2G peer arriving over TCP has no CP line.

The answer, which every subsequent run rests on: **their module graph is addressable over MQTT**, so the
simulated car can be plugged in and the session authorized from outside — without patching a line of
EVerest. That is [`sil-car.sh`](../tools/interop-everest/sil-car.sh), two publishes and a subscription.
Everything below was unblocked by it.

---

## What has run

- **ISO 15118-2 DC, a complete charge (2026-08-02).** The first complete -2 session this project ever ran
  against a foreign station: 36 exchanges from `SupportedAppProtocolReq` to `SessionStopRes`, every
  response `OK`, and the flow report's verdict on the route — *"The order matches the declared flow
  exactly."* Not a walkthrough but a charge: their log shows the power supply set to 400 V/120 A, the
  charger state going `PrepareCharging→Charging`, isolation measured, and the next transaction starting
  at the energy the previous one had metered. On the OpenV2G image, so an independent decoder in **both
  directions** ([`…-iso2-dc-full-charge`](interop-runs/2026-08-02-everest-iso2-dc-full-charge/notes.md)).

- **ISO 15118-2 AC, complete (2026-08-03)** — after it first died seven messages in at
  `FAILED_WrongEnergyTransferMode`, which was our defect and theirs to catch (below).

- **ISO 15118-20 DC, a complete charge (2026-08-03).** 113 exchanges, route confirmed message for message
  against our own recorded session. The `AuthorizationSetup → ServiceDiscovery → ServiceDetail →
  ServiceSelection → ScheduleExchange` sequence in particular had never before been answered by anything
  but our own SECC ([`…-iso20-dc-full-charge`](interop-runs/2026-08-03-everest-iso20-dc-full-charge/notes.md)).

- **ISO 15118-20 DC in Dynamic control mode (2026-08-03).** Twice, 102 and 119 exchanges. Their own log
  is what makes it a Dynamic result rather than a hopeful one — `control mode: Dynamic` against the
  Scheduled run's `control mode: Scheduled` — and the substance shows up in the power supply: Scheduled
  ran at the setpoint *our* car named (400 V/120 A), Dynamic at an operating point *their station chose*
  from the envelope we declared (500 V/125 A). The mode, in two log lines
  ([`…-iso20-dc-dynamic`](interop-runs/2026-08-03-everest-iso20-dc-dynamic/notes.md)).
  <br>**This is the half Josev cannot give.** Every recorded Dynamic session against Josev had *their*
  EVCC on the other side: our station could answer a Dynamic car long before our car could *be* one, so
  "Scheduled and Dynamic" quietly meant "Scheduled both ways, Dynamic inbound". The Dynamic EVCC was
  built on 2026-08-03 and its first outing was here — which is why the matrix reads `EV→` for EVerest
  and `←SECC` for Josev on that row, and why neither column covers the mode alone.

- **ISO 15118-20 DC over mutual TLS 1.3 (2026-08-03, re-run 2026-08-06).** 116 exchanges. The first time
  anything outside this project exercised the -20 TLS profile our own `docs/pki-model.md` pins — TLS 1.3,
  mutual authentication, the profile's two suites — and confirmation that our EVCC *builds and validates*
  a foreign SECC chain against a supplied anchor rather than accepting what it is handed. First proven
  from macOS; the Windows half needed `V2G_TLS_BACKEND=BouncyCastle` in the app and then ran twice
  ([`…-iso20-dc-tls13`](interop-runs/2026-08-03-everest-iso20-dc-tls13/notes.md),
  [`…-tls13-windows`](interop-runs/2026-08-06-everest-iso20-tls13-windows/notes.md)).

- **ISO 15118-2 Plug & Charge over TLS 1.2 (2026-08-03).** Their station accepted our contract chain
  (`PaymentDetailsRes = OK`) and **verified our signature** — established positively rather than by
  absence of error: their `publish_require_auth_pnc` sits *downstream* of `check_iso2_signature`, so the
  message existing at all is the statement, and the response codes corroborate by elimination
  ([`…-pnc`](interop-runs/2026-08-03-everest-pnc/notes.md)). Their station-side rule *"no `Contract`
  without TLS"* was also the first external check of that spec requirement against us, and it still holds
  on 2026.02.1.
  <br>**The two halves of this cell exclude each other**, which is worth naming: their `EvseManager`
  drops `Contract` from the offer for an already-authorized session, and plugging the simulated car in —
  the thing that makes a *complete charge* possible at all — is what authorizes it. So a complete charge
  and a PnC offer cannot be had in the same session against this SIL.

- **`IsoMux`, all four offer shapes (2026-08-03), and over TLS (2026-08-06).** One endpoint answering
  both protocols; then both protocols in *one* offer, which is the case a multiplexer exists for; then
  the same over TLS. Our EVCC follows the station's answered SchemaID into the matching state machine
  rather than its own ranking — the whole content of the feature
  ([`…-isomux-dc`](interop-runs/2026-08-03-everest-isomux-dc/notes.md),
  [`…-isomux-both`](interop-runs/2026-08-03-everest-isomux-both/notes.md),
  [`…-isomux-tls`](interop-runs/2026-08-06-everest-isomux-tls/notes.md)).

- **MCS — the first live counterpart our megawatt support ever had (2026-08-05/06).** everest-core
  2026.02.1 is the first release shipping `config-sil-mcs.yaml`. Three forward sessions with service id
  **8** read back by their stack as MCS; then **MCS_BPT (9)** twice, with their `EvseManager` decoding
  `dc_ev_maximum_power_limit: 3750000.0` at 3000 A and 1250 V — our megawatt envelope, through a foreign
  decoder. Then the reverse: their `PyEvJosev`, configured `supported_d20_energy_services: MCS`, picking
  service 8 **out of our catalogue** — the only direction that tests our numbers rather than theirs
  ([`…-mcs`](interop-runs/2026-08-05-everest-mcs/notes.md),
  [`…-mcs-bpt-complete`](interop-runs/2026-08-06-everest-mcs-bpt-complete/notes.md),
  [`…-mcs-reverse`](interop-runs/2026-08-06-everest-mcs-reverse/notes.md)).

- **BPT without MCS (2026-08-06).** Two complete **DC_BPT** sessions, Scheduled and Dynamic, with their
  station reading `Max discharge current 200.000000A` out of our `BPT_DC_*` request. **AC_BPT** negotiates
  — their log says `EV selected service: AC_BPT` — and then meets their contactor wall
  ([`…-bpt`](interop-runs/2026-08-06-everest-bpt/notes.md)).

- **Reverse, `PyEvJosev` → our SECC (2026-08-06).** Lower information in general — their car *is* Josev —
  with one exception that made it worth doing: it is the only run that puts **our** service catalogue in
  front of a foreign chooser. It also produced, unasked, the first **-20 Plug & Charge** result against
  this counterparty in either direction: their EV signed, our SECC verified challenge, digest and
  signature. Later re-run through the recording fixture, so the direction now leaves frames and a corpus
  trace rather than a console log
  ([`…-mcs-reverse-recorded`](interop-runs/2026-08-06-everest-mcs-reverse-recorded/notes.md)).
  <br>**That PnC capture is deliberately not a corpus entry.** The signature is made with their EV's
  private key, so a trace built from it could only re-check the recorded bytes against themselves —
  `SessionTrace.Build` refuses it, correctly, and an EIM re-run supplies the trace instead. Both halves
  are kept: one is the evidence, the other is the corpus entry.

---

## What it found in **us** — and the shape they share

This is the part that justifies the setup cost. EVerest has found more defects in this project than
every other counterparty combined, and almost all of them are one of two shapes.

**Shape one — a value taken from our own assumption where the protocol supplies one.** The AC run notes
name it as such, on the third instance — and it is the shape that stopped being paid for one finding at a
time: two of the three that triggered [the sweep](assumed-values-sweep.md) came from here.

| Defect | The station that showed it |
|---|---|
| **No bound on an "Ongoing" poll.** Our EVCC polled `AuthorizationRes` until the fixture's 3-minute budget ran out. `OngoingGuard` — a 60 s per-phase deadline, ISO 15118's own EVCC ongoing timeout — went into both protocols and all three languages. | a station with nothing to authorize, which answers `Ongoing` correctly and forever |
| **The energy transfer mode was never read.** `Evcc2` hard-coded `AC_three_phase_core`; `ServiceDiscoveryRes` had carried the station's list five messages earlier. Their AC SIL is single-phase and refused it. | a station that offers one thing and we assume another |
| **`PreferredEnergyServiceIds` was a filter, not a ranking.** `SelectEnergyTransferService` walked the *station's* catalogue and took the first entry we accepted, so our order was never read. | a catalogue of `[8, 9]` handed an EVCC listing `{9, 8}` the service **8** |
| **`Evcc20Mcs` declared a DC envelope under an MCS service.** The EV-side limits were literals where the station-side ones were already virtual. | their `EvseManager` logging `dc_ev_maximum_power_limit: 50000.0` under service 9 |
| **No BPT request path on the EV side.** Our bidirectional work was station-side, where the EV's message decides the direction. | `FAILED_WrongChargeParameter` — their station enforces the service/parameter coupling |
| **Our SECC answered SchemaID 1 as a literal** rather than echoing the id of the entry it accepted — indistinguishable from correct for as long as every EVCC it met assigned SchemaID 1, which ours did. | building the both-protocol offer to meet their mux |

**Shape two — a knob written for the case in front of us, narrower than the thing it models.** These are
the more insidious ones: the narrowness *hid a gap* rather than causing a failure, so no test went red.

- **Our EVCC offered exactly one protocol per session**, because the state machine was chosen before the
  handshake. A capability that reads as present because both halves exist separately.
- **`Secc20Base.SelectedEnergyServiceId` was `protected`** while its EVCC counterpart was public — and in
  a reverse run the station is the *only* side that can report the choice.
- **`InteropSession.RunSeccAsync` returned a bare `Boolean`**, so a reverse run could report whether it
  finished and never what finished. `Secc20Mcs` offers `{8, 9}` on a machine that runs a plain DC session
  just as happily, so an MCS run that negotiated something else would have passed.
- **The reverse fixture could not advertise over SDP**, so a peer that discovers rather than connects had
  no way to arrive — and every reverse run went through the CLI, which can advertise but writes no
  artifacts.
- **BPT-first was reachable only for MCS.** The AC and DC rankings live on `Evcc20Base` and `Evcc20Ac` is
  sealed, so services **5 and 6 could not be asked for at all** — while their SIL had been advertising
  service 6 at every -20 DC run ever made against it.
- **`DevTlsOrNull` pinned the TLS profile from the protocol**, including for a both-protocol offer, where
  the protocol is by construction not settled when the ClientHello goes out.

And one that is neither shape but belongs on the list, because a loopback peer could never have shown it:
**the harness's own MCS_BPT probe copied the megawatt envelope** by hand — because `Evcc20Mcs` was sealed
— and the copy drifted. It took a station that logs what it received to see it.

> The recurring line across these run notes: **a loopback peer clamps nothing and reports nothing.** Our
> own SECC answers in kind, advertises exactly what our EVCC asks for, and never enforces a coupling. It
> cannot be the counterparty that finds any of the above.

---

## What it found in **them**

Written up per run; two are drafted for filing under [`docs/reports/`](reports/) and neither has been
sent — they are the operator's to post, under their own name.

- **An error anywhere on the accept path ends `Evse15118D20`'s whole event loop, sockets still bound.**
  One defect, three triggers found: a unicast SDP request, TLS key logging, and a refused TLS handshake.
  The station then keeps accepting connections and answers nothing, which from outside is
  indistinguishable from a hung peer. On 2026.02.1 the unicast trigger is **fixed** and the refused-
  handshake one **persists** — reachable from their *stock* SIL config with one `openssl s_client` line.
  `IsoMux` does **not** share it (it survived two refused handshakes and kept accepting), which narrows
  the report to the one module ([`everest-loop-shutdown.md`](reports/everest-loop-shutdown.md)).
- **`IsoMux` never reads SAP `Priority`.** It walks the offer in array order and returns on the first
  namespace starting with `urn:iso:std:iso:15118:-20`, so an EV ranking -2 first still lands on -20.
  Confirmed on the wire against 2025.10.0 **and** 2026.02.1, and a third time over TLS. Whether it is a
  defect rests on requirement text this project does not hold, so the notes say what their code does and
  stop there.
- **`IsoMux` serves TLS 1.2 only**, and routes on the SAP offer regardless — so a dual-stack EV gets a
  complete **ISO 15118-20 session over TLS 1.2**, while a -20 EV that pins its own profile gets alert 70
  and never reaches the backend at all. TLS is settled before `SupportedAppProtocol` runs, so nothing on
  that path is in a position to object.
- **ISO 15118-20 Plug & Charge is not implemented in `Evse15118D20`** — `auth_services.push_back(…PnC)`
  is commented out with *"Currently Plug&Charge is not supported and ignored"*. It moved off this
  project's list and onto theirs.
- **`PyEvJosev`'s manifest documents 4 of the 12 energy-service values it accepts**, omitting the `MCS`
  its own shipped config uses — and an unrecognised entry is silently dropped rather than reported
  ([`pyevjosev-manifest-services.md`](reports/pyevjosev-manifest-services.md)).
- **`EvseV2G` in the 2023 demo image segfaults on the second V2G session in one process**, taking every
  module down with it. Not present in 2025.10; a property of that image.
- **Corrected, and worth keeping corrected:** "their SECC sends only its leaf" turned out to be a
  property of what `everest-aux`'s `CPO_CERT_CHAIN.pem` contains, not of their code withholding
  intermediates.

---

## What stays out of reach, and why

Each of these is structural rather than a missing run:

- **A complete Plug & Charge *charge*, in either protocol.** The signature is the part that is ours and it
  verifies; the authorization backend is the part that is theirs and their SIL does not have one
  (`NO_CONNECTOR_AVAILABLE`). `config-sil-ocpp201-pnc.yaml` is the configuration that would, and it needs
  an OCPP 2.0.1 CSMS on the other end — a different counterparty and a bigger piece of work than this
  harness has set up.
- **ISO 15118-20 AC past `PowerDelivery(Start)`.** Their SIL expects *their own* EV module to close the
  contactor, so driving the CP line is not enough. Two different car-simulator sequences give the
  identical `FAILED_ContactorError`.
- **Megawatt *power*.** Their MCS SIL is electrically an ordinary charger and clamps to 22 kW whatever is
  declared. The catalogue and the envelope are validated; the current is not.
- **secp521r1 material from a counterparty.** Their `create_certs.sh -v iso-20` emits **P-256** — with
  their own `TODO` beside it — so the secp521r1 half of our -20 TLS profile still has no external
  generator.
- **CertificateInstallation.** Not implemented here at all; that result belongs to Josev.
- **A byte-level codec diff.** At HEAD both station modules are cbV2G, which is our own oracle. The
  independent-codec evidence from this counterparty is the 2023 image's OpenV2G, and it is a working
  decoder in both directions rather than an octet comparison.

---

## Current state

The full forward matrix — -2 DC/AC, -20 DC Scheduled **and** Dynamic, `IsoMux` in all four offer shapes
and over TLS, -20 DC over mutual TLS 1.3, MCS, MCS_BPT, DC_BPT — is green against **2025.10.0** and
re-validated against **2026.02.1 built from source**. The reverse direction runs and is recorded. There
is no unattempted cell left in EVerest's column of the matrix; what remains is the list of walls above,
and two reports waiting to be sent.
