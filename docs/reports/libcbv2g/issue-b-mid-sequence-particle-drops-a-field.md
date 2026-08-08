# Issue B — post-ready

> Paste everything below the line. Self-contained.

---

**Title:** WPT FinePositioning: an optional element after an optional list is silently dropped, and the list is capped at two

### Summary

ISO's schema, in all four `WPT_FinePositioning{,Setup}Req/ResType`:

```xml
<xs:element name="VendorSpecificDataContainer" type="…" minOccurs="0" maxOccurs="16"/>
<xs:element name="WPT_LF_DataPackageList"      type="…" minOccurs="0"/>
```

Two independently optional particles, the first repeating up to sixteen times. The generated grammar
in `lib/cbv2g/iso_20/iso20_WPT_Encoder.c` unrolls only two list positions and reaches the second
particle only from the one-item state:

```
generated  id 178 (no items):   SE(list)=0                 EE=1
           id 179 (one item):   LOOP=0  SE(LF list)=1      EE=2
           id 180 (two items):          SE(LF list)=0      EE=1     <- no third item, ever

schema     state A (no items):  SE(list)=0  SE(LF list)=1  EE=2
           state B (n items):   LOOP=0      SE(LF list)=1  EE=2     <- loops to maxOccurs
```

Two documents ISO permits therefore cannot be represented:

1. **A third `VendorSpecificDataContainer`.** Id 180 has no production for another item. The struct
   array is sized 16, so the ceiling is in the grammar rather than the storage.
2. **`WPT_LF_DataPackageList` (or `LF_SystemSetupData`) with an empty container.** Id 178 offers only
   the list and the end-element.

### The part we would fix first: it fails silently

Case 2 does not return an error. Set `LF_SystemSetupData` on a `WPT_FinePositioningSetupRes`, leave
`VendorSpecificDataContainer` empty, and call `encode_iso20_wpt_exiDocument` against `03350be048b3`:

```
control, field not set   -> encoded (0)  23 B
LF_SystemSetupData set   -> encoded (0)  23 B     <- byte-identical to not setting it
```

The encoder returns success and drops the field. Same length as the message that never carried it: no
error, nothing short, nothing for a caller to check. Probe source:
<https://github.com/OpenChargingCloud/ISO15118ConformanceTests/tree/master/tools/cbv2g-defect-probe>

It also masks a second defect. Because the encoder never descends into `LF_SystemSetupData` in this
state, the `minOccurs="2"` failure inside it (filed separately) cannot be reached until this is fixed.

### The visible part: one event code

With both particles absent the encoder writes **1** for the end-element where the schema grammar has
**2**. A schema-informed EXI processor reads that `1` as a start element, finds no content, and reports
`Premature EOS`. That is what happened to all four `WPT_FinePositioning*` frames of ours and to nothing
else in the set; the sibling `WPT_AlignmentCheckReq` — an ordinary bounded list with nothing after it —
round-trips byte-exact.

### Suggested fix

Emit the schema's grammar for this construct: loop to `maxOccurs`, and keep the following particle
reachable from every state including the empty one. In our own generator the schema-conformant emitter
is *shorter* than the cbexigen-compatible one, because there is nothing to unroll.

### Context, and what we changed on our side

Found by cross-reading our ISO 15118-20 corpus — generated with cbV2G — against EXIficient: 332 of 347
frames round-tripped byte-for-byte. Our codec used to reproduce this grammar deliberately, to stay
byte-exact with cbV2G. We stopped on 2026-08-08 and now follow the schema here, which moved four of our
vectors. That is context you are owed rather than a complaint: the reason we noticed is that we had been
copying it.
