# Interop run — ISO 15118-20 DC, **live Plug & Charge over TLS with signature VERIFIED**: Josev EVCC → our SECC

- **Date:** 2026-07-22
- **Our side:** `Vanaheimr.V2G.Simulation.Cli secc --listen 55000 --protocol 20 --mode dc --tls-backend dotnet
  --server-cert secc.p12 --server-cert-pass 12345 --require-client-cert`, .NET 10 under WSL.
- **Josev:** SwitchEV/iso15118 EVCC, docker host-mode, `-20 DC`, `ENABLE_TLS_1_3=True`, `useTls`, PnC
  auto-selected (our SECC advertises PnC + a GenChallenge).
- **Transport:** mutual **TLS 1.3** (P-256 PKI), same setup as the 2026-07-21 reverse-TLS/PnC runs.
- **Outcome:** ✅ the full PnC flow runs and — unlike the [2026-07-21 run](../2026-07-21-iso20-dc-pnc-tls/) which
  reported `signature FAIL` — our SECC now **verifies Josev's SignedInfo signature**:

  ```
  Plug & Charge: contract DC=MO, C=UK, O=Switch, CN=UKSWI123456791A;
    challenge OK, digest OK, signature OK
    (http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256, grammar=xmldsig-standalone).
  ✓ Session complete in 23422 ms.
  ```

  The `grammar=xmldsig-standalone` tag confirms the signature verified via the **interop fallback path**: our
  SECC first tried its own combined `V2G_CI_CommonMessages` grammar (no match — that is what our own EVCC
  signs), then re-encoded the `SignedInfo` under the **standalone `xmldsig-core-schema.xsd` grammar** (the form
  Josev signs, root-caused in `docs/interop-runs/2026-07-21-iso20-dc-pnc-tls/`) and verified against that.

## What this closes

The last open item from the 2026-07-21 PnC run — "the ECDSA signature over `SignedInfo` does not verify" — is
now **fully resolved, live**. Our SECC accepts a real Josev EVCC's Plug & Charge authorization: challenge echo,
reference digest, **and** the ECDSA signature all verify, and the session completes the full DC charge loop
(SOC 10→100%) to `SessionStop`.

- Verify-only: our generator reproduces Josev's exact 209-byte standalone-xmldsig `SignedInfo`
  (`WWCP_ISO15118_XMLDSig` project; `XmlDsigStandaloneGrammarReproducesJosev`), and
  `XmlDsigInteropVerify` re-encodes + verifies. We never *sign* this form — our signing stays cbV2G-byte-exact.

## Reproduce

1. `tools/interop-josev/reverse-pnc-tls.sh`-style orchestration (see the scratch script used for this run):
   build the CLI, `secc.p12` from Josev's SECC leaf+key+CPO Sub-CAs, start our SECC + the TLS SDP responder,
   launch Josev's EVCC container (host mode, `ENABLE_TLS_1_3=True`, `useTls` config).
2. Our SECC prints the `Plug & Charge:` verdict; expect `signature OK … grammar=xmldsig-standalone`.

Full logs: [`our-secc-pnc-verified.log`](our-secc-pnc-verified.log),
[`josev-evcc-pnc-session.log`](josev-evcc-pnc-session.log).
