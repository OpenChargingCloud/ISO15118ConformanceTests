# 2026-11-27 — EVerest 2026.10.0: the still-present findings, reproduced on the wire

**What this run is:** the live half of the [release sweep](../2026-11-26-everest-2026.10.0-release/notes.md).
That sweep found twelve findings still **present in the source** of the `2026.10.0` tag, and five more by
hand. It also said what it had not done: *"the live run did not drive the thirteen still-present findings
to reproduce them, which would be a session each against the release."* This run is those sessions. Every
arm ran against `~/everest/dist-2610` (`74845c578`, 22 modules), one manager start per arm.

**The headline:** **every finding that was driven reproduces on the release**, twelve of them, plus the
two `isomux` sections already on tape from the [forward run](../2026-11-26-everest-2026.10.0-forward/notes.md).
Two of them reproduce **with different numbers**, and the reports have to say so:

- `d20-eim-rejection`: the rejection still never reaches the `-20` stack, but the release ends the
  `Ongoing` loop at **55 s**, not 180 s. `TIMEOUT_EIM_ONGOING` (3 min) became `TIMEOUT_ONGOING`
  (55 s, commented `[V2G20-2102]`). It is still 37 times the 1,5 s of `[V2G20-2230]`.
- `evsemanager-renegotiation-supply`: the renegotiated `CableCheck` still fails and still raises
  `MREC11CableCheckFault` and `Inoperative`. But the release **clears both at the unplug**, and the next
  session in the same process is served. *"It stops serving the next one"*, the sentence the report
  leads with, is no longer true of 2026.10.0.

