# Josev cross-validation, in detail

The long form of the Josev column in the [interop matrix](../README.md#the-interop-matrix--who-we-test-against-and-what-happened):
every scenario that has run against **Josev**, what each one caught, and what stays out of reach.
Josev is the counterparty with the most history here — the first independent stack this suite met, and
still the one that has found the most bugs — which is why it has a page of its own rather than a column
of ticks.

Tooling: [`tools/interop-josev/`](../tools/interop-josev/README.md). Per-run write-ups and frame logs:
[`docs/interop-runs/`](interop-runs/). The other counterparties are summarised in the matrix and
written up under the same run directories.

---

## What Josev is, and why it counts double

**Josev** (SwitchEV/iso15118 @ `d645255`) is an independent Python stack that encodes with **EXIficient**
and shares no lineage with our cbV2G oracle — the highest-value conformance signal short of certified
hardware. It is the only counterparty that serves both roles well (EVCC and SECC), which is why it is the
only one with results in both directions across the whole message set. The `[Explicit]`
`JosevInteropTests` gate keeps all of this out of the offline CI run.

- **Record mode (byte-exact codec cross-check, in the offline run):** Josev's own EXIficient-encoded frames decode and
  re-encode *identically* through our codec — for -2, SAP + the full AC and DC charge loops; for -20, SAP +
  **all 30** frames of a full PnC DC session across both schema sets, including the **signed**
  `AuthorizationReq` (`JosevCapturedFrames{,Dc,20}Tests`). On the SAP frames, **our codec ≡ cbV2G ≡
  EXIficient**. This surfaced and **fixed** a real source-generator gap — the xmldsig `Transforms`
  EXI-canonicalisation grammar (`TransformType`'s `minOccurs=0 maxOccurs=unbounded` choice was a mandatory
  single choice with no END-Element alternative; `TransformsType`'s unbounded list left `ListMax=0`); the
  generator now models an optional/repeatable direct `xs:choice` as an EE-terminated optional run, all cbV2G
  vectors staying byte-exact.
- **Live over-the-wire, plain TCP — both directions:** a complete ISO 15118-20 **DC** session runs end to end
  to `SessionStop` both as our EVCC ↔ Josev SECC (forward) and Josev EVCC → our SECC (reverse). Together they
  caught and fixed **ten** real conformance bugs invisible to loopback (the V2GTP SAP payload id, the SAP -20
  namespace, EVCC SessionID adoption, dynamic service negotiation, `MaximumSupportingPoints`, a populated
  `EVPowerProfile`/`PowerToleranceAcceptance`, three SECC content bugs, and the DC **poll-loop self-looping** —
  a real EV polls CableCheck/PreCharge/PowerDelivery/WeldingDetection until each step completes). Both SECCs
  also accept `SessionStop` in any phase (graceful abort).
- **Live over-the-wire, TLS:** the same DC session runs over **TLS 1.2 (unilateral)** and **TLS 1.3 (mutual)**
  forward, and **TLS 1.3 mutual** reverse. Josev's PKI is **P-256**, not the secp521r1 (or Ed448) that -20
  prescribes for the PKI and the key exchange alike, so the Josev-facing TLS is the .NET `SslStream`
  backend; our secp521r1/Ed448 BouncyCastle backend was proven in loopback only until 2026-08-07, when
  eVDriveFlow's P-521 PKI finally gave it a foreign peer. Worth saying plainly because
  it took a third counterparty to make it visible as a pattern: **EVerest's -20 test PKI is P-256 too**
  (with their own `TODO`), and eVDriveFlow — which ships P-521 — was the first peer here whose -20 key
  material matches -20 (`docs/interop-runs/2026-08-07-edf-mutual-tls13/`). Schannel's inability to use
  P-521 for TLS is most of the reason a portable test PKI ends up non-conformant. Found + fixed a client/server certificate-**chain** transmission bug (`SslStream` sent only the
  leaf, breaking a root-only peer) via `SslStreamCertificateContext`.
- **Live Plug & Charge over TLS — fully verified:** our SECC offers PnC + a `GenChallenge` and validates
  Josev's signed `AuthorizationReq` **end to end — `GenChallenge` echo, reference digest, and the ECDSA
  signature all verify** (live run [`2026-07-22-iso20-dc-pnc-tls-verified`](interop-runs/2026-07-22-iso20-dc-pnc-tls-verified/):
  `challenge OK, digest OK, signature OK … grammar=xmldsig-standalone`, then the full DC charge loop to
  `SessionStop`). The signature verification took a short investigation, since Josev signs the `SignedInfo`
  over a **different EXI grammar** than we do: its `to_exi(signed_info, Namespace.XML_DSIG)` selects
  `BuiltInSchema.XSDCore` / `XMLDSIG_Core_Schema_Grammar` — a grammar built from **`xmldsig-core-schema.xsd`
  standalone** — whereas we (like cbV2G, our authoritative reference) encode the `SignedInfo` as a fragment of
  the full `V2G_CI_CommonMessages` schema set. The EXI *Fragment* grammar's leading element event-code width
  tracks the number of global elements in the loaded schema, so the standalone-xmldsig grammar yields a
  **209-byte** `SignedInfo` (one-bit-narrower top-level code, whole bitstream shifted) vs our/cbV2G **210-byte**
  form, though both decode identically. Our own generator reproduces the 209-byte form byte-for-byte from the
  same schema (`WWCP_ISO15118_XMLDSig` project; `XmlDsigStandaloneGrammarReproducesJosev`), so our SECC
  **verifies** Josev-style signatures via a standalone-xmldsig fallback (`XmlDsigInteropVerify`) while our
  default signing stays cbV2G-byte-exact. See `JosevPnCSignatureDiag`.
- **Live Plug & Charge, forward — our EVCC signs, Josev verifies:** the closing counterpart. `evcc
  --contract-cert <pfx>` switches the EVCC from EIM to a **signed** PnC `AuthorizationReq` (challenge echo +
  contract chain, signed in Josev's exact interop form via `XmlDsigInteropSign`: SHA-256 fragment digest +
  `Transforms`=[EXI C14N], SignedInfo over the **standalone xmldsig** grammar, `ecdsa-sha256` raw `r‖s`).
  A real Josev SECC — re-encoding everything with its own EXIficient codec — logs `=> Match: True` and
  **`Signature verified successfully`**, then runs the full DC PnC session to `SessionStop` over mutual
  TLS 1.3 ([`2026-07-22-iso20-dc-pnc-forward-signed`](interop-runs/2026-07-22-iso20-dc-pnc-forward-signed/)).
  With that, **-20 PnC is live-validated in both directions** (they sign → we verify; we sign → they verify);
  CI guard: `Iso20LoopbackTests.DcPncSession_SignedAuthorization_VerifiesAtSecc`.
- **Live Renegotiation:** mid-session renegotiation in every direction Josev supports
  ([`2026-07-22-renegotiation`](interop-runs/2026-07-22-renegotiation/)). **-2 both ways** [V2G2-841]:
  SECC-triggered (`secc --renegotiate` → `EVSENotification.ReNegotiation` → Josev answers
  `PowerDeliveryReq(Renegotiate)` + a second ChargeParameterDiscovery) and EV-initiated
  (`evcc --renegotiate` vs Josev's SECC) — both to `SessionStop`. **-20** [V2G20-1477]: our SECC's
  `ServiceRenegotiation` notification makes a Josev AC EVCC send a real
  `SessionStopReq(ServiceRenegotiation)`, which our SECC answers **without ending the session**
  (re-entry at ServiceDiscovery) — Josev then drops the link anyway (its EVCC posts the terminating stop
  notification before honouring its own `next_state = ServiceDiscovery`; its DC path even hardcodes
  `Terminate`), so the full -20 cycle incl. the second round is guarded by
  `Secc20DynamicModeTests.ServiceRenegotiation_ReentersServiceDiscovery_AndCompletes`.
- **Live Pause/Resume:** sessions can end with `ChargingSession.Pause` and be rejoined on a fresh
  connection ([`2026-07-22-pause-resume`](interop-runs/2026-07-22-pause-resume/)). Forward vs a real
  Josev SECC: the **-2** flow works end to end — Josev preserves the session context across connections and
  answers the resumed `SessionSetupReq` (old id, after a fresh SDP discovery — Josev moves ports on pause)
  with **`OK_OldSessionJoined`**. For **-20**, Josev preserves an *empty* context (its -20 states never fill
  it) and degrades to a graceful new session — a Josev gap; our own -20 resume answers `OK_OldSessionJoined`
  (loopback E2Es for both protocols). CLI: `evcc --pause-resume`, or `--pause`/`--resume <hex>` as separate
  invocations; the SECC keeps accepting while paused.
- **Live ISO 15118-2 Plug & Charge — both directions:** the full -2 PnC message set runs live over TLS
  ([`2026-07-22-iso2-pnc-tls`](interop-runs/2026-07-22-iso2-pnc-tls/)). Reverse (Josev EVCC → our
  SECC): Contract offered, `PaymentDetails` + GenChallenge, Josev's **signed `AuthorizationReq`** and
  **signed `MeteringReceiptReq`** both verify (`challenge OK, digest OK, signature OK,
  grammar=xmldsig-standalone` — Josev's -2 signatures use the same standalone-xmldsig SignedInfo form as
  its -20 ones), full session to `SessionStop`. Forward (our EVCC → Josev SECC): Contract payment with
  `contract.p12`, our signed `AuthorizationReq` gets `=> Match: True` + `Signature verified successfully`
  from Josev's own verifier. Three live findings fixed en route: `SAScheduleList` is mandatory with
  `EVSEProcessing=Finished` [V2G2-905], a SECC must not demand a receipt on *every* status response (a
  Josev EVCC then loops forever), and the -2 SAP offer must carry protocol version **2.0** (Josev matches
  major version, not just the namespace). CI: `Secc2PnCTests` + the `AcPncSession` loopback E2E.
- **Live smart charging / signed tariffs:** the last declared non-goal, closed in both protocols
  ([`2026-07-22-tariff`](interop-runs/2026-07-22-tariff/)). **-2** (`--tariff-cert`): the SECC offers a
  two-tuple `SAScheduleList` whose SalesTariffs are digitally signed into ONE header signature (one reference
  per tariff, §7.9.2.5) and validates the EV's `PowerDeliveryReq(Start)` (`FAILED_TariffSelectionInvalid` /
  `FAILED_ChargingProfileInvalid` [V2G2-761]); the EVCC verifies the signature, picks the cheapest tuple by
  average `EPriceLevel`, and shapes its `ChargingProfile` to that tuple's `PMaxSchedule`. **-20**: the
  Scheduled-mode `ScheduleExchangeRes` carries a rich **signed `AbsolutePriceSchedule`** (power-banded EUR/kWh
  price rule stacks, ECDSA-P521/SHA-512) instead of the flat `PriceLevelSchedule`. Live, three runs: a Josev
  EVCC consumed our signed two-tuple offer, **chose the cheap tuple** and sent a PMax-shaped profile our
  validation accepted; a Josev AC EVCC consumed the signed -20 `AbsolutePriceSchedule`; and — the surprise —
  Josev's SECC **MO-signs its own SalesTariff** (MO Sub-CA2, the actual spec role), which our EVCC
  **live-verified** (`digests OK, ECDSA OK, grammar=xmldsig-standalone`) — a genuine external oracle for the
  -2 tariff-verification path. Honest residue: our -2 combined-grammar signing form and the -20 price-schedule
  signature have no external verifier (Josev's EVCC-side tariff check is a literal `# TODO`; nothing external
  touches -20 price-schedule signatures) — CI-guarded by `Secc2TariffTests` + the tariff loopback E2Es. One
  more Josev quirk: its pydantic `Reference` model requires the schema-optional `Transforms` on *receive* in
  -2 too, so our tariff references include `Transforms`=[EXI C14N].
- **Live -20 contract provisioning (CertificateInstallation):** our SECC announces the service, **verifies a
  real Josev EVCC's signed `CertificateInstallationReq` live** (OEM provisioning chain; digest + ECDSA over
  the standalone-xmldsig grammar) and issues a **signed, Josev-validated** `CertificateInstallationRes` —
  fresh P-521 dev contract, private scalar wrapped via secp521r1 ECDH → ConcatKDF-SHA512 → AES-256-GCM
  (`ContractProvisioning`; self-consistent-only crypto octets — no independent stack implements -20
  provisioning to diff against). Josev then stops at its own `NotImplementedError` (it implements
  cert-install on neither side), so the live exchange reaches the maximum possible; the **full roundtrip**
  (EVCC requests → SECC issues → EVCC unwraps a *working* contract key) runs in-repo
  (`Iso20LoopbackTests.DcCertInstallSession_ProvisionsAWorkingContractKey`). Three interop findings en route:
  Josev mis-frames the req with V2GTP payload type 0x8001 (a `create_next_message` default-arg bug — our -20
  SECC read path tolerates it), our new `OEMProvisioningCertificateChain` fragment codec is byte-identical to
  EXIficient's (1476 B, real-material digest + signature verify), and Josev's pydantic `Reference` model
  requires the schema-optional `Transforms` (our res now includes the EXI-C14N transform). See
  [`2026-07-22-iso20-certinstall-sdp`](interop-runs/2026-07-22-iso20-certinstall-sdp/).
  <br>**The EVerest fork does the same thing, and that says something about both.** On 2026-08-08
  `PyEvJosev` sent the same request against a different PKI, and stopped at the same
  `NotImplementedError` — the gap is upstream, not a packaging choice. What the second run added is the
  half this one could not have: the OEM chain **validated to a foreign root**, which was not yet a thing
  our station could do in July
  ([`…-everest-oem-provisioning-chain`](interop-runs/2026-08-08-everest-oem-provisioning-chain/notes.md)).
  What stays self-checked in both is the key wrap: SwitchEV's provisioning leaf is P-256 and EVerest's
  is too, so neither car could unwrap a secp521r1-wrapped contract key even with the handler written.

- **Live SDP discovery (no shim):** `secc --sdp --interface <nic>` now drives a real Josev EVCC end to end —
  the WWCP `SECC_SDPServer` binds `[::]:15118`, joins `FF02::1`, and answers the EVCC's `SDP_Request` with our
  TCP/TLS endpoint (verified: full PnC-over-TLS session to `SessionStop`, [`2026-07-22-iso20-dc-sdp-noshim`](interop-runs/2026-07-22-iso20-dc-sdp-noshim/)).
  The multicast binding was never actually broken; the earlier "SDP multicast" shim was worked around a
  *policy* default — `SECC_SDPServerOptions.RejectNoTlsRequests` is `true` (TLS-deployment-oriented), so a
  **plaintext** SECC silently dropped a plaintext EVCC's `SDP_Request`. The CLI now sets it from our own TLS
  mode (`RejectNoTlsRequests = !noTls`), so plaintext `--sdp` discovery works too.

- **Live -20 AC:** full AC session (`ACChargeParameterDiscovery` + `ACChargeLoop`) to `SessionStop` against a
  real Josev EVCC, both over **plain TCP + `--sdp`** ([`2026-07-22-iso20-ac-eim-sdp`](interop-runs/2026-07-22-iso20-ac-eim-sdp/))
  and over **mutual TLS 1.3 + `--sdp`** ([`2026-07-22-iso20-ac-tls-sdp`](interop-runs/2026-07-22-iso20-ac-tls-sdp/)),
  each re-confirming plaintext/TLS `--sdp` discovery and PnC signature verify (`grammar=xmldsig-standalone`).

- **Live -20 bidirectional (BPT / V2G):** full **DC_BPT** and **AC_BPT** sessions to `SessionStop`
  ([`2026-07-22-iso20-dc-bpt-sdp`](interop-runs/2026-07-22-iso20-dc-bpt-sdp/),
  [`2026-07-22-iso20-ac-bpt-sdp`](interop-runs/2026-07-22-iso20-ac-bpt-sdp/)). The SECC advertises both the
  unidirectional and BPT energy-transfer services (DC `{2,6}`, AC `{1,5}`) and, because the -20 CPD/charge-loop
  energy-transfer-mode & control-mode are polymorphic (`BPT_*` derives from the unidirectional type), replies
  with the matching charge-**and-discharge** variant whenever the EV sends a BPT request — Josev selects
  `ServiceID 6`/`5` and runs the charge loop with discharge parameters. Backward-compatible: unidirectional
  runs still select service `2`/`1`.

- **Live -20 Dynamic control mode:** full **DC, DC_BPT and AC_BPT** sessions in **Dynamic** mode to
  `SessionStop` ([`2026-07-22-iso20-dynamic-sdp`](interop-runs/2026-07-22-iso20-dynamic-sdp/)). The SECC
  offers both control-mode parameter sets (Scheduled=1, Dynamic=2; `secc --dynamic` puts Dynamic first, which
  is what a Josev EVCC then adopts) and answers ScheduleExchange + charge loop **strictly in kind** across all
  four control-mode variants — fixing a latent wire-type mismatch where a Dynamic-mode EV got Scheduled res
  types (guarded by `Secc20DynamicModeTests`).

All four -20 energy modes any independent stack implements — **DC, AC, DC_BPT, AC_BPT** — now run live against
Josev over TCP and TLS, plain and Plug & Charge, in Scheduled **and** Dynamic control mode. **WPT and ACDP
stay codec-validated only** (record mode, byte-exact vs cbV2G): no live run is possible because Josev — the
only independent -20 stack available — implements no WPT/ACDP session state machines (only AC/DC), and our own
WPT/ACDP projects are codec-only by the same token; a live run would need full session state machines built on
both sides.

---

## Every claim about their side, in their source

Each statement above that says something is *missing* on Josev's side was re-checked on **2026-08-06**
against upstream **`SwitchEV/iso15118` @ `d645255`** — re-fetched and re-checked 2026-08-11, still the head; the commit
[`tools/interop-josev/`](../tools/interop-josev/README.md) pins, so the same code every run above met.
Paths are relative to `iso15118/`.

| Claim | In their source |
|---|---|
| The -20 session context is never filled, so a -20 resume degrades | `ev_session_context` appears **17×** in `secc/states/iso15118_2_states.py` and **0×** in `iso15118_20_states.py`. The -20 resume branch compares against the *live* `comm_session.session_id` and otherwise falls through to their own *"False session ID from EV, gracefully assigning new session ID"* → `OK_NEW_SESSION_ESTABLISHED` (`secc/states/iso15118_20_states.py:152-165`) |

**Filed** as [`reports/josev-iso20-pause-resume.md`](reports/josev-iso20-pause-resume.md) on 2026-08-08, after re-reading the two branches against `d645255`: their own preservation path *does* run for a `-20` session and hands the next connection an empty context, which is the strongest single line of evidence and comes from their log rather than ours. EVerest's vendored fork (`26f7988`) has the same shape.

| Their EVCC-side tariff check is a literal `# TODO` | `evcc/controller/simulator.py:526` — *"TODO If a SalesTariff is present and digitally signed (and TLS is used), verify each sales tariff with the mobility operator sub 2 certificate"* |
| CertificateInstallation is implemented on neither side | `secc/states/iso15118_20_states.py:323` and `evcc/states/iso15118_20_states.py:340` — the same `NotImplementedError("CertificateInstallation not yet implemented")` |
| Their EVCC drops the link after `SessionStopReq(ServiceRenegotiation)` | `evcc/states/iso15118_20_states.py:1153` posts the session-terminating `StopNotification(True, …)` **before** the `service_renegotiation_supported and renegotiation_requested` test at 1160 that sets `next_state = ServiceDiscovery`. **Re-read 2026-08-10 and the cause is one line further down than this row said:** the branch sets `next_state` and never calls `create_next_message(...)` — the only one of 28 transitions in the file that does not — so their framework raises `FaultyStateImplementationError` (*"Field `next_v2gtp_msg` is None but must be set because next state is not Terminate"*, quoted from **their** log in the run) and tears the link down before the stop notification matters. Filed: [`josev-iso20-renegotiation.md`](reports/josev-iso20-renegotiation.md) |
| Their `Reference` model requires the schema-optional `Transforms` | `shared/messages/xmldsig.py:83` — `transforms: Transforms = Field(..., alias="Transforms")`, required, where xmldsig-core leaves it optional |
| **The `-20` charge-loop sequence timeouts are defined and never referenced** | `shared/messages/iso15118_20/timeouts.py` carries `V2G_SECC_SEQUENCE_TIMEOUT_{AC,DC,WPT}_CL = 0.5`, transcribed from Tables 216/217 — and all three have **zero references** outside that file. `ACChargeLoop` and `DCChargeLoop` hand `Timeouts.V2G_SECC_SEQUENCE_TIMEOUT` (60) to `create_next_message`, which is what `rcv_loop` re-arms the socket read from. Two lines to fix, the constants already there. Read on upstream `d645255` **and** EVerest's fork `26f7988`; **not run** — that is the first unticked item on the filing. Filed 2026-08-11: [`josev-iso20-charge-loop-timeout.md`](reports/josev-iso20-charge-loop-timeout.md), [audit](interop-runs/2026-08-11-josev-charge-loop-timeout-audit/notes.md) |
| Their `-20` SECC states log a timeout they do not wait on | All 17 pass `Timeouts.V2G_EVCC_COMMUNICATION_SETUP_TIMEOUT` to `State.__init__` where `-2` uses the sequence timeout 34 times out of 35. `State.__init__` only stores it and logs *"Waiting for up to {timeout} s"*, so the wait is unaffected and the log is wrong — cosmetic, and filed as cosmetic beside the row above. **Nearly filed as the headline**, which is why the audit note spends a section on following the value to `asyncio.wait_for` rather than to the name that sounds right |
| **Checked and found correct — their SECC enforces `[V2G2-460]`/`[V2G20-460]`** | `secc/states/secc_state.py:270-282` refuses any request that is neither a `SessionSetupReq` (all three protocols) nor a `SupportedAppProtocolReq` when `message.header.session_id != comm_session.session_id`, with `FAILED_UNKNOWN_SESSION`. **No zero exemption** — which is exactly the conjunct that makes EVerest's `-2` station serve an all-zero SessionID ([filing](reports/everest-evsev2g-session-id-zero.md)). A ruled-out class, recorded because the same probe was pointed here next |
| **Checked and found correct — a malformed contract cert is caught, not fatal** | `iso15118_2_states.py:944` wraps the whole `PaymentDetails.process_message` body in one `try:`, and the leaf certificate is parsed inside `verify_certs`, whose failure is a caught exception → a `FAILED` response. So the same non-empty-but-unparseable `ContractSignatureCertChain.Certificate` that [crashes EVerest's `EvseV2G`](reports/everest-evsev2g-paymentdetails-crash.md) — used before its parse result is checked — is answered here. Recorded because the three-stack contrast is what makes that a filable crash rather than a shrug: Josev catches, ours catches, only EVerest reaches OpenSSL with a null |
| **Checked and found correct — an unimplemented `-2` message still gets a lawful answer** | `secc/failed_responses.py:488-495` carries a prepared `CertificateUpdateRes(response_code=FAILED, …)` with every mandatory element filled by a schema-conformant placeholder, and `-2` `CertificateUpdate` is implemented nowhere else in their SECC. That is `[V2G2-558]` and `[V2G2-736]` in nine lines from a stack with no intention of renewing contracts. Recorded because it is what decides the shape of [the EVerest filing](reports/everest-evsev2g-certificate-update.md) beside it — *answer the way you already answer everything you cannot do*, a one-function fix rather than a feature request |
| **Checked, and it is half a feature rather than a defect — the `-2` metering receipt** | `iso15118_2_states.py:1962-1979` **verifies** the `MeteringReceiptReq` signature and stops the session with *"Unable to verify signature of MeteringReceiptReq"* when it fails — the half EVerest's `EvseV2G` does not do. But `meter_info=…get_meter_info_v2()` is **commented out** at both `-2` call sites (`:2147`, `:2494`), so their station sends no `MeterInfo` and nothing sets `ReceiptRequired`: they verify a receipt for a record they never send. **Deliberately not filed** — an unimplemented option is not a defect, which is the distinction [`reports/README.md`](reports/README.md)'s *What is deliberately not here* exists for. Recorded because the [three-stack table](interop-runs/2026-08-11-everest-iso2-metering-receipt/notes.md) needs it: each of the three implements a different half, and the [EVerest filing](reports/everest-evsev2g-metering-chain.md) says so |
| **Checked and found correct — their random values are full-width and cryptographically generated** | `shared/security.py:95-100` — `get_random_bytes(n)` is `secrets.token_bytes(n)`, the CSPRNG. Used for the SessionID at full 8 bytes in all three protocols (`iso15118_2_states.py:183`, `iso15118_20_states.py:151`, `din_spec_states.py:107`) and for the 16-byte `GenChallenge` in `-2` and `-20` (`:1037`, `:262`). So `[V2G2-835]`/`[V2G20-835]` (a CSPRNG *shall*), `[V2G20-2621]` (SessionID ≥ 58 bits) and `[V2G2-698]`/`[V2G20-698]` (challenge ≥ 120 bits) are all met, one call doing the work of all three. A ruled-out class, recorded because the same audit found **two** of the other three `-20` stacks short — EVerest's `-20` library at ≤ 32 bits ([filing](reports/everest-d20-rng-entropy.md)) and eVDriveFlow at 26,6 ([filing](reports/evdriveflow-session-id-entropy.md)) |
| ~~Their SECC verifies our signed `AuthorizationReq` without checking the contract chain~~ **— ruled out 2026-08-10** | It is not a finding, and it looked like a serious one. Their log carries `WARNING - shared.security (999): Sub-CA and root CA certificates were not used to verify signatures along the certificate chain` immediately before *"Signature verified successfully"* and an `AuthorizationRes: OK` ([`…-iso2-pnc-tls`](interop-runs/2026-07-22-iso2-pnc-tls/josev-secc-iso2-pnc.log):108). `verify_signature`'s own docstring documents the skip — step 3 *"can be skipped if the contract certificate chain from leaf to root was already checked when receiving the `PaymentDetailsReq`"* — and the same log shows exactly that happening 70 ms earlier: leaf and both sub-CAs printed with subject, issuer, serial and validity, then *"Using MO root at …/moRootCACert.der"*. The warning fires at the call site that passes no roots, not at a station that checked none |

The renegotiation row deserves one more sentence, because the finding was nearly **ours**: the branch that
would keep the session alive is gated on `ServiceRenegotiationSupported` — a flag *our* SECC puts into
`ServiceDiscoveryRes`. Advertise `false` there and their `Terminate` is the correct answer, and the defect
is on this side of the wire. `Secc20Base.cs:590` sends `true`, so it is not.

Worth naming why this column came through a source audit unchanged while EVerest's did not: every claim
here is about **absent** code — a TODO, an unimplemented state, a field never written, two statements in
the wrong order. Absence is hard to mistake for intent. The two claims that had to be corrected on the
[EVerest page](everest-cross-validation.md) were both about code that is present, deliberate and
commented — and on the wire, a deliberate narrowing looks exactly like a missing capability.
