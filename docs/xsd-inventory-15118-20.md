# XSD inventory ISO 15118-20 (Phase 4)

## Source

The eight -20 schemas were downloaded from `https://standards.iso.org/iso/15118/-20/ed-1/en/` —
the same freely accessible ISO source the already-checked-in -2 schemas came from
(see the header comment in `WWCP_ISO15118_2/Schemas/V2G_CI_MsgDef.xsd`;
cbexigen's `tools_config.py` uses exactly the same URL structure for its
`--auto-download-public-xsd` mode). Downloaded on 2026-07-10:

`V2G_CI_AC.xsd`, `V2G_CI_ACDP.xsd`, `V2G_CI_AppProtocol.xsd`, `V2G_CI_CommonMessages.xsd`,
`V2G_CI_CommonTypes.xsd`, `V2G_CI_DC.xsd`, `V2G_CI_WPT.xsd`, `xmldsig-core-schema.xsd`.

Reference encoder is still cbV2G@03350be048b3 (`lib/cbv2g/iso_20/*`, in the same checkout as
already used for -2 — libcbv2g covers -20 completely: CommonMessages/AC/DC/WPT/ACDP each
with its own `iso20_<Set>_{Datatypes,Encoder,Decoder}.c`).

### Amendment 1 (added 2026-07-25)

Two further schemas live one directory deeper, in
`https://standards.iso.org/iso/15118/-20/ed-1/en/Amd/1/AMD1_xsdSchema.zip` (25 KB, free, no
paywall — worth re-checking that `Amd/` directory for future amendments, since cbexigen's
`--auto-download-public-xsd` only fetches the eight base files above):

`V2G_CI_AC_DER_IEC.xsd` (namespace `urn:iso:std:iso:15118:-20:AC-DER-IEC`, 166 elements) and
`V2G_CI_AC_DER_SAE.xsd` (`…:AC-DER-SAE`, 364) — **AC DER**, distributed-energy-resource grid
support.

They are **not a sixth and seventh message set**. Both import the base AC schema, leave their
message roots commented out, and contribute six `DER_*` **substitution-group members** extending
AC's own types via `xs:extension` — the same construct AC already uses for its `BPT_*` variants,
so the generator needed no changes. They are consumed by
`WWCP_ISO15118_20.AC_DER_{IEC,SAE}`, each compiling AC + DER + `CommonTypes` +
`xmldsig` into a *grammar variant of AC*.

**No cbV2G reference exists for these:** cbexigen crashes analysing them (a substitution-group
head fed by two schemas), so they are cross-validated against EXIficient instead. Full write-up
incl. reproduction: [`ac-der.md`](ac-der.md).

## Architectural difference from -2 (confirmed)

- **No `V2G_Message` wrapper.** Every message (`SessionSetupReq`, `AuthorizationReq`, …) is
  its own global element with its own type, which extends `V2GRequestType`/`V2GResponseType`
  (abstract, in `CommonTypes`). The header (`SessionID`, `TimeStamp`, optional
  `Signature`) sits directly in `V2GMessageType` (the base of `V2GRequestType`) — no separate
  body-substitution-group construct like -2's `BodyElement`.
- **Five independent schema sets** in the base edition (Amendment 1 adds no sixth — see above):
  CommonMessages, AC, DC, WPT, ACDP — each imports
  `CommonTypes` + `xmldsig-core-schema`. One generated assembly per set
  (`WWCP_ISO15118_20.CommonMessages/.AC/.DC/.WPT/.ACDP`), `CommonTypes` is
  deliberately duplicated per assembly (as cbV2G/cbexigen itself does). WPT and ACDP were
  originally explicitly out of scope, but were completed afterward on 2026-07-11 (see
  "WPT/ACDP — completed afterward" below for the new constructs found in the process).
- **`RationalNumberType`** (`CommonTypes`): `Exponent xs:byte, Value xs:short` — structurally
  identical to -2's `PhysicalValueType` minus the unit; only needs a simple
  `RationalNumber.Of/.ToDecimal` helper (no new codec feature).

## New construct #1 (central): `xs:choice` as the last particle of an `xs:sequence`

Occurs **nine times** in CommonMessages (`AuthorizationSetupResType`, `AuthorizationReqType`,
`ScheduleExchangeReqType`, `ScheduleExchangeResType` → `Dynamic_SEResControlModeType`,
`ChargingScheduleType`, `SignedInstallationDataType`, `SignedMeteringDataType`,
`EVPowerProfileType`) — by far the most common new building block. Example:

