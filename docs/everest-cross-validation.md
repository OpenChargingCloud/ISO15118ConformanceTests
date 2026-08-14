# EVerest cross-validation, in detail

The long form of the EVerest column in the [interop matrix](../README.md#the-interop-matrix--who-we-test-against-and-what-happened):
every scenario that has run against **everest-core**, what each one caught, and what stays out of reach.
It has a page of its own for the opposite reason to [Josev's](josev-cross-validation.md). Josev is the
independent *codec*; EVerest is the independent *charger* — the thing a car in the field actually meets —
and it has found more defects in this project than any other counterparty, all of one shape.

Tooling and the per-session ritual: [`tools/interop-everest/`](../tools/interop-everest/README.md).
Per-run write-ups and frame logs: [`docs/interop-runs/`](interop-runs/) (thirty-seven directories, all
prefixed `*-everest-*`; counted 2026-08-11).

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
  anything outside this project exercised the -20 TLS profile the app's
  [`docs/pki-model.md`](../libs/EVSimulatorApp/docs/pki-model.md) pins — TLS 1.3,
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
  <br>**A complete charge and a PnC offer never came in the same session — which is their design, not a
  wall.** Their `EvseManager` narrows `PaymentOptionList` to `ExternalPayment` the moment an EIM
  authorization arrives, switching certificate installation off with it, and restores `Contract` at
  `SessionFinished`; the comment on that branch says the stack *"should not offer the contract option"*.
  It is the ad-hoc / RFID path a public charger has to have, expressed per session — and the same switch
  exists per station, fed from OCPP through `set_plug_and_charge_configuration`.
  <br>Only the *ordering* belongs to the SIL: `DummyTokenProvider` publishes an ISO14443 token on
  `SessionStarted`, so the simulated swipe lands at plug-in and the window in which a plugged-in but
  still unauthorized car is offered `Contract` never opens. On a real charger that window is the ordinary
  case. Our own workaround says it from the other side — `token_provider.main.connector_id: 2`, a
  connector that does not exist, is just *do not swipe*.
  <br>**And the session stopped at `Authorization` for a reason that took until 2026-08-13 to find, and
  it was ours.** `EvseManager` republishes the contract token through its own `token_provider`
  implementation, and only `config-sil-ocpp-pnc.yaml` and `config-sil-ocpp201-pnc.yaml` connect that to
  `auth` — everywhere else the token is published to a variable nobody subscribed to, and the session
  polls until `auth_timeout_pnc` and answers `FAILED` with no token in any log. One connection added,
  and the same session runs **past `Authorization`** to `ChargeParameterDiscovery` and `CableCheck`.
  <br>With [the contract-validator arm](../tools/interop-everest/contract-validator-arm.sh) supplying
  the verdict their SIL has no backend for, `Invalid` + `certificate_status: CertificateRevoked` also
  produced **`AuthorizationRes = FAILED_CertificateRevoked`** — unreachable by any configuration of
  their SIL, since `DummyTokenValidator` cannot set `certificate_status` and
  `evse_managerImpl.cpp:386` then fills in `value_or(Accepted)`
  ([`…-contract-validator`](interop-runs/2026-08-13-everest-contract-validator/notes.md)).

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
  station reading `Max discharge current 200.000000A` out of our `BPT_DC_*` request. **AC_BPT** negotiated
  — their log says `EV selected service: AC_BPT` — and then met their contactor wall
  ([`…-bpt`](interop-runs/2026-08-06-everest-bpt/notes.md)); **it runs complete since 2026-08-13**, twice,
  once the wall turned out to be a 3 s window we were arriving five seconds early for
  ([`…-d20-ac-contactor-window`](interop-runs/2026-08-13-everest-d20-ac-contactor-window/notes.md)).

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

- **Chain validation, both levels, both directions (2026-08-08).** The X.509 chain check added that day
  had only ever seen certificates this project minted; these two runs are the first foreign hierarchy.
  Forward, our EVCC validated their station's TLS chain against their V2G root. **The first reading of
  that run was wrong and is corrected below** — root-alone was refused, we wrote it up as their station
  sending a bare leaf, and it was our own code dropping the intermediates instead. Reverse, their EV's
  signed `-20 AuthorizationReq` carried its real
  contract chain, anchored at a **`MORootCA` of its own** — and there the root alone *is* enough,
  because their car ships its Sub-CAs. Same vendor, opposite shapes. The negative control is the one
  worth keeping: pointed at their **V2G** root, our station printed `signature OK … chain REJECTED`,
  which is exactly the distinction that did not exist here the day before
  ([`…-chain-validation`](interop-runs/2026-08-08-everest-chain-validation/notes.md)).

- **The OEM provisioning chain (2026-08-08).** `PyEvJosev` with `is_cert_install_needed: true` — a key
  in its own manifest — sends a signed `CertificateInstallationReq` carrying
  `OEMRootCA → OEMSubCA1 → OEMSubCA2 → OEMProvCert`, their **third** self-signed root. Root alone
  suffices (their car ships the intermediates), Sub-CAs without the root do not, and their V2G root is
  refused — although their own request *names* that root in `RootCertificateIDList`, which is the car
  declaring what it can verify, not what vouches for it. That closed the last validator path judged only
  by our own material ([`…-oem-provisioning-chain`](interop-runs/2026-08-08-everest-oem-provisioning-chain/notes.md)).

- **The correction, one day later (2026-08-09).** Their station **does** send its full chain —
  `openssl s_client -showcerts` reads back `SECCCert`, `CPOSubCA2`, `CPOSubCA1`, and with our TLS call
  sites fixed to pass the peer's intermediates to the validator, their **V2G root alone** anchors at
  `CN=V2GRootCA`. What was written up as a property of `EvseV2G` was a defect of ours that produced the
  same symptom for every peer. It went unquestioned because it agreed with a note this repository
  already carried about `Evse15118D20`; what broke it was eVDriveFlow, whose car has a differently
  shaped chain ([`…-edf-chain-validation`](interop-runs/2026-08-09-edf-chain-validation/notes.md)).
  The other two rows of that run survive.

- **`session_logging` measured on 2026.02.1 (2026-08-10).** A second complete -2 DC charge, run for one
  purpose: to put their published station-side record beside our own recording of the same session, on
  the current release rather than the 2023 image. 43 of 43 requests byte-exact, 43 of 43 responses
  carrying the *preceding request's* length, and 42 of them the V2GTP version byte `0x00`
  ([`…-session-log-lengths`](interop-runs/2026-08-10-everest-session-log-lengths/notes.md)).

- **The MQTT authorization path, repaired and controlled (2026-08-10).** Driving a session with no
  hardware had stopped working when the variable name moved into the topic — quietly, as such things
  do. Both versions of `mqtt-authorize.sh` against the same station, ten minutes apart: the old one
  authorizes nothing and the EV polls `AuthorizationReq` 401 times, the new one is authorized on the
  fourth ([`…-mqtt-authorize-2026021`](interop-runs/2026-08-10-everest-mqtt-authorize-2026021/notes.md)).
  Their `Auth` cannot tell our token from their own `DummyTokenProvider`'s, and nothing of theirs is
  patched to make that true.

- **Two probes and a control against `IsoMux` (2026-08-10).** Six bytes of a V2GTP header against
  eight, six seconds apart, no EV involved. It turned a line that had been sitting unexplained in a
  2026-08-03 station log into a reproduction: the multiplexer announces the read failure and then makes
  its backend decision anyway
  ([`…-isomux-shortread`](interop-runs/2026-08-10-everest-isomux-shortread/notes.md)). The same lesson
  as the OCSP run, one line further down the same log file.

- **A warning in their boot log, chased to the end (2026-08-10).** Not a session at all — the shortest
  thing in this list and one of the larger findings. `<n> certificates != <n> OCSP responses` at
  startup, with the two numbers left literal, turned out to mean that no EVerest station staples an
  OCSP response at all. The measurement is one MQTT command and its reply
  ([`…-ocsp-stapling`](interop-runs/2026-08-10-everest-ocsp-stapling/notes.md)); the cause is one
  member missing from one conversion function; the requirement is in both protocols. Worth recording
  as a method as much as a result: **every unexplained line in a counterparty's log is a candidate,**
  and this one had been in our recorded logs since 2026-08-03 without anyone reading it.

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

Written up per run; the ones that became filings are drafted under [`docs/reports/`](reports/) and none
has been sent — they are the operator's to post, under their own name. **The count lives in
[`reports/README.md`](reports/README.md) and nowhere else**: it was carried here as a literal for weeks
and was wrong by four before anyone noticed, which is the same failure this page's *Current state*
section warns about in a different costume.

> **Everything below was read against `2026.02.1`, the release. On 2026-08-11 every finding was
> re-tested against everest-core `main`** ([audit](interop-runs/2026-08-11-reports-upstream-audit/notes.md))
> and **three of them are fixed there**: the `-20` AC contactor latch, the accept-loop shutdown's TLS
> trigger, and the `-20` AC namespace filter. Two more had their *argument* overtaken without changing
> the observable behaviour — OCSP and client-auth, both because the `-20` TLS layer was rebased onto
> `lib/everest/tls`. Each entry below says where it stands. Twelve are unchanged on `main`, verbatim.
>
> Two related traps, both paid for once: **the standalone `EVerest/libiso15118` is not maintained** —
> it still shows every one of these and decides nothing — and a marker broad enough to survive a
> refactor reports *unchanged* for code that was rewritten around it, which nearly hid the namespace
> fix.

- **A malformed contract certificate in `PaymentDetailsReq` crashes the module.**
  `handle_iso_payment_details` parses the EV's contract certificate at `iso_server.cpp:982`, **uses**
  it at `:990` (`getEmaidFromContractCert(contract_crt)`), and only checks the parse result `err` at
  `:1006`. On unparseable DER `der_to_certificate` returns a null `certificate_ptr`
  (`openssl_util.cpp:653`), so line 990 runs `certificate_subject(nullptr)` — which opens
  `assert(cert != nullptr)` and then `X509_get_subject_name(cert)` (`openssl_util.cpp:774`): **SIGABRT**
  in a debug build (their CMake default), **SIGSEGV** in a release build.
  <br>**Reachable pre-authentication.** `-2` TLS is unilateral, and the crash is *during parsing*,
  before any signature or chain check — so the trigger is one `PaymentDetailsReq` with a non-empty,
  non-certificate `ContractSignatureCertChain.Certificate` after an ordinary handshake and a
  `Contract` selection. A single crafted message, repeatable. **Filed 2026-08-11:**
  [`everest-evsev2g-paymentdetails-crash.md`](reports/everest-evsev2g-paymentdetails-crash.md),
  [run](interop-runs/2026-08-11-everest-evsev2g-paymentdetails-crash/notes.md).
  <br>**Demonstrated in isolation, not against a running station**: a C reproduction of
  `certificate_subject`'s first two lines on a null `X509*` gives SIGSEGV under `-DNDEBUG` and SIGABRT
  with the assert live. **Explicitly a null dereference — availability, not code execution** — and
  **no ISO clause**; it stands on the crash and its reachability, the same footing as any robustness
  bug. The code's own intent for this input is `FAILED_CertChainError`, set at every neighbouring exit;
  one misplaced check is the whole distance to a crash.
  <br>**Josev and ours both survive it, differently** — Josev's parse-level `ValueError` falls through
  the state's cert-*verification* `except` to the framework rcv-loop catch-all
  (`shared/comm_session.py:510-516`), which stops the session with no `PaymentDetailsRes`; ours catches
  in `Secc2.PaymentDetails` and returns `OK`, failing a message later at the signed `AuthorizationReq`.
  Neither crashes, and **neither answers `FAILED_CertChainError` for the unparseable case** — a smaller
  shared imperfection, one of whose three misses is us. The corrected contrast is *survival*, not the
  answer; only `EvseV2G` reaches OpenSSL with a null. (The report's first draft said "both answer
  FAILED"; reading the exception paths corrected it.)
  <br>**Same thread as the two above**: reading their `-2` Plug & Charge handlers, which our own stack
  could only start exercising this morning (`WWCP_ISO15118` `c1a7989`). Three `EvseV2G` PnC findings in
  one afternoon, one of them a crash.
- **`CertificateUpdateRes` is sent from the union slot the previous response left behind.**
  `handle_iso_certificate_update` (`iso_server.cpp:1817-1820`) is `// TODO: implement CertificateUpdate
  handling` and returns `V2G_EVENT_NO_EVENT` — `0`, the ordinary carry-on value, where the only value
  that suppresses an answer is `V2G_EVENT_IGNORE_MSG`. So the dispatch, which has already set
  `CertificateUpdateRes_isUsed = 1u` and cites `[V2G2-556]` on the calling line, sends it.
  <br>**And `iso2_BodyType`'s message bodies are a `union`** (`iso2_msgDefDatatypes.h:2141-2178`).
  None of the three inits touches its members — `init_iso2_exiDocument` is `(void) exiDoc;`,
  `init_iso2_BodyType` clears only the `_isUsed` bitfields outside the union, and
  `init_iso2_CertificateUpdateResType` clears only `RetryCounter_isUsed`. Every `*ResType` begins with
  `ResponseCode`, so a contract-renewal request is answered with the **previous message's** code —
  `OK` in any session that reached it — and five mandatory elements read as bytes of another type.
  <br>`[V2G2-556]` makes acting on the request a *shall*; `[V2G2-557]`/`[V2G2-558]` leave exactly two lawful
  answers and this is the wrong one; `[V2G2-736]` wants the mandatory fields schema-conformant
  whatever happens. **Filed 2026-08-11:**
  [`everest-evsev2g-certificate-update.md`](reports/everest-evsev2g-certificate-update.md),
  [audit](interop-runs/2026-08-11-everest-iso2-cert-update-audit/notes.md).
  <br>**Two things it deliberately does not claim.** Not a memory disclosure — the generated encoder
  bounds-checks lengths, so a wild stale length fails the encode instead of copying past the field.
  And not measured: no session was run, and the filing's first checklist item is that run.
  <br>**Bounded by a complete sweep**, which is what makes it one function rather than a class: all
  seventeen `handle_iso_*` handlers were checked, sixteen assign `ResponseCode` between 2 and 13
  times, and this one never does. **Josev does not implement the feature either and answers `FAILED`
  correctly in nine lines** — which is why the report asks them to answer, not to implement renewal.
  <br>**Askable only since this morning**: our own `-2` stack learned contract provisioning in
  `WWCP_ISO15118` `c1a7989`, so until then no counterparty's `-2` provisioning path had been looked
  at. Fifth time this month a capability of ours opened a question about somebody else — and the first
  of the five not answered on the wire the same day.
- **The ISO 15118-2 signed-metering path is open at both ends.** Going out,
  `ISO15118_chargerImpl::handle_update_meter_info` reads `powermeter.energy_Wh_import.total` and never
  the `energy_Wh_import_signed` sibling **on the same argument** — `v2g_ctx->meter_info` has three
  members and no room for it, and `SigMeterReading` occurs nowhere in `EvseV2G`. Coming back, the EV's
  signed `MeteringReceiptReq` reaches `publish_iso_metering_receipt_req`, whose entire body is
  `// TODO: publish PnC only`, and gets `ResponseCode = OK` unconditionally;
  `check_iso2_signature` has **one** call site in the module and it is the `AuthorizationReq`.
  <br>**Measured off bytes already in this directory**, decoded by neither codec involved: entry `[30]`
  of the [2026-08-02 DC charge](interop-runs/2026-08-02-everest-iso2-dc-full-charge/frames.log) put
  through V2Gdecoder (RISE-V2G + EXIficient) shows `MeterInfo` with `MeterID` and `MeterReading` and
  nothing else — **two of the five elements** `MeterInfoType` defines. Entry `[31]`, 17 bytes shorter,
  has none at all: their `meter_info_is_used` is one-shot.
  <br>**`[V2G2-902]` is the requirement** — the `MeterInfo` the SECC sends shall be exactly what the
  meter itself produced. **`[V2G2-904]` is expressly a `may`**, so not verifying the receipt is permitted, and
  the filing says so before it says anything else; what closes that door is that NOTE 1's secondary
  actor cannot verify it either, because nothing forwards it. **Filed 2026-08-11:**
  [`everest-evsev2g-metering-chain.md`](reports/everest-evsev2g-metering-chain.md),
  [run](interop-runs/2026-08-11-everest-iso2-metering-receipt/notes.md).
  <br>**The `-20` sibling is already filed** and is the same capability one step further gone:
  `meter_info` is never set on a `ChargeLoopRes` at all
  ([`everest-d20-meter-info.md`](reports/everest-d20-meter-info.md)). Two modules, one capability,
  neither fix reaching the other.
- **The `-20` SessionID and the PnC challenge carry 32 bits, whatever their width.** Four sites in
  `libiso15118` fill a security-relevant array from `std::mt19937` seeded with **one** 32-bit
  `random_device` draw — three `d20::Session` constructors and `AuthorizationSetup::handle_request` —
  so the 64-bit SessionID and the 128-bit `GenChallenge` each take at most 2³² values.
  `[V2G20-2621]` requires 58 bits in the first and `[V2G20-698]` 120 in the second, with
  `[V2G20-835]` requiring a cryptographically secure generator at all; `[V2G2-835]`, `[V2G2-697]` and
  `[V2G2-698]` say the same under the same numbers, so it is not a `-20` novelty.
  <br>**Measured against the binary, from data this repository already held.** Their own station log
  prints `New session created with session_id: 0x…`, and twenty `-20` runs between 2026-08-03 and
  2026-08-11 had recorded **49 distinct** of them for other reasons.
  [`seedsearch`](../tools/everest-rng-probe/README.md) reproduces their five lines verbatim and walks
  all 2³² seeds: **49 of 49 recovered, 639 s on sixteen threads.** The search exits non-zero if any
  target fails, so a toolchain mismatch could not have passed as a strong RNG. A second arm counts
  `GenChallenge` collisions: **8 repeats in 262 144 draws** against **0** for a `/dev/urandom` control,
  where the birthday bound over 2³² predicts 8,0. **Filed 2026-08-11:**
  [`everest-d20-rng-entropy.md`](reports/everest-d20-rng-entropy.md),
  [run](interop-runs/2026-08-11-everest-d20-rng-entropy/notes.md).
  <br>**Their own `EvseV2G` gets both values right** — `/dev/urandom` through `tools.cpp:38`, for the
  `-2` SessionID, the `-2` challenge and the DIN SessionID alike. Second time in two days that a rule
  EVerest satisfies in one module it misses in the other, the `[V2G2-460]` zero exemption below being
  the same shape reversed.
  <br>**No message-level oracle could ever have found this**, ours included: the bytes are the right
  width, in the right field, schema-valid and different every session. An entropy requirement has no
  observable in a single message — it is reachable only by reading the generator or by counting
  collisions across many values, and this run did both.
- **`V2G_SECC_Sequence_Timeout` is one flat 60 s, in a charge loop that allows 0,5 s.** Read out of
  their `-20` timeout header — `TIMEOUT_SEQUENCE = 1000 * 60`, armed from the single call site in
  `Session::send_response()` — and then measured. Two arms: a normal charge completes in 24 s; a car
  that stops sending after one `DC_ChargeLoopReq` **and holds the connection open** is left standing for
  **60,0025 s**, the interval taken from *their own* log between `DcChargeLoopRes` and their
  `Sequence Timeout … Stopping the session` verdict. Tables 216 and 217 (`[V2G20-1500]`, `[V2G20-1502]`)
  give the SECC **0,5 s** after a charge-loop response — the phase in which the contactor is closed.
  120×. Everything around the value is right: the arming, the disarming, the `SupportedAppProtocolReq`
  exemption, the session stop. **Filed 2026-08-11**:
  [`everest-d20-sequence-timeout.md`](reports/everest-d20-sequence-timeout.md),
  [run](interop-runs/2026-08-11-everest-d20-sequence-timeout/notes.md).
  <br>**Measuring it needed a capability of ours, for the third time this month.** A car that hangs up
  is an EOF and says nothing about a timer; `Evcc20Base.GoSilentInChargeLoop` makes ours go quiet with
  the socket open. MeterInfo was a field we never set, the SessionID a header we could not forge, this a
  message we could not withhold — three different shapes of the same lesson.
  <br>Their log line names a third number, `40secs`, which is `V2G_EVCC_Sequence_Performance_Time` —
  the EV's row of Table 215. Cosmetic, in the filing as cosmetic, and evidence the table was read.
  <br>**And ours is the same shape:** `Secc20Base` takes one `sequenceTimeout` for every phase. In
  [`open-work.md`](open-work.md), and in the report, because leaving it out is what makes a report easy
  to dismiss.
- **A SessionID of zero walks past `EvseV2G`'s `[V2G2-460]` check.** The next arm of the same probe,
  and the one that found something. Their check is present and cited in their own code — and carries a
  `received_session_id != 0` conjunct, so the one value ISO reserves for *"I have no session"* is
  exempted. Three arms against a fresh station, differing only in the SessionID of an in-sequence
  `ServiceDiscoveryReq`: the correct id is served, **one flipped bit is refused** with their log naming
  *"error: Unknown Session"*, and **eight zero bytes are served** — the response differing from the
  correct arm in no byte but the echoed id. Their DIN twin has no such guard, their `-20`
  implementation has none, and their own test for the rule is DIN-only with a non-zero id, which is why
  it survived. **Filed 2026-08-11**:
  [`everest-evsev2g-session-id-zero.md`](reports/everest-evsev2g-session-id-zero.md),
  [run](interop-runs/2026-08-11-everest-iso2-session-id-zero/notes.md).
  <br>**The first attempt measured the wrong thing and said so.** The probe assumed the SessionID was
  byte-aligned; their log printed `0x8c04a714dff52c76` where the probe read the same value shifted two
  bits, so the "zero" arm had actually sent `…0001` and was refused entirely correctly. The run now
  cross-checks the id it reads against the id their log says it created, in every arm. A negative from
  a probe pointing two bits to the left is indistinguishable from a conformant peer, and that is the
  transferable part.
  <br>**And ours is worse:** `FAILED_UnknownSession` appears nowhere in our live code, in either
  protocol version — see *Ours to fix* in [`open-work.md`](open-work.md).
- **Checked and found correct — their `-20` station refuses a foreign SessionID, where their `-2` one
  serves it.** The `-20` twin of the probe above, and it came back clean: `validate_and_setup_header`
  is called in **15 of the 17** `d20/state/*.cpp` files, and the two that skip it — `session_setup` and
  `supported_app_protocol` — are exactly the two `[V2G20-460]` excludes. Measured with two arms against
  `Evse15118D20`: the control charges end to end, and eight zero bytes from `AuthorizationSetupReq`
  onward get **`FAILED_UnknownSession`**. So within one project the `-20` implementation does what the
  `-2` one exempts, which is the sharpest available argument that the `!= 0` conjunct is an accident
  rather than a policy ([`…-iso20-session-id-probe`](interop-runs/2026-08-11-iso20-session-id-probe/notes.md)).
  <br>**A correction to how it was first read**: an earlier grep reported ten checking states and left
  the charge loop looking unguarded. It had been truncated by a `head` — a truncated grep and a short
  list are indistinguishable. The full sweep over all 17 files is what the table above rests on.
- **Checked and found correct — `EvseV2G` answers an out-of-order request instead of hanging up.** Recorded here because a ruled-out class saves the next sweep the hour: `[V2G2-538]` wants *the corresponding response message* carrying `FAILED_SequenceError` before the session ends (`[V2G2-459]`, then `[V2G2-539]`), and closing the socket without answering is the failure mode — one **we had ourselves** until 2026-08-06. Two arms, `AuthorizationReq` and `ChargeParameterDiscoveryReq` sent where a `ServiceDiscoveryReq` was due: both answered with the right message type, their own log naming *"error: Sequence Error"* each time, connection closed after. The probe is 40 lines and reusable against any `-2` station ([`…-iso2-sequence-error`](interop-runs/2026-08-11-everest-iso2-sequence-error/notes.md)).
- **Their `-20` charge loop never returns `MeterInfo`, even when the EV asks.** `[V2G20-1081]` gives the
  EV one way to be told the meter reading; `[V2G20-1082]` makes answering a *shall* once asked. Over a
  complete 70-exchange DC session all three charge-loop responses came back without the element — and the
  control is what makes it sharp: **our request changed by one bit** (`0x81`→`0xa1` in the same 38-byte
  frame) and **their responses were byte-identical between the two runs**, so the answer does not depend
  on the question. `dc_charge_loop.cpp:261` reads the field and forwards it as feedback; `:178` is the
  one comment where the response's metering should be — *"TODO(sl): Setting EvseStatus, MeterInfo,
  Receipt, *_limit_achieved"* — and nothing in `d20/` ever assigns `meter_info`. What it costs is more
  than a reading: `[V2G20-1083]`'s `MeteringConfirmation` and `[V2G20-1919]`'s kWh receipt both need the
  element, so the `-20` signed-metering path is unreachable rather than partial.
  **The first finding here that needed a capability of ours before it could be looked at**: our own EVCC
  hardcoded `MeterInfoRequested` to `false` until the same morning, so no run of this suite had ever
  asked anybody ([`…-d20-meter-info`](interop-runs/2026-08-10-everest-d20-meter-info/notes.md)). Filed:
  [`everest-d20-meter-info.md`](reports/everest-d20-meter-info.md).
- **Their `-20` station refuses the vehicle certificate and accepts a contract certificate.** It loads
  two anchors for the EV's TLS client certificate, the **V2G** root and the **MO** root — and MO
  certifies *contract* certificates, which ISO 15118-20 places at the application layer. The anchor that
  certifies the **vehicle** certificate is the **OEM** root, and it is never loaded because
  `CaCertificateType` has no `OEM` value to ask with. Measured with **their own unmodified PKI**, two
  arms three seconds apart: `OEM_LEAF` → `certificate verify failed`; `MO_LEAF` →
  `Verify certificate result is okay` and then their own **`Vehicle Cert is available`**, whose SHA-512
  becomes the `[V2G20-2677]` resume binding. `[V2G20-2331]` anchors a vehicle certificate at an OEM or
  V2G root; `[V2G20-2401]` names exactly those two in `certificate_authorities`; their own error string
  at `connection_ssl.cpp:275` already says *"Verify OEM root not found!"* over a field called
  `mo_root`. **Why none of our runs caught it**: `install-pki.sh` mints a vehicle credential under the
  **V2G** root, the other anchor `[V2G20-2331]` allows and the one they do load, so every mutual-TLS
  session we have run took the branch that works
  ([`…-d20-trust-anchor`](interop-runs/2026-08-10-everest-d20-trust-anchor/notes.md)). Filed:
  [`everest-d20-trust-anchor.md`](reports/everest-d20-trust-anchor.md).
- **`Evse15118D20` never staples an OCSP response, and has nowhere to put one.** Asked with
  `openssl s_client -status` — the extension `[V2G20-2372]` obliges every `-20` EV to send — their
  station answers **`OCSP response: no response sent`** on TLS 1.2 and on TLS 1.3, and logs nothing,
  because at `2026.02.1` `libiso15118` had no OCSP code at all: three independent gaps, each sufficient —
  `include_ocsp = false`, no member on `SSLConfig` to carry the data, no `SSL_CTX_set_tlsext_status_cb`.
  <br>**On `main` two of those three are gone** and the finding is better for it. The `-20` TLS layer
  was rebased onto `lib/everest/tls`, which has the `OcspCache` and the `status_request` callback, and
  `SSLConfig` grew a `chains` vector whose `ChainConfig` carries `ocsp_response_files`. What is left is
  a module that asks with `include_ocsp = false` and passes `{}` — so the measurement is unchanged and
  the ask went from *"implement OCSP"* to **three one-line changes in three files that only work in a
  particular order**. Their own `<n> certificates != <n> OCSP responses` warning fires once per TLS
  **session setup** — not at process start, `ConnectionSSL` being constructed per SDP request — and on
  2026-08-12 that was **measured on a `main` build**, 2 of 2, on their stock `-20` SIL config
  ([run](interop-runs/2026-08-12-everest-main-ocsp-warning/notes.md)). The same session start also
  emits `trusted_ca_keys support disabled`, which is the `IsoMux` §4 finding appearing on the `-20`
  station. **Checked against the `-20` text on 2026-08-12: not applicable** — `trusted_ca_keys` is
  absent from ISO 15118-20, which carries the same duty on RFC 8446's `certificate_authorities`.
  Chasing that turned up the finding the run was not looking for: `lib/everest/tls` reads the EV's
  `certificate_authorities` **nowhere** and fixes the server chain at `cfg.chains[0]`, so
  `[V2G20-1007]`/`[V2G20-2379]` cannot be met by a station holding two roots — now
  [`everest-d20-client-auth.md`](reports/everest-d20-client-auth.md) §3, and **measured the same day**:
  with two roots installed and a valid chain under each, the EV asks for root A and the station serves
  root B, identically to when it asks for B and when it asks for nothing
  ([run](interop-runs/2026-08-12-everest-main-chain-selection/notes.md)). Their own log names the single
  leaf it picked before any `ClientHello` existed. **Still not the same issue as the
  dropped `ocsp` member** — that one is the conversion boundary, still present on `main`, and *neither
  fix alone produces a staple*. **Controlled**, because "your client never
  asked" is the first objection: the same client and the same flag against `IsoMux` made their own
  `OcspCache::lookup` run on the digest of their own leaf. `[V2G20-2388]` obliges a public SECC to
  answer — with `[V2G20-2398]` the exemption to ask about, since this module has PnC commented out —
  and reading one clause further cost part of an earlier claim: `[V2G20-2411]` lets a `-20` EV fetch the
  response itself, so the `-2` *"close the connection"* consequence has no `-20` twin
  ([`…-d20-ocsp-absent`](interop-runs/2026-08-10-everest-d20-ocsp-absent/notes.md)). Filed:
  [`everest-d20-ocsp-absent.md`](reports/everest-d20-ocsp-absent.md).
- **`Evse15118D20` lets the EV decide whether the EV is authenticated.** Their `-20` TLS server sets
  `SSL_VERIFY_NONE` on the context and raises it to `SSL_VERIFY_PEER | SSL_VERIFY_FAIL_IF_NO_PEER_CERT`
  inside the `ClientHello` callback — but only when the offered `supported_versions` list contains TLS
  1.3. Offer TLS 1.2 alone and no `CertificateRequest` is ever sent; `[V2G20-2400]` puts it on the SECC
  unconditionally. Two arms, one variable, no client PKI and no EV needed: `-tls1_3` is refused with
  *"peer did not return a certificate"*, `-tls1_2` reaches **`Handshake complete!`**. Then our own
  recorded `supportedAppProtocolReq(-20:DC)` gets `OK_SuccessfulNegotiation` and `SessionSetupReq` gets a
  session id, so an anonymous peer sits at `AuthorizationSetup` on a `-20` station — which is
  `[V2G20-2356]` a second time, in a module with no multiplexer in front of it. Downstream,
  `vehicle_cert_hash` is never computed, so `session_setup.cpp:99` can never take the resume branch:
  pause/resume on such a connection silently cannot work. **Second finding in the same function**: the
  handshake carries no `certificate_authorities` (`[V2G20-2401]`), OpenSSL's default signature-algorithm
  list rather than Table 8 (`[V2G20-1667]`) and a named group outside Table 7 (`[V2G20-2460]`) — while
  `:224` sets exactly the Table 6 cipher suites in Table 6's order, so the profile was consulted once and
  not carried across
  ([`…-d20-client-auth`](interop-runs/2026-08-10-everest-d20-client-auth/notes.md)). Filed:
  [`everest-d20-client-auth.md`](reports/everest-d20-client-auth.md).
  <br>**Re-argued 2026-08-11, and §1 got smaller.** On `main` the mechanism moved into
  `lib/everest/tls` behind a flag, `verify_client_on_tls13`, with the reasoning written out: *-2 means
  TLS 1.2 and no client certificate, -20 means TLS 1.3 and a client certificate.* That is **correct for
  the library**, which serves both protocols from one code path — and wrong for `Evse15118D20`, which
  serves only `-20` and opts into the leniency anyway (`out.verify_client = false`). So the defect is a
  call site, the fix is one line, and it costs no conformant interop because `[V2G20-1237]` puts a
  conformant `-20` EV on TLS 1.3 regardless. The assumption is also never checked downstream: the same
  station that declined to authenticate the peer *because it looked like `-2`* then answers
  `supportedAppProtocolReq(-20:DC)` with `OK`. §2 is unchanged at the new location — and two things
  there now *look* like `certificate_authorities` and are not, the peer-side chain selection and
  RFC 6066 `trusted_ca_keys`.
- **A DC-only `Evse15118D20` accepts the `-20` **AC** message set.** Offered `-20:AC` and nothing else,
  a station with no AC hardware anywhere in its module graph answers `OK_SuccessfulNegotiation` and
  commits the session to the AC schema; `handle_request` puts both namespaces in one priority map and
  is not given the station's configuration to filter with. `[V2G20-169]` makes the station's own
  capability a filter **before** the EV's ranking — and the control arm shows the ranking itself is
  honoured, so this is the other half of the same requirement. Carried past the handshake with the car
  plugged in, the session spends session setup, an authorization and a token and then dies at
  `ServiceDiscovery` on services 2 and 6
  ([`…-d20-ac-namespace`](interop-runs/2026-08-10-everest-d20-ac-namespace/notes.md)). Drafted:
  [`everest-d20-ac-namespace.md`](reports/everest-d20-ac-namespace.md) — **withdrawn 2026-08-11,
  fixed on `main` before it was ever sent.** `handle_request` now gates both namespaces on the
  station's own energy services (`is_dc = … and modes.dc`), which is `[V2G20-169]`'s
  filter-before-ranking, implemented, with a log line that reads like somebody working from the same
  clause. **The sibling of the `IsoMux` routing finding, from the other side** — there the router reads
  the namespace and not the ranking, here the backend read the ranking and not its own capability — and
  **that sibling is not covered by this fix**: different file, different module, still live.
- **`IsoMux`'s TLS server boots with `trusted_ca_keys support disabled`.** It asks libevse-security for
  its certificate through `get_leaf_certificate_info`, which carries `include_root = false`, so it has
  no root to put in `trust_anchor_pem`; its chain is then neither verified nor registered, and the
  extension handler is left with an empty list. `EvseV2G`, in the same process on the same PKI 4 ms
  later, logs neither warning — it asks the other way. `[V2G2-651]` obliges **every** EVCC to send that
  extension and `[V2G2-871]` obliges the station to present a chain rooted where the EV said it trusts;
  with one V2G root nothing shows, with two the mux serves the first and an EV holding the other
  abandons the handshake per `[V2G2-924]`. Two log lines from 2026-08-06, unread until 2026-08-10
  ([`…-isomux-trusted-ca-keys`](interop-runs/2026-08-10-everest-isomux-trusted-ca-keys/notes.md)).
  Filed: [`everest-isomux.md`](reports/everest-isomux.md). **The
  failing case has not been run** — it needs two roots and an EV that sends the extension; ours does
  not.
- **`IsoMux` reports that it could not read the message, and then handles it.** A failed
  `v2g_incoming_v2gtp()` is logged and not acted on, so a short or malformed V2GTP header still reaches
  `v2g_sniff_apphandshake`, still yields an `iso20` verdict, and the connection is still proxied — to
  the `-2` backend, which meets the same bytes and closes. In the same seven lines the retry loop turns
  on `rv == 1`, the value that means *the peer closed*. `EvseV2G`, from which the function was forked,
  has the `goto error_out` it lost. Sat unread in a station log from 2026-08-03 for a week;
  reproduced deliberately on 2026-08-10 with a control connection differing by two bytes
  ([`…-isomux-shortread`](interop-runs/2026-08-10-everest-isomux-shortread/notes.md)). Filed:
  [`everest-isomux.md`](reports/everest-isomux.md).
  **Third finding in this module**, alongside the SAP-priority and TLS-1.2 ones.
- **No EVerest station staples an OCSP response, and one missing line is why.** `EvseV2G` asks
  libevse-security for the OCSP data belonging to its certificate chain (`include_ocsp = true`);
  libevse-security assembles one entry per certificate; and `to_everest(CertificateInfo)` copies six of
  the seven members, forgetting `ocsp`. The TLS server then sees `3 certificates != 0 OCSP responses`,
  caches **nothing** — all-or-nothing, not even the certificates that do have a response — and the
  handshake extension is omitted for want of anything to put in it. `[V2G2-871]` and `[V2G20-2388]`
  both require the stapling; `[V2G20-2372]` makes a `-20` EV always ask for it; and `[V2G2-873]` makes a
  conformant `-2` EV **close the connection** when it asked and got nothing — so TLS, and therefore
  Plug & Charge, is unreachable for an EV that enforces it. Measured off their own MQTT reply with no
  EV and no session, **with a control**: two OCSP responses installed through their own
  `update_ocsp_cache`, written to their own cache by their own handler, station restarted — same
  warning, same reply, nothing reaches the TLS server
  ([`…-ocsp-stapling`](interop-runs/2026-08-10-everest-ocsp-stapling/notes.md)). Filed 2026-08-10:
  [`everest-evse-security-ocsp-dropped.md`](reports/everest-evse-security-ocsp-dropped.md).
  `IsoMux` reaches the same place by a different route — it asks with `include_ocsp = false` — and
  `Evse15118D20` has no OCSP code at all, so a fix to the conversion alone does not finish the job.
- **`session_logging` publishes every response with the preceding request's length.** Their MQTT
  message stream is an attractive station-side record of a session, and we used it as one on
  2026-08-02; requests are byte-exact, responses are truncated or padded with stale buffer, under the
  correct message name. `publish_var_V2G_Message()` sizes from `conn->payload_len`, which only
  `v2g_incoming_v2gtp()` ever writes, and both response publish sites run before
  `v2g_outgoing_v2gtp()` computes the response's own length. Filed 2026-08-10:
  [`everest-evsev2g-session-log-responses.md`](reports/everest-evsev2g-session-log-responses.md).
  **Re-measured the same day on 2026.02.1**, over a complete -2 DC charge
  ([`2026-08-10-everest-session-log-lengths`](interop-runs/2026-08-10-everest-session-log-lengths/notes.md)):
  43 of 43 requests byte-exact, 43 of 43 responses carrying the *request's* length — no exceptions —
  and 42 of them published with the V2GTP version byte `0x00`, from the buffer reset that precedes
  each encode, so the record is not a malformed frame but not a frame at all. `Evse15118D20` is
  unaffected because it publishes the message id and no bytes: the byte-level record exists only for
  -2/DIN, and it is the one that is wrong.
- **An error anywhere on the accept path ends `Evse15118D20`'s whole event loop, sockets still bound.**
  One defect, three triggers found at the time: a unicast SDP request, TLS key logging, and a refused
  TLS handshake. The station then keeps the SDP socket bound and answers nothing, while the module and
  the manager both stay alive — so no supervisor built on process liveness can see it. On 2026.02.1 the
  unicast trigger was **fixed** and the refused-handshake one **persisted**, reachable from their stock
  SIL config with one `openssl s_client` line. `IsoMux` does **not** share it (it survived two refused
  handshakes and kept accepting), which narrows the report to the one module.
  <br>**On `main` the handshake trigger is fixed too — and the defect is not.** `TbdController::loop()`
  still ends on any throw out of `poll()`; the fix was to stop the handshake throwing, not to stop the
  loop dying. Rewriting the report on 2026-08-11 against `main` found **eight throw sites that still
  reach it**, including the TLS-key-logging constructor, and a **second** fix of theirs carrying the
  comment `// FIXME (aw): we should not die here immediately` — seventeen lines above a call in the same
  function that still does exactly that. The report now leads with the loop and cites both of their
  fixes as agreement ([`everest-loop-shutdown.md`](reports/everest-loop-shutdown.md)).
- **`IsoMux` decides on *"does this EV do -20 at all"*, not on SAP `Priority`.** It walks the offer in
  array order and returns on the first namespace starting with `urn:iso:std:iso:15118:-20`, so an EV
  ranking -2 first still lands on -20. `Priority` is printed to their log two lines above the decision
  and not used in it, and the branch carries their own comment — `// Check if it supports ISO-20` — so
  this is a routing policy, not an unread field. Confirmed on the wire against 2025.10.0 **and**
  2026.02.1, and a third time over TLS — the same 79-byte request and the same 12-byte answer all three
  times. **Decided and filed 2026-08-09**:
  [`everest-isomux.md`](reports/everest-isomux.md). `[V2G2-169]` and
  `[V2G20-169]` both make selecting the EV's highest-ranked protocol a *shall*, so the policy conflicts
  with a requirement after all — and the `-2` caveat is answered rather than declared here, the `-20`
  clause and the 2019 manual (written to the 2014 edition) saying the same thing
  ([`normative-basis.md`](normative-basis.md)). What decides the report is neither: **both modules
  behind the mux read `Priority` correctly**, each citing the requirement in a comment, so the router in
  front of them is the whole defect.
- **`IsoMux` terminates TLS at the -2 profile, then routes -20 traffic through it.** Not an oversight:
  `connection/tls_connection.cpp` pins `cipher_list` to the suite ISO 15118-2 prescribes and sets
  `ciphersuites = ""` under the comment *"disable TLS 1.3"* — two lines carried verbatim from `EvseV2G`,
  and their TLS library caps the version on exactly that condition. The **consequence** is what this
  column found, and it is structural: TLS is settled before `SupportedAppProtocol` runs, so the profile
  is fixed before the protocol is known. A dual-stack EV gets a complete **ISO 15118-20 session over
  TLS 1.2**; a -20 EV that pins its own profile gets alert 70 and never reaches the backend at all.
  Nothing on that path is in a position to object.
  **Filed 2026-08-09:** [`everest-isomux.md`](reports/everest-isomux.md).
  `[V2G20-2356]` forbids an SECC to select -20 on a connection at TLS 1.2 or below, and the two halves
  above together mean the -20 backend is reachable *only* by an EV that breaks the mirror requirement
  `[V2G20-1237]` — which ours did, and which is now an item of our own in
  [`open-work.md`](open-work.md).
- **ISO 15118-20 Plug & Charge is not implemented in `Evse15118D20`** — `auth_services.push_back(…PnC)`
  is commented out with *"Currently Plug&Charge is not supported and ignored"*. It moved off this
  project's list and onto theirs.
- **Their `-20` station cannot report a failed EIM authorization, and the branch that would is dead
  code in their own tree.** `EvseManager` forwards authorization verdicts to the HLC **for Plug & Charge
  only** (`evse_managerImpl.cpp:381-387`, under its own comment), so `Evse15118D20` never learns that a
  token was rejected — and since it offers no PnC, *no* verdict ever reaches it. Measured 2026-08-13 with
  [the contract-validator arm](../tools/interop-everest/contract-validator-arm.sh) answering `Invalid`:
  their `Auth` logged `REJECTED` and their station answered `AuthorizationRes = OK, EVSEProcessing =
  Ongoing` **602 times**, where `[V2G20-2230]` gives it **1,5 s** to answer `Finished` with
  `WARNING_EIMAuthorizationFailure`. Their `-20` library has exactly that branch, citing exactly that
  requirement in the comment above it (`d20/state/authorization.cpp:55-57`) — the same shape as the
  `IsoMux` finding two entries up, where the layer that is right is underneath the layer that decides.
  <br>**Both halves measured out the same day.** With our car's own patience raised out of the way
  (`V2G_INTEROP_ONGOING`), the tail arrives at **1 800 `Ongoing` then `FAILED`** — the wrong code, since
  their timeout branch is tested before the EIM switch, and 120× the 1,5 s allowed. And the **`-2` twin
  is correct**: `[V2G2-854]` requires `Ongoing_WaitingForCustomerInteraction` when no *positive* EIM
  information is available, `[V2G2-845]` has the EV keep asking, and `EvseV2G` does exactly that for
  299,8 s — its `auth_timeout_eim` — citing the clause at `iso_server.cpp:947-948`. **The rule changed
  between the protocols and the shared module kept `-2`'s**, which is what any report has to say, or the
  fix lands on both and breaks the one that is right.
- **The `-20` AC contactor wait is edge-triggered against a level `EvseManager` already holds**, so a
  contactor that closed a moment early can never be learned about and the station answers
  `FAILED_ContactorError` on a **closed** contactor. `ac_connector_closed` is a per-entry `bool` written
  only by a `ClosedContactor` arriving while `PowerDelivery` is active; `EvseManager` sends that event on
  a CP **edge** only, keeps the truth in `contactor_open`, and never re-reports it — while the request
  the state machine does send merely sets `hlc_allow_close_contactor`, which changes nothing once the
  contactor is shut. **Measured with a positive control on 2026-08-13**: `PowerOn` at −4,948 s gives the
  3,047 s timeout, `PowerOn` inside the window gives `OK` and a complete session, five times. `EvseV2G`
  is immune twice over — it reads the current state *before* waiting and re-tests a latched value inside
  the wait. **The second instance of the same shape as the EIM entry above**: the layer that knows does
  not tell the layer that decides, and the fix is on `EvseManager`'s side both times
  ([`everest-d20-ac-contactor-edge.md`](reports/everest-d20-ac-contactor-edge.md)). It is **not** the
  withdrawn pointer-latch report; it survives that fix, and on `main` the gate has widened to
  `is_ac_der_iec_charger()`.
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

- **ISO 15118-20 pause/resume without mutual TLS.** Not a gap in their implementation — a precondition
  of it. `d20/state/session_setup.cpp` rejoins a paused session only when
  `SHA-512(session_id ‖ vehicle_cert_hash)` matches what it stored, and that hash comes from the TLS
  peer certificate: `ConnectionSSL` fills it after a handshake that verified one
  (`connection_ssl.cpp:484-499`), while `ConnectionPlain::get_vehicle_cert_hash()` returns
  `std::nullopt` unconditionally (`connection_plain.hpp:24`). Over plain TCP the resume branch is
  therefore unreachable by construction and every `SessionSetupReq` is answered
  `OK_NewSessionEstablished`. Read against `everest-core` @ `b61bb12` on 2026-08-08. **That is why the
  matrix cell is `▢`**: every `-20` run against them so far was EIM over TCP, and the runs over TLS were
  not pause/resume runs. The run that would close it is specified in
  [`docs/open-work.md`](open-work.md) — and it raises a question about *our* SECC, which binds nothing.
- **A complete Plug & Charge *charge*, in either protocol.** The signature is the part that is ours and it
  verifies; the authorization backend is the part that is theirs and their SIL does not have one
  (`NO_CONNECTOR_AVAILABLE`). `config-sil-ocpp201-pnc.yaml` is the configuration that would, and it needs
  an OCPP 2.0.1 CSMS on the other end — a different counterparty and a bigger piece of work than this
  harness has set up.
- ~~**ISO 15118-20 AC past `PowerDelivery(Start)`.** Their SIL expects *their own* EV module to close the
  contactor, so driving the CP line is not enough.~~ **Closed 2026-08-13 — and the explanation above was
  wrong, not merely incomplete.** Their SIL never needed its own EV: `EvseManager` produced the
  contactor confirmation in our topology too, from the CP line we were driving. It arrived **4,948 s
  before** `PowerDelivery` opened its 3 s window, and `libiso15118` — unlike `EvseV2G`, which latches
  the value in a loop that re-tests it — remembers nothing that arrived early. Six runs and three
  car-simulator sequences went into that wall over four months; what got past it was publishing the same
  car command about five seconds later, into the window. **Five complete sessions, three `AC` and two
  `AC_BPT`, 16/16 `OK`, nothing injected and nothing patched**, with the old sequencing run between them
  as a control that still fails at 3,047 s
  ([`…-d20-ac-contactor-window`](interop-runs/2026-08-13-everest-d20-ac-contactor-window/notes.md)).
  The instrument is [`carsim-on-trigger.sh`](../tools/interop-everest/carsim-on-trigger.sh), and the
  distinction from `contactor-report.sh` is what makes these sessions count: that one asserts a hardware
  fact to the HLC layer, this one moves the simulated car and lets the station reach its own conclusion.
  <br>**Reading their source to explain that wall, on 2026-08-09, had turned up a defect beside it** —
  `d20::state::PowerDelivery` assigns the `ClosedContactor` event **pointer** to its `bool`, so a
  board-support module reporting the contactor *open* latches it closed, cancels the timeout that would
  have refused, and answers `PowerDeliveryRes(OK)`. Drafted the same day:
  [`everest-iso20-ac-contactor-latch.md`](reports/everest-iso20-ac-contactor-latch.md) — **withdrawn
  2026-08-11, fixed on `main` by the missing `*` before it was ever sent** — and
  **reproduced against their running station that afternoon** — one command on their own MQTT
  interface, `OK` 95 ms later where the control waits 3.000 s and refuses, 2 of 2
  ([`2026-08-09-…-contactor-injection`](interop-runs/2026-08-09-everest-ac-contactor-injection/notes.md)).
  Worth keeping the two apart: **the wall was ours to get past and the defect is theirs to fix**, they
  sit on the same code path, and neither causes the other. The reason given at the time — that our runs
  "never produce a `ClosedContactor` event at all" — held for the `control` topology and not for
  `cphold`, where one is produced and lands in a state that does not read it. Either way the timeout
  branch is the one taken, which is what made the injection a clean measurement.
- **Megawatt *power*.** Their MCS SIL is electrically an ordinary charger and clamps to 22 kW whatever is
  declared. The catalogue and the envelope are validated; the current is not.
- **A conformant -20 curve, from this counterparty.** Their `create_certs.sh -v iso-20` emits **P-256** —
  with their own `TODO` beside it — where ISO 15118-20 prescribes secp521r1 (or Ed448) for the PKI and
  the key exchange alike. So every -20 TLS session run here against EVerest is carried by -2-grade key
  material, which is a property of their test PKI rather than of their stack. It is not unusual:
  Josev's -20 PKI is P-256 too, and Schannel cannot do P-521 for TLS at all, so a test PKI that must
  work everywhere drifts to P-256 almost by force. eVDriveFlow was the first counterparty to supply what
  -20 describes ([`2026-08-07-edf-mutual-tls13`](interop-runs/2026-08-07-edf-mutual-tls13/notes.md)),
  which is what makes this a gap in *their* material rather than a missing capability here.
  <br>**Filed 2026-08-08**, once the script rather than one certificate had been read:
  [`josev-iso20-pki-curve.md`](reports/josev-iso20-pki-curve.md). It is Josev's `create_certs.sh`, which
  this counterparty carries as a fork — so the report goes to both. And the sharp end of it is not TLS
  strength: `-20` contract provisioning wraps the contract key by ECDH on secp521r1 or x448, so a P-256
  provisioning certificate cannot complete that exchange in any implementation. We measured that too,
  the same day, against their car.
- **A CertificateInstallation their car can *finish*.** Their EV does send a real one — the OEM run
  above — but the *response* handler it is Josev-derived from is `raise NotImplementedError`, so the
  session dies on our answer. Their P-256 OEM leaf could not have unwrapped the contract key anyway.
  Two independent stacks have now sent the request and neither can consume the reply, which makes the
  wrap itself structurally uncheckable rather than merely untested.
- **A byte-level codec diff.** At HEAD both station modules are cbV2G, which is our own oracle. The
  independent-codec evidence from this counterparty is the 2023 image's OpenV2G, and it is a working
  decoder in both directions rather than an octet comparison.

---

## Current state

The full forward matrix — -2 DC/AC, -20 DC Scheduled **and** Dynamic, **-20 AC and AC_BPT**, `IsoMux` in
all four offer shapes and over TLS, -20 DC over mutual TLS 1.3, **-2 AC over TLS 1.2**, MCS, MCS_BPT,
DC_BPT — is green against **2025.10.0** and re-validated against **2026.02.1 built from source**. The
reverse direction runs and is recorded. There is no unattempted cell left in EVerest's column of the
matrix; what remains is the list of walls above and the drafts waiting to be sent. The last untried AC shape, a **reverse AC** session,
ran the same evening and was the most productive hour of the day: it found a defect of *ours* — the
reverse fixture defaulted its SAP catalogue to DC, so every reverse `-20` run ever made announced
DC-only, and it took an AC EV on the far end to notice — and then measured one of theirs, their EV
pacing the AC charge loop at **≈532 ms** against the 500 ms `[V2G20-1500]` allows a station to wait
([`…-d20-ac-reverse`](interop-runs/2026-08-13-everest-d20-ac-reverse/notes.md)). **That pair is worth
more than either half:** their own `-20` station flattens the same override to 60 s
([the twenty-second filing](reports/everest-d20-sequence-timeout.md)), so a station that waits 60 s
never discovers that its own EV takes 532 ms. Neither side is visible without a conformant third party.

**Filed 2026-08-14, and the EV half belongs to a different project** — the car is `PyEvJosev`, so
[the forty-seventh](reports/josev-iso20-evcc-charge-loop-pacing.md) goes to SwitchEV and EVerest's fork
of it rather than to everest-core. Table 216 gives the EVCC 0,25 s (`[V2G20-1499]`) against the
station's 0,5 s, so the deviation is 2,1× — but it is a **performance** criterion by Figure 212's own
legend, not an error one, and the report is written to that distinction. Two things emerged in the
writing that the run had not asked: the ≈532 ms is the session's per-message cost rather than a
charge-loop decision (the setup phase runs at ≈573–600 ms per exchange in the same logs), and
`V2G_EVCC_SEQUENCE_PERFORMANCE_TIME` is absent from their timeouts file entirely — a different shape
from the SECC constants beside it, which exist and are simply never referenced.

The other run that appeared on 2026-08-13 was done the same evening. **AC over mutual TLS 1.3** — two
`AC` and two `AC_BPT` sessions, their `Handshake complete!` and `Verify certificate result is okay`,
the contactor window unchanged at +832…1048 ms because the handshake is spent before `PowerDelivery`
([`…-d20-ac-tls13`](interop-runs/2026-08-13-everest-d20-ac-tls13/notes.md)). Without it the two new AC
cells were green for a transport `[V2G20-1237]` and `[V2G20-2356]` both forbid, which is a distinction
this page should keep making: **complete is not conformant**, and the matrix cannot tell them apart on
its own.

**2026-08-14 closed the oldest gap in this column — `-2` AC over TLS 1.2 — and the transport was the
easy half.** Four sessions, 13/13 `OK` each, one config line changed (`tls_security: force`); `-2` TLS is
unilateral, so it needed no vehicle credential and no PKI regeneration. Their side is conformant where it
counts: **TLS 1.2 with `TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA256`**, one of the two ISO 15118-2 prescribes
and the one [tux-evse's configs lack](reports/tux-evse-tls.md), and `EvseV2G` sends its **whole chain**
where `Evse15118D20` sends a bare leaf. It also survives a refused handshake, which the `-20` module's
accept loop does not.

**Being able to say that second thing cost a fix of ours**, and it is the most useful result of the day.
The arm anchored at the V2G root **alone** was refused by us and accepted by `openssl s_client -CAfile`
against the same station minutes apart: `InteropEnvironment.DevTlsOrNull` discarded the validation
callback's `X509Chain` — the same defect the app fixed on 2026-08-09, in a second copy the interop
fixtures had of their own. Every TLS run in this document had its intermediates handed to it in the trust
bundle, which passes either way, so nothing could see it; only a root-only anchor can
([`…-iso2-ac-tls12`](interop-runs/2026-08-14-everest-iso2-ac-tls12/notes.md)). **No earlier run is
invalidated** — a superset bundle validates the same chains — but *"we verified their chain"* was a
weaker claim than it read, in every one of them.

**The last two AC cells fell on 2026-08-13, and the lesson is not about AC.** They had stood since
2026-08-03 behind an explanation this page stated with confidence four separate times — *their SIL
expects its own EV module* — which their own `cphold` log from 2026-08-09 had already contradicted, in a
file sitting in this repository. Nobody re-read it, because the wall had a name. What was actually
needed was one MQTT publish about five seconds later. **A named wall stops being measured**, and that is
the failure mode this page should be read for, more than any individual cell.

**As of 2026-08-11 those drafts were re-tested against everest-core `main`, not just the tag they were
written against.** Two are withdrawn — the maintainers fixed them independently, which is the strongest
external check this project's judgement has had. One was rewritten around what survived. Two had their
argument replaced without their measurement changing. Twelve stand verbatim. The re-test is two scripts
([`tools/reports-audit/`](../tools/reports-audit/README.md)) and is worth re-running before any of them
goes out: `main` moves daily, and this column is the one that moves.