One side observation is **ours**, not theirs: with `ev_receipt_required` on, their station asks for a
receipt in a `CurrentDemandRes` that carries no `MeterInfo`. Our car then skipped the receipt, which
`[V2G2-790]` does not allow. **Fixed the same evening.** With the fix, and with a new knob that signs a
receipt over the wrong bytes, the receipt half of `evsev2g-metering-chain` went on the wire too: three
tampered receipts, all answered `OK` ([below](#after-the-fix-the-receipt-arms-again)).

| | |
|---|---|
| Counterparty | EVerest **2026.10.0** (`74845c578`), `~/everest/dist-2610`, built 2026-11-26 |
| Their stations | `Evse15118D20` (`-20`, plain and TLS), `EvseV2G` (`-2`, plain and TLS 1.2), `IsoMux` |
| Ours | WWCP_ISO15118 as in `~/i15118` (built 2026-11-27 15:44), `EverestInteropTests.OurEvcc`, and `openssl s_client` (OpenSSL 3.5) for the TLS arms |
| Driven by | [`scripts/tls-arms-2610.sh`](scripts/tls-arms-2610.sh) (handshakes, via [`station-2610.sh`](scripts/station-2610.sh)) and [`scripts/evcc-arms-2610.sh`](scripts/evcc-arms-2610.sh) (sessions, via the forward harness [`fwd-2610.sh`](scripts/fwd-2610.sh) / [`fwd-2610v.sh`](scripts/fwd-2610v.sh)) |
| Configs | the forward run's `*-fwd2610.yaml`, plus three derived here: [`config-d20-tls-default-2610.yaml`](configs/config-d20-tls-default-2610.yaml) (`enforce_tls_1_3` at its default), [`config-d20-eim-reject-2610.yaml`](configs/config-d20-eim-reject-2610.yaml) (`DummyTokenValidator` answers `Invalid`), [`config-dc2-pnc-receipt-2610.yaml`](configs/config-dc2-pnc-receipt-2610.yaml) (`ev_receipt_required: true`) |
| Artifacts | [`tls/`](tls/) (openssl transcripts and their charger lines), [`sessions/`](sessions/) (our flows, frames and logs, their logs), [`isomux/`](isomux/) (the forward run's lines) |

## The result, finding by finding

| finding | arm | on 2026.10.0 | as reported? |
|---|---|---|---|
| `d20-client-auth` §1 | `ca12` | TLS 1.2 only, no client certificate: handshake complete, **no `CertificateRequest`** | ✅ same |
| `d20-client-auth` §1, consequence | `ca12-replay` | then `supportedAppProtocolReq(-20:DC)` → **`80400040`** (`OK_SuccessfulNegotiation`) and a `SessionSetupRes`; their log: *V2G session established* | ✅ same |
| `d20-client-auth` §2 | `ca13` | TLS 1.3: `CertificateRequest` of 62 bytes, **no CA names**; signature algorithms are OpenSSL's defaults (ML-DSA, Ed25519/448, brainpool, RSA-PSS); group **`X25519MLKEM768`** | ✅ same, the profile August measured |
| `d20-trust-anchor` | `ta-oem`, `ta-mo`, `ta-vehicle` | OEM-rooted client chain: **`certificate verify failed`**. MO (contract) chain: `Handshake complete!`, **`Vehicle Cert is available`**. Their own vehicle chain (control): accepted | ✅ same |
| `isomux` §1 | forward `fix-mux-both-2first` | `-2` at Priority 1, `-20` at Priority 2: **routed to `-20`** | ✅ same |
| `isomux` §2 | forward `fix-mux-tls-both`, `mux-tls-iso20` | `-20` over **TLS 1.2** to `SessionStop`; a `-20`-only offer needing TLS 1.3 gets `alert protocol version` | ✅ same |
| `d20-meter-info` | `meter-ctl`, `meter` | our request changes by one bit (`…8062 81…` → `…8062 a1…`); their responses have the same lengths (55/57 B) in both arms, **never a `MeterInfo`** | ✅ same |
| `d20-sequence-timeout` | `seqto-dc`, `seqto-ac` | EV silent after one loop: **59,992 s** (DC) and **60,003 s** (AC) to their stop; their log still says *"Sequence Timeout 40secs"* | ✅ same |
| `d20-eim-rejection` | `eim-reject` | `Auth`: `REJECTED`; the station answers `AuthorizationRes = OK, Ongoing` **≈550 times, then `FAILED` after 54,9 s** | ⚠️ the defect, **with a shorter tail** (55 s, not 180 s) |
| `d20-ac-contactor-edge` | `contactor-edge` | CP at state C from the plug-in: `Event PowerOn` at 48,37 s, `PowerDeliveryReq` at 51,67 s, then *"AC contactor is not closed within 1400ms"* → **`FAILED_ContactorError`** | ✅ same (the window is 1400 ms now) |
| `evsev2g-session-id-zero` | `sid-zero`, `sid-wrong` | SessionID of eight zero bytes on every request after `SessionSetup`: **44 exchanges to `SessionStop`, all `OK`**. Control, a wrong non-zero id: `FAILED_UnknownSession` at the first request | ✅ same, and stronger: a whole session, not one request |
| `evsev2g-certificate-update` | `certupd` | `CertificateUpdateReq` under parameter set 1: **`CertificateUpdateRes = OK`**, and a `DHpublickey` that is not a P-256 point | ✅ same |
| `evsev2g-metering-chain`, going out | forward `dc2`, `receipt` | `MeterInfo` carries `MeterID` and `MeterReading`, **nothing else** — decoded by V2Gdecoder (RISE-V2G + EXIficient), not by us | ✅ same |
| `evsev2g-metering-chain`, coming back | `receipt-tamper` | three receipts whose digest does not match their body, **all answered `OK`**, session to `SessionStop` | ✅ on the wire for the first time ([below](#after-the-fix-the-receipt-arms-again)) |
| `evsemanager-renegotiation-supply` | `reneg-fwd`, `reneg` | renegotiated `CableCheck` → `FAILED`, `MREC11CableCheckFault`, `Inoperative`, *Initiating error shutdown* — then **both errors cleared at the unplug**, and the second session is served | ⚠️ the failure, **not** the lasting `Inoperative` |

`manager alive at end: 1` on every arm.

## Two that reproduce with different numbers

### `d20-eim-rejection`: the tail is 55 s now

The defect is unchanged. `EvseManager/evse/evse_managerImpl.cpp:466` is still `} else if (pnc) {` with the
comment *"we only send authorization responses to the HLC for PnC rejections"*, so an EIM `REJECTED`
never reaches `Evse15118D20`:

```
19:06:19.647  auth:Auth      Result for token: [redacted] hash: CE55F71752B68164: REJECTED
19:06:25.300  EVSE ISO V2G AuthorizationRes          ← first of ≈550, all OK / Ongoing
19:07:20.214  EVSE ISO V2G AuthorizationRes          ← FAILED
```

What changed is how long the station keeps the car waiting. In 2026.02.1 `d20/timeout.hpp` had
`TIMEOUT_EIM_ONGOING = 1000 * 60 * 3`. In 2026.10.0 it has `TIMEOUT_ONGOING = 1000 * 55`, armed in
`d20/state/authorization.cpp:282` under the comment *"[V2G20-2102]: EIM has no longer window than PnC"*.
So the August numbers, *1 800 times, then `FAILED` at ≈180 s*, are a 2026.02 statement. For the
release they are **≈550 times, `FAILED` at 54,9 s**. The ask is the same, and the distance to
`[V2G20-2230]`'s 1,5 s is a factor of 37 instead of 120. Our car waited with `V2G_INTEROP_ONGOING=240`,
so the 55 s is theirs, not ours.

### `evsemanager-renegotiation-supply`: still fails, no longer sticks

Both arms (`reneg-fwd` with the plain forward harness, `reneg` with the dlink-aware one) renegotiate
after the first `CurrentDemand` and fail the fresh `CableCheck`:

```
evse_manager  Cancel cable check while waiting for the voltage to drop below 60.00 V
evse_manager  no measurement received from the power supply at all within 10 s, so it is unknown
              whether the voltage did drop below 60 V, sending CableCheck Finished(false) anyway
evse_manager  Error raised, type: evse_manager/MREC11CableCheckFault … Charger did not enter PrepareCharging state.
evse_manager  Error raised, type: evse_manager/Inoperative
evse_manager  Initiating error shutdown
```

The wording is new: 2026.02.1 said *"Voltage did not drop below 60V within timeout"*. The new line
claims ten seconds without a measurement, and it fires 44 ms after the cancel. Then, at the unplug:

```
19:15:19.555  SDP dlink_ready set to false
19:15:23.640  Cleared error of type evse_manager/MREC11CableCheckFault with sub_type Self test failed
19:15:23.640  Cleared error of type evse_manager/Inoperative
19:15:31.151  SDP dlink_ready set to true
```

and session 2 in the same manager runs `CableCheck`, `PreCharge`, `PowerDelivery` and `CurrentDemand`, all
`OK`, before its own renegotiation fails the same way. The report leads with *"The station does not
merely fail the session — it goes `Inoperative` and stops serving the next one"*. On 2026.10.0 the
first half holds and the second does not. The `reneg-fwd` arm's session 2 got no endpoint, but that was
the harness: it probed SDP during the seconds of `dlink not ready` (*"SDP request discarded"* ×3).

## The receipt arm: one thing of ours

With `ev_receipt_required: true` in `EvseManager`, the `-2` PnC session over TLS 1.2 ran like this
([decoded](sessions/receipt.decoded.txt)):

| # | ours | theirs |
|---|---|---|
| 40 | `CurrentDemandReq` | `CurrentDemandRes`, `MeterInfo` (`DC_POWERMETER`, 0), **`ReceiptRequired = true`** |
| 41 | `MeteringReceiptReq`, signed | `MeteringReceiptRes = OK` |
| 42 | `CurrentDemandReq` | `MeterInfo` (…, 1), `ReceiptRequired = true` |
| 43 | `MeteringReceiptReq`, signed | `OK` |
| 44 | `CurrentDemandReq` | **no `MeterInfo`**, `ReceiptRequired = true` |
| 45 | `PowerDeliveryReq(Stop)` | **`FAILED_SequenceError`** |

At [44] their station asks for a receipt and sends nothing new to sign; their `meter_info_is_used` flag
is one-shot, so a `MeterInfo` goes out only after the meter has published again. Our car signs a receipt
only over a `MeterInfo` it was just given (`Evcc2.cs:467`). So it skipped the receipt and went to
`PowerDelivery`. The station had moved to *wait for `MeteringReceiptReq`* and refused.

`[V2G2-790]` settles whose step that was: after a `CurrentDemandRes` with `ReceiptRequired = TRUE`, the
EVCC **shall** send a `MeteringReceiptReq`. It sets no condition on `MeterInfo` being present. The car can
meet it by signing the last `MeterInfo` it received. That is **ours to fix** (`ChargingStatusRes` has the
same branch at `Evcc2.cs:481`). That the station asks for a receipt with nothing to sign is odd, but the
text read here does not forbid it, so it is not filed.

This arm could not decide the other half of `evsev2g-metering-chain`, *the receipt's signature is never
checked*. Both of our receipts were validly signed, so `MeteringReceiptRes = OK` is what a verifying
station would have answered too. The next section decides it.

## After the fix: the receipt arms again

The car was fixed the same evening, on stack branch `evcc2-receipt-last-meterinfo` (`8e6fd3e`).
`Evcc2` now keeps the last `MeterInfo` of the session and signs that when the response asking for a
receipt carries none. It aborts with a stated reason when it cannot comply: no `MeterInfo` yet, or
`ReceiptRequired` under EIM. The same commit adds the missing instrument, `Evcc2.TamperMeteringReceipt`
(interop: `V2G_INTEROP_RECEIPT_TAMPER=1`): a receipt whose signature covers a `MeterReading` one higher
than the one it carries. Three new loopback tests cover both, AC and DC, and the tampered one shows our
own SECC's `[V2G2-904]` check failing the digest. The full offline suite passed with exit 0, 1 605 tests.

Re-run against the same release, the same config, two sessions per manager start, with
[`fwd-2610v.sh`](scripts/fwd-2610v.sh):

| arm | session 1 | session 2 |
|---|---|---|
| `receipt-fixed` | 3 receipts: [42] `MeterInfo` 0, [44] `MeterInfo` 1, **[46] no `MeterInfo` → our receipt over 1**, all `OK`; then `PowerDelivery OK`, `WeldingDetection`, **`SessionStop OK`** | no receipt asked for (below), `SessionStop OK` |
| `receipt-tamper` | 3 receipts **signed over other bytes than they carry**, **all `MeteringReceiptRes = OK`**, `SessionStop OK` | no receipt asked for, `SessionStop OK` |

Decoded with V2Gdecoder: [`receipt-fixed.decoded.txt`](sessions/receipt-fixed.decoded.txt) and
[`receipt-tamper.decoded.txt`](sessions/receipt-tamper.decoded.txt). The station's log for the tampered
receipts has nothing but the `CAR ISO V2G MeteringReceiptReq` / `EVSE ISO V2G MeteringReceiptRes` pairs.

- **`[V2G2-790]` is met**, and the session that ended in `FAILED_SequenceError` now completes.
- **The coming-back half of `evsev2g-metering-chain` is on the wire.** `[V2G2-904]` makes the check a
  *may*, so this is the report's weaker half, as it says itself. But the claim is now a measurement:
  a receipt the car did not sign as sent gets the same `OK` as one it did. That a tampered receipt
  really fails a verifier rests on the loopback test, where our own SECC verifies it. The live arm
  shows the release does not tell the two apart.

**A side observation about their station, not filed.** Session 2 of both arms was never asked for a
receipt. `EvseManager` hands `ev_receipt_required` to the HLC once, at module start
(`EvseManager.cpp:1096`, `call_receipt_is_required`), and `EvseV2G` clears it at the end of every
session: `v2g_ctx_init_charging_session` (`connection.cpp:500`) calls `v2g_ctx_init_charging_values`,
which sets `receipt_required = 0` (`v2g_ctx.cpp:329`; `:248` in 2026.02.1). So the configured receipt is
asked for in the first session after boot and in none after it. It is `EvseV2G`-only, and that module is
deprecated in this release. It stays a note unless someone wants a fifty-first filing.

## Smaller differences from August, none of them a change of verdict

- **The contactor window** at `PowerDelivery(Start)` is 1400 ms in the release (*"AC contactor is not
  closed within 1400ms"*, here **1,426 s** after *"Waiting for contactor is closed"*), where 2026.02.1
  gave up after 3,047 s. It is the same constant the forward run met at the stop. The edge is still
  missed (`PowerOn` came **3,262 s before** the wait began); only the wait before giving up changed.
- **The `IsoMux` TLS arm** logs *"Negotiated ISO 15118-20 … on an unsecured connection … [V2G20-2356]"*
  from `Evse15118D20`. The connection was TLS 1.2 at the mux. The backend only sees the mux's plaintext
  hop, so the release's own `-20`-over-TLS-1.2 check fires for the wrong reason, and the real reason
  (TLS 1.2) is not checked anywhere ([`isomux/fix-mux-tls-both.txt`](isomux/fix-mux-tls-both.txt)).

## Two mistakes of ours on the way, both corrected in the run

- **`replay.consequence.hex` again.** The first `ca12-replay` fed the 2026-08-10 file to the station and
  got *"Expected SupportedAppProtocolReq! But code type id: 2"*. That file holds the station's
  **responses**, as its own run note warns in a blockquote. The re-run takes the request frames from
  `libs/EVSimulatorApp/vectors/Session.iso20-dc-eim.trace.json` and keeps them as
  [`tls/ca12-replay.sent.hex`](tls/ca12-replay.sent.hex), so the bytes sent are on file this time.
- **`certupd` without an eMAID** stopped in our own car (*"CertificateUpdateReq: the eMAID of the expiring
  contract is required"*). Re-run with `V2G_INTEROP_PROVISION_EMAID=UKSWI123456791A`, the CN of the
  release's MO leaf. The August run had used `…789A`, the CN of 2026.02.1's.

## What this did not do

- **`isomux` §4** (the `trusted_ca_keys` boot line) was not re-run; it is not in the release sweep's
  still-present list either, which left it unread.
- **The outgoing `MeterSignature` in the C++ `d2/` path** and the two **libcbv2g** issues are source and
  codec findings. The codec files are byte-identical to `main`, so the 2026-11-06 probe carries over.
  Nothing here drove them.
- **`d20-ac-namespace`, `d20-rng-entropy`, `d20-ocsp-absent`** and the other findings the sweep marked
  fixed were not re-run to confirm the fix.

## Next

- ~~Our car: send the `[V2G2-790]` receipt over the last `MeterInfo`~~ and ~~the tamper knob~~: done
  the same evening (above). What is left is merging stack branch `evcc2-receipt-last-meterinfo` and
  the two pointer bumps.
- Update `everest-d20-eim-rejection` (55 s) and `everest-evsemanager-renegotiation-supply` (clears at
  the unplug) before either is sent. Done in this change; see the reports' banners.