```xml
<xs:extension base="v2gci_ct:V2GResponseType">
  <xs:sequence>
    <xs:element name="AuthorizationServices" type="authorizationType" maxOccurs="2"/>
    <xs:element name="CertificateInstallationService" type="xs:boolean"/>
    <xs:choice>
      <xs:element name="EIM_ASResAuthorizationMode" type="EIM_ASResAuthorizationModeType"/>
      <xs:element name="PnC_ASResAuthorizationMode" type="PnC_ASResAuthorizationModeType"/>
    </xs:choice>
  </xs:sequence>
</xs:extension>
```

**Byte diff against cbV2G (`encode_iso20_AuthorizationSetupResType`, grammar state 272) shows
the wire semantics unambiguously:**

```c
struct iso20_AuthorizationSetupResType {
    ...
    struct iso20_EIM_ASResAuthorizationModeType EIM_ASResAuthorizationMode;
    unsigned int EIM_ASResAuthorizationMode_isUsed:1;
    struct iso20_PnC_ASResAuthorizationModeType PnC_ASResAuthorizationMode;
    unsigned int PnC_ASResAuthorizationMode_isUsed:1;
};
// state 272; 2 bits (ceil(log2(2+1)), phantom rule as before); state selects the branch
// directly (SE(EIM…)=0, SE(PnC…)=1), NO separate wrapper SE; then immediately element EE.
```

This is **not** the -2 substitution-group pattern (there: ONE polymorphic field, `is TypeX`
pattern matching). cbexigen models an inline `xs:choice` as **N independent, parallel
optional fields** (one `X_isUsed` bit per branch) — structurally identical to our already
existing **root**-level `xs:choice` path (`ParameterType`, `EmitEncodeChoice`: one field per
branch, `msg.Field is not null`), except that here the branches are additionally flattened
into the same optional-run/event-code space as the *preceding* sequence particles
(confirmed on `Dynamic_SEResControlModeType`: `DepartureTime?, MinimumSOC?, TargetSOC?,
choice(minOccurs=0){Absolute/PriceLevel}` — the choice branches share the event-code space
with the preceding optionals in exactly the shape our existing
`EmitEncodeOptionalRun` machine already supports for optional substitution references).

Two variants, both confirmed in practice:
1. **Required choice** (no `minOccurs="0"` on `<xs:choice>`): terminates the run like a
   required substitution reference (an already-supported terminator path) — just without
   an absence production.
2. **Optional choice** (`minOccurs="0"`): joins the run as an ordinary optional, including
   the EE alternative — confirmed on `ChargingScheduleType`
   (`PowerSchedule, choice(minOccurs=0){AbsolutePriceSchedule/PriceLevelSchedule}`) and
   `Dynamic_SEResControlModeType`.

At least one choice has **3 branches** with **mixed** value types (`SignedInstallationDataType`:
`SECP521_EncryptedPrivateKey`/`X448_EncryptedPrivateKey` are `base64Binary` **simple**
types, not a complexType!) — the event-code order is **document order**, not
alphabetical (confirmed: SECP521=0, X448=1, TPM=2 in schema order; alphabetical would be
SECP521, TPM, X448 — different, so this disambiguates it).

**Generator consequence (implemented):** a new `ValueEncoding` case `InlineChoice` (a list of
`InlineChoiceMember(ElementName, FieldName, CSharpType, ValueEncoding, IsCSharpNullable)`,
with no abstract head) instead of reusing `SubstitutionChoice` (which presupposes a
polymorphic single field). Each member becomes its **own** nullable field in the record —
with its **own natural** `PascalCase(ElementName)` field name (e.g. `EIM_ASResAuthorizationMode`
stays exactly as the field name); synthesizing a shared name is **not** needed, since
(unlike substitution groups) no shared C# base type has to exist. The wrapper `ChildPlan`
itself only carries an internal placeholder field name (never dereferenced). Content per
member goes through `EmitEncodeContent`/`EmitWriteValue` (covers simple AND complex branches
uniformly, as already established for root-level `xs:choice` (`ParameterType`)).
`ProductionCount` counts `Members.Count`; the optional-run machine
(`EmitEncodeOptionalRun`/`EmitDecodeOptionalRun`) needs no change — it already handles a
multi-member production per `ChildPlan` generically (cf. `ChildPlan.IsWildcardAny` from
Phase 3, which uses the same principle). A choice doesn't have to be the last sequence
particle (`EVPowerProfileType` has one followed by another required list) —
`ParseParticles` inserts the choice marker at its actual document position
(`ElementsBeforeSelf` counting), not blanketly at the end.

