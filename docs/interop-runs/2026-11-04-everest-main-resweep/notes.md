# 2026-11-04 — every EVerest finding re-read against `main`

**What this run is:** not a session on the wire. A re-read of all twenty-four EVerest report files in
[`docs/reports/`](../../reports/) against `everest-core` **`main` (`5d83027`, 2026-11-03)**, because
the premise every one of them rested on — *"not sent, and unchanged upstream"* — had quietly stopped
being true. **Ten reports are fixed outright and two more in part** — and for eight of the ten that is
news to this repository. Nobody told us; the fixes are in their history.

**How it was read.** A `--depth 1` clone, read out of the object database rather than a worktree
(`git ls-tree` → `git cat-file`), because the scratchpad path plus this tree's depth exceeds Windows'
`MAX_PATH` and the checkout aborts halfway. The commit log came from the GitHub API per module path.
**The line numbers in the reports are all stale** — this tree has moved a long way — so every check
was made by *content*, never by the cited line, and the moved locations are recorded below.

**The one thing that is not a code change, and matters most**

`EvseV2G` and `IsoMux` are **deprecated in 2026.10.0, earliest removal 2027.04.0** (#3044), superseded
by `Evse15118D20`, which now runs DIN SPEC 70121, ISO 15118-2 **and** ISO 15118-20 on the C++ stack
(#2485 — new `lib/everest/iso15118/src/iso15118/{d2,din,message_2,message_din}/`). Their own manifests
say so. Eight of our reports are against those two modules, so eight reports now aim at code with a
removal date — which changes what they are worth, in both directions:

- A fix in `EvseV2G` buys at most one release cycle.
- **The same defect in the new `d2/` path would be worth far more** — and that is unmeasured. This run
  did not check whether the `-2` findings were carried into the C++ `-2` implementation. That is the
  most valuable follow-up this sweep produced, and it is *new* work, not a re-read.

---

## Fixed upstream — ten reports, outright

| report | fixed by | verified against `main` by |
|---|---|---|
| [`everest-evsev2g-paymentdetails-crash`](../../reports/everest-evsev2g-paymentdetails-crash.md) | **#3047**, merged 2026-10-27 | the `err` guard is now `iso_server.cpp:1187` and the use `:1193` — the order the report asked for |
| [`everest-evsev2g-session-log-responses`](../../reports/everest-evsev2g-session-log-responses.md) | **#2566**, 2026-09-16 | `v2g_server.cpp:274-277` takes a response's length from `exi_bitstream_get_length(&conn->stream)` and a request's from `payload_len`; the comment states the distinction |
| [`everest-d20-rng-entropy`](../../reports/everest-d20-rng-entropy.md) | new `detail/random.hpp` | `fill_random()` is OpenSSL `RAND_bytes`, and its doc comment names the defect: *"must come from a CSPRNG, not from `std::mt19937`"*, citing `[V2G2-826]`/`[V2G2-835]`. Used at **every** site: `d2/payment_details`, `d2/session_setup`, `d20/session` (×3), `d20/authorization_setup`, `din/session_setup` |
| [`everest-d20-ac-namespace`](../../reports/everest-d20-ac-namespace.md) | the SAP rewrite — **already recorded as fixed** in `sending-order.md` | `session/secc_sap.cpp:98-112` gates every candidate on the configured modes — `is_dc` needs `energy_modes.dc`, `is_ac` needs `energy_modes.ac`. A DC-only station can no longer be talked into the `-20` AC set |
| [`everest-iso20-ac-contactor-latch`](../../reports/everest-iso20-ac-contactor-latch.md) | the `PowerDelivery` rewrite — **already recorded as fixed** in `sending-order.md` | `d20/state/power_delivery.cpp:131-148`: `ac_connector_closed = *control_data`, then `if (not ac_connector_closed)` waits instead of proceeding. "Did not close" is no longer read as "closed" |
| [`everest-evse-security-ocsp-dropped`](../../reports/everest-evse-security-ocsp-dropped.md) | — | `conversions.cpp:447-461`: `to_everest(CertificateInfo)` now copies `ocsp` |
| [`everest-d20-ocsp-absent`](../../reports/everest-d20-ocsp-absent.md) | — | new `Evse15118D20/charger/evse_security_ssl_config.cpp:57-76` forwards `chain.ocsp[].ocsp_path` into `ocsp_response_files`, with a unit test of its own |
| [`everest-d20-eim-rejection`](../../reports/everest-d20-eim-rejection.md) | — | `ISO15118_chargerImpl.cpp:1786-1801` sends an `AuthorizationResponse{authorized, cert_status, token_unknown}` control event whatever the verdict, so a rejection reaches the `-20` stack |
| [`everest-loop-shutdown`](../../reports/everest-loop-shutdown.md) | — | explicit throw-containment. `tbd_controller.cpp:90-99` wraps the SDP callback, and its comment describes our failure mode verbatim — *"would silently kill SDP discovery until the interface changes"*; `connection_ssl.cpp:281` does the same for TLS accept: *"fail this connection rather than throw out of the poll callback"* |
| [`everest-evsev2g-certificate-update`](../../reports/everest-evsev2g-certificate-update.md) | — | `iso_server.cpp:2610-2624`: every branch sets `_isUsed` **and** calls the matching `init_iso2_*ResType` before its handler, `CertificateUpdateRes` included |

**Two of these ten were already known.** `sending-order.md` has carried `everest-iso20-ac-contactor-latch`
and `everest-d20-ac-namespace` as *"fixed on `main` and not sent at all"* since August — but **their own
Status lines still said "draft, not sent"**, so the two documents disagreed and a reader starting from the
report would not have known. That is now resolved in the reports, with where it was verified. The other
eight are new.

**Both halves of hard dependency #1 are fixed.** `sending-order.md` carried it as the sharpest
ordering constraint in the file — *"neither fix alone produces a staple"* — and both landed
independently of us. The dependency is deleted, not reordered.

## Fixed in part — two reports

### [`everest-d20-client-auth`](../../reports/everest-d20-client-auth/README.md) — one of three

- **Issue 3 (server chain selection): fixed.** `config::ChainTlsVersion{ANY,TLS_1_2,TLS_1_3}` tags each
  chain, and `ssl_config_builder.cpp:35-44` → `tls.cpp:1242-1251` selects on the negotiated version.
  The header states the reason the report gave: *"one leaf cannot serve both."*
- **Issue 1 (the EV decides): not fixed — reshaped into a documented switch.** `connection_ssl.cpp:70-71`
  is still `verify_client = false` with `verify_client_on_tls13 = true`, which *is* the mechanism the
  report describes. What is new is `enforce_tls_1_3` — and it defaults to **`false`**
  (`config.hpp:83`), with the `Evse15118D20` manifest spelling out the consequence: *"Set to false to
  offer TLS 1.2 (cipher ECDHE-ECDSA-AES128-SHA256, no client-certificate request), which is required
  for ISO 15118-2 / DIN SPEC 70121."* So the behaviour is unchanged by default and is now
  **acknowledged**. The report has to be rewritten around the default rather than the mechanism, and
  the new argument is sharper than the old: one module now serves `-2`, DIN **and** `-20`, and a
  single default cannot be right for all three.
- **Issue 2 (the TLS profile stops at cipher suites): live, unchanged.** No `SSL_CTX_set1_sigalgs*`, no
  `set1_groups`/`set1_curves`, no `set_client_CA_list`/`add_client_CA` anywhere in `lib/everest/tls`
  or `lib/everest/iso15118`. The `CertificateRequest` still carries no `certificate_authorities`, and
  the signature-algorithm list and group preference are still OpenSSL's.

### [`everest-isomux`](../../reports/everest-isomux.md) — one of four

- **3 (a failed V2GTP header read is ignored): fixed** by #3028 *payload hardening*.
  `IsoMux/v2g_server.cpp:53-67` now tests the return value, and adds an overflow and a buffer-size guard.
- **1 (routes on the first `-20` entry, never reads `Priority`): live.** Refactored into a
  `HandshakeResult` enum; `Priority` is still only logged, and the return is still
  `iso20 ? Iso20Offered : Iso20NotOffered` — *whether* `-20` appears, not which the EV ranked first.
- **2 (TLS capped at 1.2, `-20` routed onto it anyway): reshaped.** The cap is **gone** — no version
  pinning is left anywhere in `IsoMux/` or `EvseV2G/`. But `IsoMux` never sets `enforce_tls_1_3`
  either, so it defaults false: TLS 1.3 is now reachable, and which version is used is the EV's
  choice. This finding has collapsed into client-auth issue 1 and belongs in it, not on its own.
- **4 (boots with `trusted_ca_keys support disabled`): live, different cause.** `tls.cpp:1310-1315`
  still warns when no trust anchor is found, but the condition is now the per-chain `trust_anchor`
  PEM of the multi-chain config, so the report's mechanism needs re-reading before it is sent.

## Still live — seven reports

Each verified by content at `5d83027`. The citations in the reports are stale; the new locations are
given so the next reader does not have to re-do this.

| report | still live because | moved to |
|---|---|---|
| [`everest-evsev2g-session-id-zero`](../../reports/everest-evsev2g-session-id-zero.md) | the `[V2G2-460]` conjunction still carries `received_session_id != 0`, so a zero short-circuits the check | `iso_server.cpp:105-110` (was `:86-91`) |
| [`everest-d20-meter-info`](../../reports/everest-d20-meter-info.md) | `// TODO(sl): Setting EvseStatus, MeterInfo, Receipt` is still the implementation; the request's flag is forwarded as feedback and nothing fills the response | `ac_charge_loop.cpp:167`, `dc_charge_loop.cpp:192` |
| [`everest-d20-sequence-timeout`](../../reports/everest-d20-sequence-timeout.md) | one `TIMEOUT_SEQUENCE = 60 s` for every message, returned unconditionally by `session/iso.cpp:53`. The enum has grown (CPSTATE, STOP_CHARGING, EMERGENCY_SHUTDOWN) but no per-message table exists | `timeout.hpp:37` (was `:30`) |
| [`everest-d20-ac-contactor-edge`](../../reports/everest-d20-ac-contactor-edge.md) | `PowerDelivery::ac_connector_closed{false}` is per-state and only a `ClosedContactor` event *during* the state sets it; `EvseManager` emits `call_ac_contactor_closed(true)` **only** on a `CPEvent::PowerOn` edge, and `subscribe_ac_close_contactor` merely sets a permission flag | `power_delivery.hpp:21`; `EvseManager.cpp:1262-1264` and `:467-470` (was `:1123-1143`, `:396-399`) |
| [`everest-d20-trust-anchor`](../../reports/everest-d20-trust-anchor.md) | still only `CaCertificateType::MO` reaches the TLS verify store. **And the issue-3 fix made this sharper**: with client auth required once 1.3 is negotiated, a station trusting only the MO root *refuses* the OEM-provisioning certificate that `CertificateInstallation` exists to replace | `ISO15118_chargerImpl.cpp:788`; `connection_ssl.cpp:64-71` (was `:201-202`, `:269-276`) |
| [`everest-evsev2g-metering-chain`](../../reports/everest-evsev2g-metering-chain.md) | both halves. Outgoing: no `MeterSignature` is ever set. Incoming: **the wording needs correcting** — `handle_iso_metering_receipt` is no longer an empty function; it publishes the request, logs eight fields and answers `OK`. It still never verifies the signature, which is the finding, but *"reaches an empty function"* is now false and must not be sent | `iso_server.cpp:2606-2609` and the handler below it |
| [`everest-evsemanager-renegotiation-supply`](../../reports/everest-evsemanager-renegotiation-supply.md) | the DC renegotiation branch resets `evse_processing[PHASE_ISOLATION]` and `evse_isolation_status` — both **internal** V2G context — and publishes nothing. `renegotiation_required` never leaves `EvseV2G` (`v2g.hpp:358`; two writers, both internal) | `iso_server.cpp:1897-1915` (was `:1588-1598`) |

[`everest-evsev2g-renegotiation-cablecheck`](../../reports/everest-evsev2g-renegotiation-cablecheck.md)
stays **withdrawn**; nothing in this sweep changes that.

## What this changes about how we work

1. **"Unchanged on `main`" is a measurement with a date on it, and ours had expired.** Every report
   carried one and every one was three months old. The claim needs the commit *and* the date, and
   re-reading it is cheap — this whole sweep was one shallow clone and an afternoon.
2. **Nobody is going to tell us.** Every one of these was closed by a fix that references no issue of
   ours, because we never sent them. The lesson is not *"send faster so we get credit"*; it is that an
   unsent report has no way of learning it is obsolete.
3. **Ten of the twenty-four report files describe code that no longer exists.** The reports were right —
   the code moved the way they said it should. What they bought, held, was nothing.
4. **A fact recorded in one file is not recorded.** Two reports were known-fixed in `sending-order.md`
   and still said *"draft, not sent"* at their own top. Whichever document a reader opens first has to
   be right on its own.

## Next

- Re-read the `-2` findings against the **new C++ `d2/` path** rather than against deprecated
  `EvseV2G`. That is where a `-2` defect now matters, and it is unmeasured.
- Rewrite client-auth issue 1 around the `enforce_tls_1_3` **default**, and fold `isomux` 2 into it.
- Correct `everest-evsev2g-metering-chain`'s *"empty function"* before it goes anywhere.
- Re-read `isomux` 4's mechanism against the multi-chain `trust_anchor` config.
- ~~The same sweep is owed to the other five counterparties; this run covers EVerest only.~~
  **Done the same day:** [`2026-11-04-counterparties-resweep`](../2026-11-04-counterparties-resweep/notes.md)
  — and the result is the mirror of this one. Of their twenty reports, **one fixed, one half fixed,
  eighteen live**, three of the five upstreams not having moved a single commit since we wrote them.
