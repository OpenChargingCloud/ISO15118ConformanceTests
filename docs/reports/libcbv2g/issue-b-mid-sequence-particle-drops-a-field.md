# Issue B — post-ready

> **Rewritten 2026-11-06 around the half that is live, and measured on the live trees.** The original
> filed two defects in one construct: the list capped at two items, and the field after the list
> dropped when the list is empty. The cap is **gone** from everest-core's vendored
> `lib/everest/cbv2g` — the looping state now has an exit (`ID=185` in 2026.02.2, `ID=182`/`ID=188` on
> `main`) — and was a property of the archived standalone `EVerest/libcbv2g` at `03350be04`. The empty
> case is **live on both trees**: [`tools/cbv2g-defect-probe/probe.c`](../../../tools/cbv2g-defect-probe/README.md),
> compiled against the vendored sources of **2026.02.2** (`45dba2c0`) and of **`main`** (`01f36de1`,
> after the 2026-10-20 regeneration), encodes the message with the field and the message without it to
> the same 23 bytes on both. So this goes to **everest-core** and **cbexigen**; the standalone repository
> is being archived (its open PR #24) and is not where to file it.
> [Re-sweep](../../interop-runs/2026-11-04-counterparties-resweep/notes.md),
> [reports audit](../../interop-runs/2026-11-05-reports-audit/notes.md).

> Paste everything below the line. Self-contained.

---

**Title:** WPT FinePositioning: when `VendorSpecificDataContainer` is empty, the optional element after it is silently dropped

### Summary

ISO's schema, in all four `WPT_FinePositioning{,Setup}Req/ResType`:

```xml
<xs:element name="VendorSpecificDataContainer" type="…" minOccurs="0" maxOccurs="16"/>
<xs:element name="WPT_LF_DataPackageList"      type="…" minOccurs="0"/>
```

Two independently optional particles, the first repeating up to sixteen times. The generated grammar
in `lib/everest/cbv2g/lib/cbv2g/iso_20/iso20_WPT_Encoder.c` handles the list correctly once it has an
item, and has no way past it when it has none:

```
generated  no items  (2026.02.2 ID=184; main ID=181, ID=187):  START(VendorSpecificDataContainer)  END
           n items   (2026.02.2 ID=185; main ID=182, ID=188):  LOOP  START(WPT_LF_DataPackageList)  END

schema     state A (no items):  SE(list)=0  SE(following element)=1  EE=2
           state B (n items):   LOOP=0      SE(following element)=1  EE=2
```

State B matches the schema. State A is missing the production for the following element, so a document
ISO permits cannot be represented: **`WPT_LF_DataPackageList` (or `LF_SystemSetupData`) present with an
empty `VendorSpecificDataContainer`.**

### The part we would fix first: it fails silently

The encoder does not return an error. Set `LF_SystemSetupData` on a `WPT_FinePositioningSetupRes`, leave
`VendorSpecificDataContainer` empty, and call `encode_iso20_wpt_exiDocument` — the same probe, compiled
against the vendored sources of both trees:

```
                         2026.02.2 (45dba2c0)     main (01f36de1)
control, field not set   encoded (0)  23 B        encoded (0)  23 B
LF_SystemSetupData set   encoded (0)  23 B        encoded (0)  23 B     <- byte-identical to not setting it
```

The encoder returns success and drops the field. Same length as the message that never carried it: no
error, nothing short, nothing for a caller to check. Probe source:
<https://github.com/OpenChargingCloud/ISO15118ConformanceTests/tree/master/tools/cbv2g-defect-probe>

In the 2026.02 line it also masks a second defect. Because the encoder never descends into
`LF_SystemSetupData` in this state, the `minOccurs="2"` failure inside it (filed separately) cannot be
reached until this is fixed. On `main` that second defect is fixed by the regeneration, so the masking
is a release-line concern only.

### The visible part: one event code

With both particles absent, the no-items state has two productions where the schema grammar has three,
so the encoder writes the end-element as **1** where a schema-informed processor expects **2**. That
processor reads the `1` as a start element, finds no content, and reports `Premature EOS`. That is what
happened to all four `WPT_FinePositioning*` frames of ours and to nothing else in the set; the sibling
`WPT_AlignmentCheckReq` — an ordinary bounded list with nothing after it — round-trips byte-exact.

### Suggested fix

Emit the schema's grammar for the empty state too: the following particle has to be reachable from the
state before the first item, as it already is from the state after it. Since the regeneration fixed
state B, this looks like the same rule applied one state earlier.

### Context, and what we changed on our side

Found by cross-reading our ISO 15118-20 corpus — generated with cbV2G — against EXIficient: 332 of 347
frames round-tripped byte-for-byte. Our codec used to reproduce this grammar deliberately, to stay
byte-exact with cbV2G. We stopped on 2026-08-08 and now follow the schema here, which moved four of our
vectors. That is context you are owed rather than a complaint: the reason we noticed is that we had been
copying it.