## New construct #1b: a required bounded-repeating list that isn't the last particle

`AuthorizationSetupResType.AuthorizationServices` (`maxOccurs="2"`, required) is **not**
referenced last — `CertificateInstallationService` and the EIM/PnC choice follow it.
cbV2G's grammar folds **only the immediately following particle** into the list's
"continue vs. continue with next" event codes (state 270: `{continue=0,
CertificateInstallationService=1}`, 2 bits; state 271, list at max: `{CertificateInstallationService=0}`,
1 bit, unconditional) — everything after that (here, the choice) is processed independently.
`EmitEncodeRequiredRepeatingWithTail`/`EmitDecodeRequiredRepeatingWithTail` model this;
only `maxOccurs=2` (bounded unroll) is supported, `maxOccurs≥3` with a tail is a
documented gap (no occurrence in CommonMessages/AC/DC). **Pitfall found:** the
tail particle can be a **required, non-nullable** field (e.g. `bool`) — a
presence check `is not null` doesn't compile for such a value type; the tail is therefore
written unconditionally, provided it isn't a choice/substitution member.

## Another pitfall: `abstract` on the TYPE, not on the element

`v2gci_ct:CLReqControlMode`/`CLResControlMode` (substitution-group heads in `CommonTypes`)
are themselves **not** `abstract="true"` — only their referenced type
(`CLReqControlModeType`/`CLResControlModeType`) is. The previous root filter
("document root?") only checked the element flag (`ge.IsAbstract`) and would have wrongly
treated these heads as document roots (leading to `Encode_CLReqControlMode` with no
associated codec). Fix: additionally check whether the resolved type itself is `IsAbstract`.

## New construct #2: substitution groups with a **concrete** (not abstract) head, partly **chained transitively**

Only in AC/DC (not in CommonMessages). Example (DC):

```xml
<xs:element name="DC_CPDReqEnergyTransferMode" type="DC_CPDReqEnergyTransferModeType"/>  <!-- concrete, not abstract -->
<xs:element name="BPT_DC_CPDReqEnergyTransferMode" type="BPT_DC_CPDReqEnergyTransferModeType"
            substitutionGroup="DC_CPDReqEnergyTransferMode"/>                              <!-- substitutes the CONCRETE head -->
```

and (DC, three levels deep):

```
v2gci_ct:CLReqControlMode (abstract, root)
  <- Scheduled_DC_CLReqControlMode (concrete, substitutionGroup=root)
       <- BPT_Scheduled_DC_CLReqControlMode (concrete, substitutionGroup=Scheduled_DC_CLReqControlMode)
  <- Dynamic_DC_CLReqControlMode (analogous)
       <- BPT_Dynamic_DC_CLReqControlMode
```

**Implemented.** `TryBuildSubstitution` now traverses transitively breadth-first (head →
direct members → their members → …), sorts the ENTIRE flat result alphabetically by
element name (confirmed against cbV2G's `iso20_dc_DC_ChargeLoopReqType`: 5 flat productions,
`BPT_Dynamic=0, BPT_Scheduled=1, [CLReqControlMode=2, abstract, no case], Dynamic=3,
Scheduled=4`, 3 bits). Whether a production gets a real runtime case or only reserves its
event-code slot is now decided by the **type** (`IsAbstractHead` checks
`schema.ComplexTypes[…].IsAbstract`), no longer "is this literally the named head" —
necessary because `CLReqControlMode` as an element is itself not abstract, only its type.

**Second pitfall found here:** -20 substitution members can extend **each other**
(not just the shared abstract head) — e.g. `BPT_AC_CPDReqEnergyTransferModeType
: AC_CPDReqEnergyTransferModeType` (both concrete). Since C#'s type pattern matching
(`case BaseType v`) also matches derived instances, a base case emitted first makes the
derived case unreachable (`CS8120`) — this never happened in -2 (there all members only
extend the shared abstract head, never each other). Fix: `EmitEncodeSubstitution` and
`EmitEncodeRunParticle` now emit the `case`/`if` branches **most-derived type first**
(`InheritanceDepth` walks up the `BaseRecordName` chain); the **wire event code**
stays bound to the original (alphabetical) position regardless of emission order. The
decoder needs no change (a numeric `switch`, no type pattern matching, no shadowing risk).

