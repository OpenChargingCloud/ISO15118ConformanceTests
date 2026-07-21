# Interop run — ISO 15118-20 DC, **live Plug & Charge over TLS**: Josev EVCC → our SECC

- **Date:** 2026-07-21
- **Our side:** `Vanaheimr.V2G.Simulation.Cli` (`secc … --protocol 20 --mode dc --tls-backend dotnet
  --server-cert secc.p12 --server-cert-pass 12345 --require-client-cert`), .NET 10 under WSL. Our SECC now
  offers **both EIM and Plug & Charge** in `AuthorizationSetupRes` and, for a PnC EV, validates the signed
  `AuthorizationReq` (`Secc20Base.VerifyPnc`).
- **Josev:** SwitchEV/iso15118 @ `d645255`, EVCC only, host-mode, `-20 DC`, `ENABLE_TLS_1_3=True`, `useTls`.
  Because our SECC advertises PnC, Josev's EVCC selects **PnC** and signs the AuthorizationReq with its
  contract certificate (it falls back to EIM only if it can't sign).
- **Transport:** mutual **TLS 1.3** (same P-256 setup as the reverse TLS run).
- **Outcome:** ✅ the **full PnC authorization flow runs live** and the session completes end to end to
  SessionStop ("✓ Session complete in 36551 ms"). Our SECC's verdict on Josev's real signed AuthorizationReq:

  | Check | Result | Meaning |
  |-------|--------|---------|
  | **GenChallenge echo** | ✅ OK | Josev echoed the 16-byte challenge we issued in AuthorizationSetupRes |
  | **Reference digest** | ✅ OK | our `EncodeFragment_PnC_AReqAuthorizationMode` is **byte-identical to Josev/EXIficient** — SHA-256 of our re-encoded fragment matched Josev's `DigestValue` |
  | **ECDSA signature** | ⚠️ differs (root-caused) | our `SignedInfo` fragment uses the combined CommonMessages grammar; Josev signs over the standalone xmldsig grammar (209 B). Reproduced exactly + Josev's signature verified against Josev's octets — see below |

  Contract certificate presented: `CN=UKSWI123456791A, O=Switch, DC=MO` (P-256), with the MO Sub-CA 1/2 chain.
  Signature method: **`ecdsa-sha256`** (P-256 — Josev's PKI is P-256, not the -20-nominal secp521r1, matching
  the forward TLS run's finding). Full logs: [`josev-evcc-pnc-session.log`](josev-evcc-pnc-session.log),
  [`our-secc-pnc.log`](our-secc-pnc.log).

## What this validates

- The **EIM → PnC state-machine path** over the wire: our SECC offers PnC + a `GenChallenge`, Josev signs, our
  SECC decodes the ~2 KB signed `AuthorizationReq` (contract leaf + MO Sub-CAs + XMLDSig header) and completes
  the session.
- The **reference-digest match is the strong codec result**: it proves our canonical-EXI encoding of the
  signed element (`PnC_AReqAuthorizationMode`, including the contract chain) is byte-exact against an
  independent EXIficient encoder over a *live* message — the highest-value conformance signal short of a full
  signature verify.

## SignedInfo signature verification — **root-caused and reproduced** (not a codec bug)

The ECDSA signature over `SignedInfo` failed to verify even though the reference digest matched. The original
"our fragment canonicalization is wrong" hypothesis is **refuted**, and — updating the earlier "non-reproducible
form" conclusion — Josev's exact signing octets have now been **reproduced with Josev's own codec** and its
captured signature **verifies** against them
(`JosevPnCSignatureDiag.JosevSignsSignedInfoOverStandaloneXmldsigGrammar`).

Facts:

- The crypto is sound — P-256 contract leaf (`CN=UKSWI123456791A`), 64-byte r‖s signature, `ecdsa-sha256`.
- Our codec is **byte-exact** for the signed *element*: the reference digest = `SHA-256(our re-encoded
  PnC_AReqAuthorizationMode fragment)` equals Josev's `DigestValue` byte-for-byte. (Both sides encode the
  *element* under the CommonMessages grammar.)
- The **`SignedInfo` grammar differs**. Josev encodes the `SignedInfo` via
  `EXI().to_exi(signed_info, Namespace.XML_DSIG)`; inside Josev's `EXICodec.jar` the XMLDSig namespace
  (`http://www.w3.org/2000/09/xmldsig#`) maps to `BuiltInSchema.XSDCore` → the pre-generated
  `XMLDSIG_Core_Schema_Grammar`, a grammar built from **`xmldsig-core-schema.xsd` standalone**. We — like
  cbV2G, our authoritative reference — encode the `SignedInfo` as a fragment of the full
  `V2G_CI_CommonMessages` schema set (which `<xs:import>`s xmldsig alongside dozens of V2G global elements).
- Because the EXI *Fragment* grammar's leading element event-code width is set by the number of global elements
  in the loaded schema, the standalone-xmldsig grammar gives `SignedInfo` a **one-bit-narrower** top-level code.
  That shifts the whole bitstream: Josev's form is **209 bytes**, ours/cbV2G's is **210 bytes**, and they differ
  from byte 1 onward — even though both decode to the identical `SignedInfo`.

### How it was reproduced

Josev's own codec was driven directly (no reverse-engineering) to emit the signing octets:

```sh
# in the iso15118-secc container (has py4j + JVM + EXICodec.jar)
docker run --rm --entrypoint /venv/bin/python -v probe.py:/tmp/probe.py:ro iso15118-secc:latest /tmp/probe.py
# probe.py builds the captured SignedInfo and prints:
#   EXI().to_exi(SignedInfo(...canonical-exi / ecdsa-sha256 / #id1 / <captured DigestValue>...), Namespace.XML_DSIG)
# → 209-byte hex; Josev's captured 64-byte signature verifies (SHA-256) against it.
```

The 209-byte octets are checked in as `JosevPnCSignatureDiag.JosevStandaloneXmldsigSignedInfoHex`, so the
verification runs in CI with **no Java** (same convention as the EXIficient cross-check).

### Consequence

This is a Josev-specific grammar choice, not a defect in our byte-exact (cbV2G-matched) codec, which per the
project ground rule stays as-is. To *verify* Josev's PnC signatures live, our SECC would additionally need to
re-encode the `SignedInfo` under a standalone-xmldsig grammar (a codec our generator does not currently emit) —
a self-contained interop follow-up, now that the target byte form is known exactly. Note also
`tools/exificient-ref` encoding the same `SignedInfo` over the standalone xmldsig xsd via EXIficient's *runtime*
`XSDGrammarsBuilder` yields **244 B** — close but not identical to Josev's **209 B** pre-generated grammar, so a
faithful reproduction must use Josev's grammar, not just its schema.

## Reproduce

1. Build the CLI; start our SECC with `--server-cert secc.p12 --require-client-cert` (it offers PnC by default)
   + the TLS SDP responder.
2. Run Josev's EVCC host-mode with `ENABLE_TLS_1_3=True` and a config with `useTls=true`; its PKI already
   carries a contract cert, so it selects PnC automatically.
3. Our SECC prints the `Plug & Charge:` verdict line after the session.
