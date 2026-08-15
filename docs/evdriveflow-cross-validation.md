# eVDriveFlow cross-validation, in detail

The long form of the eVDriveFlow column in the [interop matrix](../README.md#the-interop-matrix--who-we-test-against-and-what-happened):
every scenario that has run against **EDF-Lab/eVDriveFlow**, what each one caught, and what stays out of
reach.

It is the shortest of the four columns and the highest yield per exchange. Thirteen messages in one
direction and four in the other found **one defect in this project that every other oracle was
structurally blind to** — the one that started the [assumed-values sweep](assumed-values-sweep.md) — plus
three in theirs. Read alongside [Josev](josev-cross-validation.md) (the other independent codec) and
[EVerest](everest-cross-validation.md) (the independent charger).

Tooling: [`tools/interop-evdriveflow/`](../tools/interop-evdriveflow/README.md). Runs:
[`2026-08-01-edf-iso20-dc-notls`](interop-runs/2026-08-01-edf-iso20-dc-notls/notes.md) (forward),
[`2026-08-01-edf-iso20-dc-dynamic-reverse`](interop-runs/2026-08-01-edf-iso20-dc-dynamic-reverse/notes.md)
(reverse) and [`2026-08-06-edf-stdin-wall`](interop-runs/2026-08-06-edf-stdin-wall/notes.md), which
**took the wall down**.

---

## What eVDriveFlow is, and why it was picked

**EDF R&D's Python implementation of ISO 15118-20 Edition 1** (MIT, `60249c3`, 2023-04-17). It plays both
roles, and it encodes with **OpenEXI/Nagasena** — the jars sit in their `shared/lib/`, which is why the
install needs a JDK. That makes it the project's **second independent codec** after Josev's EXIficient,
and the first one to meet -20 at session level.

It was chosen for a combination nothing else here could witness: **-20 Ed. 1 + DC bidirectional + Dynamic
control mode + mutual TLS 1.3**. The app's [`docs/pki-model.md`](../libs/EVSimulatorApp/docs/pki-model.md)
pins -20 to TLS 1.3 with a mutual handshake, and
until this counterparty our own tests were the only thing that had ever checked we do it right — a second
implementation that *requires* it is an oracle rather than a second opinion from ourselves.

**All four are now reached** — within two days of 2026-08-06, when the wall that held them turned out to
be a closed file descriptor rather than a state machine. Dynamic control mode runs end to end; mutual
TLS 1.3 followed, with `TLS_AES_256_GCM_SHA384` and **secp521r1 on both sides** — which is what -20
prescribes and, remarkably, what no counterparty here had supplied before: Josev and EVerest both ship
P-256 test PKIs, so this is the first peer whose -20 key material is -20's rather than -2's. Then DC_BPT
after it, their car declaring 48 kW of discharge against our 50 kW, each envelope read by the other's
codec. What stands in front of a
*complete charge loop* is no longer a capability but a defect of theirs. The story is below, in order:
the wall, why it was invisible, and what stands behind it.

---

## What has run

- **Forward, -20 DC EIM over plain TCP — 13 exchanges, aborted in the charge loop.** SupportedAppProtocol
  through PowerDelivery all `OK`, then `DC_ChargeLoopReq` went unanswered. **Every frame we sent was
  decoded by an independent EXI implementation**: twelve of our -20 messages across CommonMessages and the
  DC set, read without a single decoding complaint, and their responses round-tripped through ours the
  same way. Not a byte diff — we never encoded the same content with both and compared octets — but a
  working independent decoder in both directions, at -20 session level, which nothing else had given.

  **That run needed their station patched, and since 2026-08-15 it does not.** Getting past their fifth
  message meant editing `process_service_discovery_request.py` inside a throwaway container, so every
  forward result carried an asterisk. Our EV now sends the optional `SupportedServiceIDs` element instead
  — its own option under Table 38 of `[V2G20-1248]`, and the thing their unguarded dereference is looking
  for — and their **unmodified** station runs the same sequence to `PowerDelivery`
  ([`…-edf-session-id-460`](interop-runs/2026-08-15-edf-session-id-460/notes.md)). The wall one message
  further on is theirs too, and it is the same defect a third time (the `DisplayParameters` row below).
  A workaround that lives in our car rather than in their tree is the difference between a result and a
  result with an asterisk.

- **Reverse, their EV against our SECC — 4 exchanges, and three firsts.** Their EV said hello, asked how to
  authorize, and left. What it produced anyway: the **first live SDP discovery** in this project against a
  non-Josev peer, the **first `trace.json` recorded from a real counterparty**, and finding 4.

  Their EV has no fixed-endpoint option at all — `start_new_session` connects strictly to whatever
  discovery returned — so the counterparty-agnostic SDP shim from the Josev harness answered it unchanged,
  and a station running **on the Mac** was discovered by an EV running **in a Linux container**. The
  reverse direction had been documented as needing SDP on a shared L2; it does, but the station itself
  does not have to be there.

- **Chain validation, and it found a defect of ours nobody else could have (2026-08-09).** Our station
  validated their car's TLS client chain against their roots: **their OEM root alone** anchors at
  `CN=OEMRootCA`, their two VEHICLE Sub-CAs without the root are refused, and their **V2G** root — a
  real root of the same vendor, wrong branch — is refused too. First **secp521r1** chain the validator
  has judged, and the first counterparty here whose car hangs off an **OEM root separate from the V2G
  one**, as the CharIN PKI describes; Josev's own `CertPath` enum anchors its vehicle branch at the V2G
  root instead, so this shape had never appeared.

  That difference is what caught the bug. Both .NET TLS call sites had been discarding the intermediates
  the peer sends, so *every* peer was judged on its bare leaf — and against EVerest the day before, the
  resulting rejection had been written up as a property of their station. Their car's chain, and the
  same run against a pre-fix binary, made the real cause visible
  ([`…-edf-chain-validation`](interop-runs/2026-08-09-edf-chain-validation/notes.md)).

---

## What it found in **us** — the response code that was never read

This is the finding that justifies the harness, and it is worth stating in full because it is the cleanest
example this project has of what a foreign stack is *for*.

`DC_CableCheckRes` came back with **`ResponseCode = FAILED`** — and our EVCC carried on. PreCharge,
PowerDelivery, into the charge loop. The cable-check loop looked only at `EVSEProcessing`, and `Expect<T>`
checked the message *set* and *type*:

```csharp
if (res.EVSEProcessing == Dc20.Processing.Finished) break;   // …and nothing else
```

There was **no `ResponseCode` check anywhere in the -20 EVCC path**. A station could have answered FAILED
to every message of a session and our car would have driven it to completion.

**Why nothing here could have found it.** At the time, our own SECC never answered FAILED — so no recorded
session contained one and no replay could produce one. It took a station that says FAILED — for a reason of
its own, in virtual mode with no hardware — to make it visible.

Our SECC has since gained one, on **2026-08-06**, out of the MCS_BPT work: it refuses a charge-parameter
set that contradicts the selected service with `FAILED_WrongChargeParameter` (`Secc20Ac.cs:69`,
`Secc20Dc.cs:106`). The argument above is unaffected, and it is worth saying why rather than quietly
leaving the sentence in the past tense: **the corpus is what a replay reads, and none of its 16 recorded
sessions contains a FAILED.** A guard has to be *exercised* to guard anything. That an EVSE-side refusal
exists in the code does not put one on the wire in a recording — which is the whole reason this defect
needed a foreign station in the first place.

Fixed the same day, in **both protocols and all three languages**. `Evcc20Base.RefuseOnFailure` sits in
the one place every -20 response passes through; `OK*` and `WARNING*` continue — a warning is explicitly
the code for "something is off and the session goes on" — and `FAILED*` ends the session with the message
and the code in the error. -2 needed a different shape: it has no common response base, so the code is
read by property name, and `Evcc2FailureHandlingTests.EveryResponseTypeIsCheckable` enumerates the
generated assembly to prove every response type carries one. A hand-written switch would have been
**fail-open** — the one forgotten, or the one added later, goes unchecked, which is the failure being
fixed.

**It has paid three times since**, all against EVerest: the AC energy-transfer-mode refusal, the MCS_BPT
`FAILED_WrongChargeParameter`, and the AC_BPT `FAILED_ContactorError` each arrived as a named code in the
message it happened in, rather than as a session that ran on and failed somewhere less obvious.

And it is the first of the three findings that made the roadmap ask for a **sweep** rather than a fourth
counterparty — *a value taken from our own assumption where the protocol supplies one*
([`assumed-values-sweep.md`](assumed-values-sweep.md)).

---

## What it found in **them**

Three, all reported in the run notes with the code that causes them:

1. **An optional element dereferenced.** `process_service_discovery_request.py` reads
   `payload.supported_service_ids.service_id` unconditionally; in -20 that element is `minOccurs="0"`, and
   omitting it means *"no filter, list everything"*. Our EVCC omits it — legally — and their session dies
   with `AttributeError: 'NoneType' object has no attribute 'service_id'`. **Filed 2026-08-10** as their
   issue 3: [`reports/evdriveflow-service-discovery-filter.md`](reports/evdriveflow-service-discovery-filter.md),
   which also counts the family it belongs to — seven `hasattr`-on-an-`Optional` sites in four files, on
   both sides.
2. ~~**The charge loop assumes Dynamic control mode.**~~ **Overstated, corrected 2026-08-10.**
   `process_dc_charge_loop_request.py` does read `payload.dynamic_dc_clreq_control_mode` without
   checking, and ours did send the Scheduled variant — but their station advertises `ControlMode = 2`
   (Dynamic) in the only parameter set it offers for either service
   (`evse_dummy_controller.py:109-114`), so a conformant EV never selects Scheduled. **We reached that
   line because our own EVCC ignored the catalogue**, which was fixed the same month. The inconsistency
   is not internal to their side; it was ours, and this entry claimed otherwise for nine days.
3. **An EVSE offering PnC *and* EIM breaks their EV.** Their
   `wait_for_authorization_setup_response.py` walks the offered list and raises `NotImplementedError` on
   the first entry it does not support, even though EIM — which it does support — is the very next entry.
   An EVSE offering PnC alongside EIM is the ordinary case in the field, and `[V2G20-2566]` says so:
   the SECC may offer EIM, or PnC, or both.
   **Filed 2026-08-10** as their issue 4:
   [`reports/evdriveflow-authorization-setup.md`](reports/evdriveflow-authorization-setup.md).
   <br>It was nearly filed as two. The termination we saw behind it looked like a second protocol
   defect, and `[V2G20-1577]` would have made it one — the EVCC *shall* send an `AuthorizationReq`
   after an `AuthorizationSetupRes` with `OK` and `CertificateInstallationService = False`. It is not:
   it is the `stdin` wall below, already filed as issue 1. **The report was rewritten before it went
   anywhere**, and half of it is now the paragraph explaining that, since without it the report reads
   as a duplicate. The section below is exactly where that trap lies for the next reader.
   <br>**Isolated on 2026-08-10.** Every earlier observation was taken with the stdin bug also active,
   so the missing cell was stdin open *and* a PnC-and-EIM offer. It was run: their EV raises at
   `wait_for_authorization_setup_response.py:36` in exactly the configuration where it otherwise
   charges ([`2026-08-10-edf-pnc-eim-stdin-open`](interop-runs/2026-08-10-edf-pnc-eim-stdin-open/notes.md)).
   The two failures even have different fingerprints — the stdin wall ends a session cleanly with
   `SessionStopReq` after four exchanges, this one dies inside the handler after three.

Findings 1 and 3 were worked around **in their copy, inside a throwaway container**, to get past them.
Our stack was not changed for either. One note on method that is worth keeping: the finding-1 workaround
had to fall back to `[2]` (plain DC) rather than `[6, 2]` — choosing 6 made their charge-parameter handler
read BPT-only fields out of a plain-DC request and fail. **A workaround that manufactures the next error
is worse than none.**

---

## The wall, and the experiment that isolated it

> **Solved on 2026-08-06 — it was `stdin`.** Their EV arms a "press Enter to stop the session" listener
> in `TCPClientProtocol.__init__`, unconditionally, and awaits `sys.stdin.readline` in an executor. At
> EOF that returns immediately, so `set_stop()` runs one millisecond after the connection is made and
> `stop_session` is true before any protocol message. `process_reaction` then **replaces** whatever the
> state machine built with a `SessionStopReq`, in the first state that allows it — and
> `exitable_states = states[2:-3]` starts at `WaitForAuthorizationSetupResponse`. The 2026-08-01 rig
> started their EV with `docker exec -d`, which is stdin at EOF.
>
> Run again with stdin held open, nothing else changed: **4 exchanges became 15**, through
> `ScheduleExchange`, `CableCheck`, `PreCharge` ×3, `PowerDelivery` and into `DC_ChargeLoop`. Full
> account, both logs and the A/B:
> [`2026-08-06-edf-stdin-wall`](interop-runs/2026-08-06-edf-stdin-wall/notes.md).
>
> The section below is kept as it was written, because the reasoning that *narrowed* it is still worth
> copying — and because it is a fair record of how an honest open question looked from the inside.

Their EV terminates the session after `AuthorizationSetupRes`, and **the root cause inside their state
machine has not been identified.** That is recorded as an open question rather than guessed at.

What *was* settled, and how, is the part worth copying. The finding-3 workaround removed the crash and
their EV then terminated cleanly instead of selecting EIM — leaving it impossible to tell from the log
whether that termination was theirs or an artefact of the patch. So our SECC gained `OfferPlugAndCharge`
(CLI `--no-pnc`, fixture `V2G_INTEROP_NO_PNC=1`) to settle it **without patching anything of theirs
further**, mirroring `PreferDynamicControlMode`, which exists for exactly this class of reason.

Re-run with an EIM-only offer, their EV received exactly the one service it is configured for — and
**still sent `SessionStopReq`**. Two things follow: the workaround is exonerated, and the wall is not an
offered-services problem. Further diagnosis means reading their state machine rather than running interop.

> Both conclusions held. The `AuthorizationReq` their handler built was correct all along — it was
> discarded one layer down, which is exactly why no experiment at the protocol level could move it.

> A verdict worth generalising, from that same run: the fixture's assertion **passed**. Their EV sent a
> well-formed `SessionStop(Terminate)`, our SECC reached its terminal state, `IsDone` was true, and the
> recorder even built a `SessionTrace`. Nothing was charged. *Four exchanges against a sixteen-message
> reference* is what `flow.md` showed at a glance — which is why the flow report, not `IsDone`, is the
> verdict that carries the information.

---

## What stays out of reach

Rewritten 2026-08-06: the authorization wall was the common cause, and it is gone.

- **Mutual TLS 1.3** — ✅ **done, 2026-08-07, on both of our TLS backends.** TLS 1.3 with
  `TLS_AES_256_GCM_SHA384`, both peers authenticated, and **secp521r1 on both sides** — the curve -20
  prescribes, and the first time a counterparty supplied it (Josev and EVerest both ship P-256; Schannel
  cannot do P-521 for TLS at all, which is most of the reason the field drifts). Run first through
  `SslStream`/OpenSSL and then through the **BouncyCastle** backend the app carries for exactly this
  profile — which until that night had never met anything but itself. 15 exchanges either way. Their PKI
  had to be regenerated with their own script first — the certificates in the repository expired in 2022.
  [`2026-08-07-edf-mutual-tls13`](interop-runs/2026-08-07-edf-mutual-tls13/notes.md).
- **Dynamic control mode** — ✅ **reached.** `ScheduleExchange` negotiated it and the session ran into
  the charge loop.
- **DC bidirectional** — ✅ **done, 2026-08-07.** Their EV picks service 6 out of our `{2, 6}`, and
  `DC_ChargeParameterDiscovery` carried a real envelope each way: their car 48 kW / 137 A of discharge
  against our 50 kW / 200 A, each read by the other's codec, then a `BPT_Dynamic` charge loop. No
  energy reverses — their charge-loop defect ends the session first.
  [`2026-08-07-edf-dc-bpt`](interop-runs/2026-08-07-edf-dc-bpt/notes.md).
- **A complete charge loop, either direction** — the one thing still walled, now by their
  `hasattr`-on-an-optional-field defect (below) rather than by anything of ours.
- **SDP in the forward direction** — the relay path that makes this runnable from a Mac is exactly what
  bypasses discovery. The reverse direction did exercise it, which is the compensation.
- **Plug & Charge** — ⛔ **structural, established 2026-08-11: they implement none.** This entry used to
  be a `▢` with a condition attached — *first find out whether they do contract certificates at all* —
  and the condition turned out to be the whole question. Neither role has a `CertificateInstallation`
  handler; `ContractCertificateChain`, `PnC_AReqAuthorizationMode`, `SignedInstallationData` and
  `OEMProvisioningCert` occur only in the xsdata-generated bindings, ISO's schema and the Sphinx output
  of both, plus two Table 214 timeout keys with nothing to time. Their README does not claim it and
  `PnC` appears nowhere in their documentation. **It took no run**: their `AuthorizationSetupRes` was
  already in this repository at 20 payload bytes against our PnC-offering 38, with nowhere to put a
  `GenChallenge` and none in it.
  [`2026-08-11-edf-pnc-source-audit`](interop-runs/2026-08-11-edf-pnc-source-audit/notes.md).
  <br>The audit found one latent defect and left it unfiled on purpose: their SECC sets the EIM
  authorization mode unconditionally, whatever the configurable `authorization_services` holds, which
  `[V2G20-1219]` and `[V2G20-2568]` each forbid — but the shipped default is `[EIM]`, where the line is
  right, and reaching it means configuring their station rather than observing it. It is a note on the
  filing that already covers the paired EVCC handler, and it is worth reading beside it: **the offer
  their EV raises on is one their own station will emit if anyone sets the field.**
  <br>Their `origin/main` is still `60249c3` on 2026-08-11 — no commit in two years and four months,
  which also ticks the *has `main` moved?* item that had been open on that filing.
- **Pause / Resume** — `secc/tcp_server.py:130-137` raises `NotImplementedError` for a `PauseSession`
  reaction on the send path. The matrix cell was already `—`; since 2026-08-11 the source says why
  rather than the absence of a run.

Their container also needs an IPv6 network to start at all (`netifaces.ifaddresses(iface)[AF_INET6][0]`
raises `KeyError` on the default bridge), and their conda environment is linux-64 pinned, so the harness
ships a `Dockerfile` that reproduces what matters rather than what they specify. Both are setup facts, not
interop verdicts, and the run notes list all seven deviations from a clean-room run so nobody later
mistakes one for a result.

---

## Current state

**Six runs, both directions, every capability it was chosen for reached.** It is one of two independent
codecs here, the only one that has read our -20 at session level, and the richest source of
defects-per-message this project has: one of ours and **six** of theirs — the four below, a shipped
PKI that expired in 2022, and the charge loop's `DisplayParameters` dereference found on 2026-08-15.

The fourth is the sharpest, and it is the mirror of the finding that started
[`assumed-values-sweep.md`](assumed-values-sweep.md): their charge-loop handler guards with
`hasattr(payload.bpt_dynamic_dc_clres_control_mode, "target_soc")` — always true on a generated
dataclass whose field is `Optional[int]` — and copies our legally omitted `TargetSOC` over its own
configured value. `None * int` ends the session one message later. **Their** code assumed a value the
protocol makes optional; it took a peer that omits it to find out.

What is left is one thing, and it is theirs: the charge loop past the first `DC_ChargeLoopRes`, waiting
on a two-line fix. It is drafted for them, with the stdin behaviour, the expired PKI and the three older
findings beside it, in
[`docs/reports/evdriveflow-headless-session.md`](reports/evdriveflow-headless-session.md). With that
fixed, this rig runs a **bidirectional Dynamic -20 session over mutual TLS 1.3 to its end** — which
would be the most complete interop result this project has against anybody.

**A sixth run, 2026-08-15, and it is the one that measured a filing rather than finding one.** Their SECC
does not read the SessionID it is sent, which had been read out of their source on 2026-08-11 and never
put on the wire: with `DEADBEEFDEADBEEF` and with eight zero bytes, **ten message types were answered
`OK`** — `PowerDelivery` among them — in sessions identical to the control message for message. Their own
debug log prints the id it received three lines above the answer carrying a different one, which is the
shortest route into the issue and it is their record rather than our reading
([`…-edf-session-id-460`](interop-runs/2026-08-15-edf-session-id-460/notes.md)).

It also moved the count in this section: **six of theirs, not five**, the sixth being the
`DisplayParameters` dereference in the charge loop — the same optional-element defect as the first, in a
second file, and the first instance of it reached by a session that had done everything right. And it
closed the last open measurement in `docs/reports/` for any counterparty.

---

## Every claim about their side, in their source

Re-checked on **2026-08-06**, and the last three rows added **2026-08-11**, against
`EDF-Lab/eVDriveFlow` @ **`60249c3`** — the commit every run above met, and still `origin/main` with no
upstream commit since 2023-04-17.

| Claim | In their source |
|---|---|
| An optional element is dereferenced | `secc/states/process_service_discovery_request.py:31` — `if 6 in payload.supported_service_ids.service_id:`, unguarded. **Their own generated model** declares it `Optional[ServiceIdlistType]` (`shared/xml_classes/common_messages/v2_g_ci_common_messages.py:827`), so both halves of the defect sit in their tree |
| The charge loop assumes Dynamic control mode | `secc/states/process_dc_charge_loop_request.py:128,149,161` read `payload.dynamic_dc_clreq_control_mode`; `scheduled_dc_clreq_control_mode` appears **0×** in the file. The one branch is `if self.session_parameters.dc_bpt_selected == True:` — BPT or not, never control mode. **All still true and no longer a finding:** their catalogue advertises Dynamic only (`evse_dummy_controller.py:109-114`, `ControlMode int_value=2`, one parameter set per service), so nothing conformant reaches those lines |
| An EVSE offering PnC *and* EIM breaks their EV | `evcc/states/wait_for_authorization_setup_response.py:30-37`. The loop has **no `break`**, so this is order-independent: `[EIM, PnC]` matches on the first pass and still raises on the second — stronger than the write-up above states |
| TLS is on by default, off by a testing switch | `shared/global_values.py:37` — `SECURITY_PROTOCOL = 0x00  # Use 0x00 to enable TLS or 0x10 to disable TLS [Testing purposes]` |
| Their EXI is OpenEXI/Nagasena | `shared/lib/nagasena.jar`, `nagasena-rta.jar` — which is why the install needs a JDK |
| Their EV has no fixed-endpoint option | `evcc/ev_session_handler.py:50` builds the TCP client from `udp_protocol.tcp_server_address` / `tcp_server_port` — strictly what discovery returned, with no override |
| The configured ports | `secc/evse_config.ini:3` (`tcp_port = 49152`), `evcc/ev_config.ini:3,5` (`udp_port = 49153`, `tcp_port = 49154`) |
| **They implement no Plug & Charge** | Neither `evcc/states/` (16 handlers) nor `secc/states/` (17) has a `CertificateInstallation` one. `ContractCertificateChain`, `PnC_AReqAuthorizationMode`, `SignedInstallationData` and `OEMProvisioningCert` occur **only** in `shared/xml_classes/…` (xsdata output), `shared/xsd_files/…` (ISO's schema) and `doc/build/…` (Sphinx over the bindings); `CertificateInstallationReq` adds two Table 214 timeout keys at `shared/global_values.py:51,57` and nothing else. Both dummy controllers ship `authorization_services = [AuthorizationType.EIM]` (`evcc/ev_dummy_controller.py:111`, `secc/evse_dummy_controller.py:104`) and the SECC ships `certificate_installation_service = False` (`:105`). Checked 2026-08-11 |
| **Their SECC's authorization mode ignores its own service list** | `secc/states/process_authorization_setup_request.py:28-31` — the list comes from `data_model.authorization_services`, then `response.eim_asres_authorization_mode = ""` unconditionally, under their own `# TODO: given the services in authorization services, the response shall be eim or pnc`. `EVSEDataModel.authorization_services` (`secc/evse_controller.py:41`) is a declared `List[AuthorizationType]` and `AuthorizationType` has `PnC`, so the field is settable and unhonoured. `[V2G20-1219]` and `[V2G20-2568]` each require `PnC_ASResAuthorizationMode` once PnC is offered. **Unreachable in the shipped default**, hence a note on the filing rather than a finding |
| **Their SECC cannot pause a session** | `secc/tcp_server.py:130-137` — `isinstance(reaction, PauseSession)` raises `NotImplementedError` on the send path, before any of the `TerminateSession` / `SendMessage` branches |
| **Their SessionID is eight ASCII digits — 26,6 bits where 58 are required** | `secc/evse_session.py:111` — `str(secrets.randbelow(100000000)).zfill(8).encode('ascii')`. The generator is a CSPRNG, so `[V2G20-835]` is met; the **range** is 10⁸, so the 64-bit field carries log₂(10⁸) = 26,6 bits against `[V2G20-2621]`'s 58, and a station repeats an id after ~10 000 sessions. Every byte on the wire is in `0x30`–`0x39`. **Their own docstring cites the requirement** and says it *might have security issues*, so the report is the number and the one-line fix (`secrets.token_bytes(8)`). **Filed 2026-08-11**: [`evdriveflow-session-id-entropy.md`](reports/evdriveflow-session-id-entropy.md), from the [four-stack entropy audit](interop-runs/2026-08-11-everest-d20-rng-entropy/notes.md) that also caught EVerest's `-20` library at ≤ 32 bits. **Worth nothing until the row below is fixed** — a value nobody compares does not have to be hard to guess — and the filing says so rather than leaving it to be discovered. **Measured on the wire 2026-08-15**, 24 consecutive sessions against their SECC: 24 of 24 eight ASCII digits, 24 distinct, min 17 203 515, max 96 751 133 — and legible **without an EXI decoder**, since the field sits at a one-bit offset and one shift turns the payload into readable text ([run](interop-runs/2026-08-15-edf-session-id-entropy/notes.md), [tool](../tools/interop-evdriveflow/session-id-from-frames.py)). The first data point was already in this repository from 2026-08-01 and nobody had looked |
| **Their SECC never reads the incoming SessionID** | All fifteen `secc/states/process_*_request.py` build the response header as `MessageHeaderType(self.session_parameters.session_id, int(time.time()))` and **none** reads `payload.header.session_id`; `FAILED_UnknownSession` appears nowhere outside `shared/xml_classes/`. So `[V2G20-460]` is unimplemented and any SessionID at all is served as the session owner's. **Filed 2026-08-11**: [`evdriveflow-session-id.md`](reports/evdriveflow-session-id.md), and the probe was first run against EVerest's `-20` station, which **refuses** the same all-zero id ([`…-iso20-session-id-probe`](interop-runs/2026-08-11-iso20-session-id-probe/notes.md)). **Measured on their station 2026-08-15 and it is no longer source-only**: with `DEADBEEFDEADBEEF` and with eight zero bytes, **ten message types were answered `OK`** — `AuthorizationSetup`, `Authorization`, `ServiceDiscovery`, `ServiceDetail`, `ServiceSelection`, `DC_ChargeParameterDiscovery`, `ScheduleExchange`, `DC_CableCheck`, `DC_PreCharge`, `PowerDelivery` — in sessions identical to the control message for message. The grep was re-run **inside the container that answered**, so it is the code that was running. Ten of the thirteen handlers the rule applies to; `DC_WeldingDetection` and `SessionStop` sit behind the `display_parameters` row below ([run](interop-runs/2026-08-15-edf-session-id-460/notes.md)) |
| **Their charge loop dereferences the optional `DisplayParameters`** | `secc/states/process_dc_charge_loop_request.py:114` (BPT) and `:176` (unidirectional) read `payload.display_parameters.present_soc` with no check. `DisplayParameters` is `minOccurs="0"` in `ChargeLoopReqType`; our EVCC omits it, and their station raises `AttributeError: 'NoneType' object has no attribute 'present_soc'` and drops the connection. **The same family as the `SupportedServiceIDs` row above, in a second file — and this one is reached by a car that has done everything right**: service selected from their catalogue, Dynamic negotiated, `CableCheck`/`PreCharge`/`PowerDelivery` all `OK`. Nobody had seen it because line 31 stops every session five messages earlier. Added to [`evdriveflow-service-discovery-filter.md`](reports/evdriveflow-service-discovery-filter.md) rather than filed separately: same one-line pattern, same sweep. Measured 2026-08-15 |

Not checkable from the source: **why their EV terminates after `AuthorizationSetupRes`**. The wall above
is recorded as an open question, and reading their state machine — rather than running more interop — is
still what would settle it.