## Otherwise: no new fundamental constructs

`maxOccurs` up to 2048 (still `<4096`, the n-bit rule from Phase 2 applies unchanged; no
`unbounded` found in the three sets), attributes (`xs:ID`, required/optional, also in
combination with the new choice — e.g. `SignedInstallationDataType` has **both**:
a required `Id` **and** a required choice as a terminator; since the choice already runs
as a normal run terminator, the attribute-as-leading-optional pattern from Phase 2 fits in
front of it unchanged), `xs:string`/`xs:boolean`/`xs:byte`/`xs:short`/`xs:unsignedInt`/
`xs:unsignedLong`/`xs:unsignedShort`/`xs:unsignedByte`/`xs:base64Binary`/`xs:hexBinary` — all
already supported. No `xs:any`/`mixed` outside the already-opaque `xmldsig` namespace.

## Diff against the -2 inventory (docs/xsd-inventory-15118-2.md)

| Construct | -2 | -20 |
|---|---|---|
| Body dispatch | `V2G_Message` wrapper + substitution over `BodyElement` | every message its own global element, header inline |
| `xs:choice` (root, whole type content) | yes (`ParameterType`, `TransformType`) | yes (unchanged) |
| `xs:choice` (sequence terminator, mixed with other particles) | — | **new**, 9× in CommonMessages |
| Substitution group, abstract head | yes | yes (no `BodyElement` analogue here; but `CLReqControlMode` etc.) |
| Substitution group, concrete/chained head | — | **new**, AC/DC |
| `RationalNumberType` | — (there: `PhysicalValueType` with a unit) | new, but trivial (analogue of `PhysicalValueType`) |
| XMLDSig signature | ECDSA P-256/SHA-256 | **ECDSA secp521r1/SHA-512** (stronger suite per
  the spec) + **Ed448** (RFC 8032 — .NET has no built-in support, so this uses
  `BouncyCastle.Cryptography`) |

## Message overview (for vector prioritization)

- **CommonMessages** (17 pairs): SessionSetup, AuthorizationSetup, Authorization (EIM/PnC),
  ServiceDiscovery, ServiceDetail, ServiceSelection, ScheduleExchange (Scheduled/Dynamic),
  PowerDelivery, MeteringConfirmation, SessionStop, CertificateInstallation, VehicleCheckIn,
  VehicleCheckOut. `ScheduleExchangeRes` (the Dynamic branch with an optional
  Absolute/PriceLevel choice) and `SignedInstallationDataType`/`SignedMeteringDataType`
  (a required choice combined with a required attribute) are the most complex cases — tackle
  them first, they cover the most gaps (confirms the recommendation from phase4.md).
- **DC** (5 pairs): ChargeParameterDiscovery, CableCheck, PreCharge, ChargeLoop
  (Scheduled/Dynamic/BPT via three-level substitution), WeldingDetection.
- **AC** (2 pairs): ChargeParameterDiscovery, ChargeLoop (same substitution pattern as DC).

## Implementation order (this session)

1. ✅ The `InlineChoice` construct (generator + mini-XSD tests) — this practically blocked
   every CommonMessages message.
2. ✅ The `WWCP_ISO15118_20.CommonMessages` project: the full schema generates +
   compiles. Along the way, found and closed two more constructs: the
   bounded-repeating-list-with-tail (`AuthorizationServices`) and the abstract-on-type-instead-
   of-element pitfall (`CLReqControlMode`/`CLResControlMode`).
3. ✅ The `WWCP_ISO15118_20.DC`/`.AC` projects: both generate + compile.
   Transitive/concrete substitution implemented (confirmed against cbV2G's
   `iso20_dc_DC_ChargeLoopReqType`, 5 flat productions, 3 bits); found and fixed the
   pattern-matching shadowing pitfall along the way (the third new finding this session).
4. ✅ Byte vectors for CommonMessages/DC/AC against cbV2G, V2GTP dispatcher,
   secp521r1/SHA-512 signature suite (CommonMessages/DC/AC), `RationalNumber` helper.
