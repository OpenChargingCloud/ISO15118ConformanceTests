# Issue C — post-ready

> Paste everything below the line. Self-contained: it does not refer to the other two filings except
> by name, and the one place it has to (B masking C) is written so it stands alone.

---

**Title:** `minOccurs="2"` repeating particles generate a LOOP state with no exit — three WPT types cannot be encoded

### Summary

For a repeating particle with `minOccurs="2"`, cbexigen emits a loop grammar state whose only
production is the loop itself. When the array runs out there is nowhere to go, and the encoder returns
`EXI_ERROR__UNKNOWN_EVENT_CODE`. Every schema-valid instance takes that path, so the affected types
cannot be encoded at all.

The generator's own grammar comments state it, in `lib/cbv2g/iso_20/iso20_WPT_Encoder.c`:

```c
// Grammar: ID=81; read/write bits=2; START (TxSpecData), END Element
// Grammar: ID=82; read/write bits=1; LOOP (TxSpecData)
```

Id 82 offers `LOOP` and nothing else — no `END Element`, and no `START` for the particle that follows.
Id 83, which would write the optional `TxPackageSpecData`, is unreachable from it.

Three types have the same shape:

| type | particle | states | dead end |
|---|---|---|---|
| `WPT_LF_TransmitterDataType` | `TxSpecData` (2, 255) | 81 / 82 / 83 | id 82 |
| `WPT_LF_ReceiverDataType` | `RxSpecData` (2, 255) | 88 / 89 / 90 | id 90 |
| `WPT_TxRxPackageSpecDataType` | `PulseSequenceOrder` (2, 255) | 74 / 75 / 76 | ids 74 **and** 75 |

`PulseSequenceOrder` is the worst of them: both of its states dead-end, so `PulseSeparationTime` at id
76 is unreachable and the type fails before the loop even begins.

### Reproducing

Encode a `WPT_FinePositioningSetupRes` whose `LF_SystemSetupData` carries either LF branch, with two
entries — the schema's own `minOccurs`, so this is a *minimal valid* document. Through the public
`encode_iso20_wpt_exiDocument`, against `03350be048b3`:

```
no LF branch      control       -> encoded              (0)  28 B
receiver-2        RxSpecData    -> UNKNOWN_EVENT_CODE (-150)  50 B
transmitter-2     TxSpecData    -> UNKNOWN_EVENT_CODE (-150)  53 B
package-spec-2    + pulse order -> UNKNOWN_EVENT_CODE (-150)  53 B
```

The control encodes, so the difference is the particle and not the caller. Full source of the probe:
<https://github.com/OpenChargingCloud/ISO15118ConformanceTests/tree/master/tools/cbv2g-defect-probe>

**One thing worth knowing before reproducing it.** Leave `VendorSpecificDataContainer` *empty* and all
three cases appear to encode fine — because the encoder then never descends into `LF_SystemSetupData`
at all. That is a separate defect (the mid-sequence particle grammar; filed separately), and it hides
this one. Give the container one entry, and this fires immediately. It cost us a false negative.

### Suggested fix

Give the loop state the exit productions the schema implies — the following particle and the
end-element — or reuse the self-looping shape already generated for a bounded list that ends a
sequence. One fix in the generator should cover all three types.

While you are there: id 81's `END Element` at 2 bits also permits an *empty* `TxSpecData`, which
`minOccurs="2"` forbids. Per EXI 1.0 Second Edition §8.5.4.1.5 the first `{min occurs}` copies of the
term carry no end-element production at all — which is also why their event code is one bit rather than
two.

### Not a schema problem

The construct is expressible: we encode these types, and EXIficient decodes the result and re-encodes
it to identical octets. This is a generator defect rather than an ambiguity in ISO's schema.

### Context

Found while cross-reading our ISO 15118-20 corpus — generated with cbV2G — against EXIficient. 332 of
347 frames round-tripped byte-for-byte, which is why we trust the library enough to file carefully
against it. Because these three types cannot be emitted, our own WPT corpus had no reference bytes
behind `LF_SystemSetupData` for a year, and a bug of ours in the *same* construct went unnoticed that
whole time — the forced prefix of a `minOccurs="2"` particle, which we encoded one bit too wide. A
generator that cannot emit a type quietly removes it from everyone's test coverage.
