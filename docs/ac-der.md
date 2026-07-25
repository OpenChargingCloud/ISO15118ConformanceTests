# ISO 15118-20 Amendment 1 — AC DER (distributed energy resources)

Date: **2026-07-25**. Status: **codec implemented and self-consistent; not wired into the session
layer, no external byte oracle yet.**

## What AC DER actually is

Not a new message set — that was the initial assumption and it was wrong. The two amendment
schemas (`V2G_CI_AC_DER_IEC.xsd`, `V2G_CI_AC_DER_SAE.xsd`, free from
`https://standards.iso.org/iso/15118/-20/ed-1/en/Amd/1/AMD1_xsdSchema.zip`) both:

- `xs:import` the **base AC schema** (`urn:iso:std:iso:15118:-20:AC`),
- leave the message roots (`AC_ChargeParameterDiscoveryReq/Res`, `AC_ChargeLoopReq/Res`)
  **commented out** — you use AC's,
- and contribute **six substitution-group members** that extend AC's own types via `xs:extension`:

  | New member | Substitutes | Extends |
  |---|---|---|
  | `DER_AC_CPDReqEnergyTransferMode` | `AC_CPDReqEnergyTransferMode` | its `…Type` |
  | `DER_AC_CPDResEnergyTransferMode` | `AC_CPDResEnergyTransferMode` | its `…Type` |
  | `DER_Scheduled_AC_CLReqControlMode` | `Scheduled_AC_CLReqControlMode` | its `…Type` |
  | `DER_Scheduled_AC_CLResControlMode` | `Scheduled_AC_CLResControlMode` | its `…Type` |
  | `DER_Dynamic_AC_CLReqControlMode` | `Dynamic_AC_CLReqControlMode` | its `…Type` |
  | `DER_Dynamic_AC_CLResControlMode` | `Dynamic_AC_CLResControlMode` | its `…Type` |

This is structurally identical to the `BPT_*` variants the base AC schema already defines, which is
why **the source generator needed no changes at all** — it already handled cross-schema
substitution groups and `xs:complexContent/xs:extension`, and modelled the DER types as C# records
deriving from their AC base records.

The same pattern shows up in MCS (which extends DC the same way, via service IDs and parameter
sets). Amendment 1 extends rather than adds.

## Two assemblies, because the grammar differs

`Vanaheimr.V2G.Exi.Iso15118_20.AC_DER_IEC` and `…_SAE` each compile
`V2G_CI_AC_DER_{IEC,SAE}.xsd` **together with** `V2G_CI_AC.xsd` + `CommonTypes` + `xmldsig`. They are
separate from the plain AC assembly because the generator inlines substitution-group members as
grammar productions, so the grammar for the AC messages is not the same one.

IEC is the smaller flavour (166 elements); SAE (364) adds apparent-power/excitation limits, inverter
details and IEEE 1547 categories.

## The measured surprise: plain AC traffic is byte-identical

The expectation was that adding members to a substitution group widens the event code and changes
the bytes for *every* message. **Measured, it does not** — a plain (non-DER)
`AC_ChargeParameterDiscoveryReq` encodes byte-for-byte identically under the plain AC grammar and
under AC+DER:

```
plain AC : 8010040000000000000000080E2CFAA0620800F85522001908
AC + DER : 8010040000000000000000080E2CFAA0620800F85522001908
```

The DER member is appended after the existing members and the production count at that choice point
stays inside the same n-bit code width, so the existing members keep their codes.

Consequences:

1. The grammars are **backward compatible for messages that don't use a DER member** — a DER-capable
   receiver reads plain AC traffic unchanged.
2. Compatibility ends the moment a DER member is used: that selects a production the plain AC grammar
   does not have.
3. This is **fragile in principle** — a future amendment adding more members to the same group could
   push the width over a power-of-two boundary and silently change the bytes for everyone.
   `PlainAcMessage_IsByteIdenticalUnderBothGrammars` exists to catch exactly that.

## What is deliberately *not* done

- **No V2GTP dispatch, no SAP negotiation.** The payload type and `ProtocolNamespace` that select a
  DER session live in the amendment *text*, which we do not have. Since the messages are AC messages,
  reusing AC's `0x8003` with a distinct negotiated namespace is plausible — but that is a guess, and
  the project ground rule forbids inventing wire semantics. `V2GTPDispatcher` is untouched.
- **No external byte oracle.** cbexigen does not generate the amendment schemas, so the checked-in
  assertions are self-consistency (encode → decode → re-encode) plus the cross-grammar comparison —
  never "these are the right bytes". EXIficient is schema-generic and remains the candidate spec
  oracle; feeding the amendment schemas to cbexigen to generate a C reference codec is the other
  option, and would restore the primary-oracle relationship.
- **SAE's `DER_*` members are not constructed in tests.** They require four mandatory limit
  structures (apparent power, reactive power, excitation, inverter details — 12–24 fields each) plus
  IEEE 1547 categories. Worth building once there is an oracle to check them against; until then the
  SAE assembly is covered only for the messages it shares with plain AC.

## Where this lives

- `Vanaheimr.V2G.Exi.Iso15118_20.AC_DER_IEC/`, `…_SAE/` — the two grammar variants (+ their schemas)
- `Vanaheimr.V2G.Exi.Tests/Iso15118_20AcDerTests.cs` — five tests: DER roundtrip, plain-AC roundtrip
  staying distinct from DER, SAE roundtrip, the byte-identity measurement, and the
  plain-codec-vs-DER-message boundary