5. ✅ WPT and ACDP (2026-07-11, originally out of scope) — see the section below.

## WPT/ACDP — completed afterward (2026-07-11)

ACDP generated and compiled immediately with no generator changes. WPT surfaced two new
EXI grammar constructs that none of the other four sets show.

### New construct: an optional bounded-repeating list in the middle of a sequence

`VendorSpecificDataContainer{0,16}` followed by another optional element
(`WPT_LF_DataPackageList?`), e.g. in `WPT_FinePositioningReqType`. Until now, an
optional bounded-repeating list was only supported as the *last* element of an optional
run (a true self-loop). Reconstructed byte-for-byte from cbV2G's generated C
(`iso20_WPT_Encoder.c`, states 178–180) — a confirmed cbexigen
special case:

- The "no elements yet" state only offers *[write first element]* or
  *[element EE]* — the following optional element is unreachable at this point.
- At this position the list is hard-capped at **2 elements**, regardless of the
  schema's `maxOccurs` (16 here) — cbexigen only unrolls two positions before it has to
  hand off to the following particles.

Implemented on the generator side in `EmitEncodeOptionalRunWithMidList`/`EmitDecodeOptionalRunWithMidList`
(`CodecEmitter.cs`). Byte-verified for the empty case (baseline vectors);
the case with list content + a following element is only self-consistency tested
(`Iso15118_20WptSelfConsistencyTests`), since it isn't part of the baseline vectors.

### New construct: a required list beyond the old `maxOccurs=2` limit, with an optional tail

`WPT_LF_TransmitterDataType.TxSpecData` (`minOccurs=2, maxOccurs=255`) followed by
`TxPackageSpecData?`. The existing construct #1b (`AuthorizationServices` →
`CertificateInstallationService`) only supported `maxOccurs=2` (unrolled) with a
*required* tail.

**There is no working cbV2G reference for this**: a standalone build of libcbv2g set up
specifically for this (gcc/cmake in WSL, see `tools/cbv2g-ref/`) shows that
cbV2G's own generated encoder for `WPT_LF_TransmitterDataType` fails with
`EXI_ERROR__UNKNOWN_EVENT_CODE` — and it fails already at the schema minimum of
2 `TxSpecData` elements. The generated state after the second element simply has no
loop option encoded anymore. This is a genuine cbexigen bug for this construct, not a
misunderstanding on our part (verified by calling
`encode_iso20_wpt_exiDocument` directly with a schema-valid instance).

With no reference to byte-diff against, an independent, spec-conformant grammar was designed
(generalized in `EmitEncodeRequiredRepeatingWithTail`/`EmitDecodeRequiredRepeatingWithTail`):
a true self-loop that offers `[loop, tail-start, element EE]` on every iteration.
Only self-consistency tested, not verifiable against cbV2G.

### ACDP: document-index grouping for shared types

`ACDP_DisconnectReq`/`Res` deliberately use the same types as `ACDP_ConnectReq`/`ResType`
(`type="ACDP_ConnectReqType"` etc.). cbV2G's document grammar (`encode_iso20_acdp_exiDocument`)
assigns directly consecutive indices to elements that share a type — grouped
by the alphabetically first element of that type (`ConnectReq=0, DisconnectReq=1,
ConnectRes=2, DisconnectRes=3`), NOT purely alphabetically by element name (which would
have placed `ConnectRes` before `DisconnectReq`). `GrammarBuilder.Build` now specifically
recognizes this (`sharedTypeGroups`); all other sets have a 1:1 element/type name pattern
and are unaffected by the change (confirmed by the full test run after the change).

**Payload types**: newly added `PayloadType_Iso20WPT = 0x8006` (from libcbv2g's
`include/cbv2g/exi_v2gtp.h`, `V2GTP20_WPT_MAINSTREAM_PAYLOAD_ID`); `PayloadType_Iso20ACDP
= 0x8005` was already correctly present.

**XMLDSig**: neither WPT nor ACDP have any `exiFragment`/signature construct in cbV2G
(no fragment structs, no `EncodeFragment`/`DecodeFragment` functions in the
generated headers) — confirmed by a full-text search in `iso20_{WPT,ACDP}_Datatypes.h` and
the corresponding `_Encoder.h` files. Nothing to implement.
