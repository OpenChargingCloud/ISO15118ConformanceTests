# RFC 8032 Ed448 reference vectors

Extracts the published Ed448 test vectors from RFC 8032 §7.4 into
`libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_EXI_Tests/Vectors/Ed448.rfc8032.vectors.json`.

```bash
curl -sSo /tmp/rfc8032.txt https://www.rfc-editor.org/rfc/rfc8032.txt
python3 extract.py /tmp/rfc8032.txt \
  > ../../libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_EXI_Tests/Vectors/Ed448.rfc8032.vectors.json
```

Nothing else here needs a toolchain: unlike `cbv2g-ref/` there is no C to build, because the
vectors are *published numbers*, not another implementation's output. That is also why they are
the strongest oracle in the repository — agreeing with them is agreeing with the specification,
not with a peer.

## Why a parser rather than copy-paste

A 114-byte signature is 228 hex characters spread over eight lines with page breaks through the
middle. Retyped by a human or a language model, a single wrong nibble produces a test that passes
against the wrong answer — the exact failure the vectors exist to prevent. So the RFC text is
parsed, and the parser asserts what it extracted: 57-byte keys, 114-byte signatures, and message
lengths matching the RFC's own `(length N bytes)` annotations. It fails loudly rather than
producing a short corpus.

## Scope: §7.4 only

**§7.5 is Ed448ph and is deliberately excluded.** It is a different algorithm — RFC 9231 §2.3.12
gives it its own XMLDSig identifier, `#eddsa-ed448ph`, separate from the `#eddsa-ed448` that
ISO 15118-20 uses — and mixing the two corpora would let a prehashed implementation pass as a pure
one. If -20 ever turns out to require the prehashed variant, that is a new corpus and a new signer,
not a flag.

## What the vectors are checked against

`libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_EXI_Tests/Ed448RfcVectorTests.cs` (BouncyCastle .NET) and
`libs/EVSimulatorApp/kotlin/exi-iso20-common/.../Ed448RfcVectorTest.kt` (BouncyCastle Java). Both back ends read *this*
file rather than each keeping a copy, as with the cbV2G corpus.

The two are worth running separately even though both say "BouncyCastle": the .NET library is a
port of the Java one, not the same code. Checking each against the standard is worth more than
checking them against each other — which is the same argument the cross-emitter gate rests on.

One vector carries a context string (`"foo"`) and shares its key and message with the empty-context
vector directly above it. That pair is what makes ISO 15118-20's empty context a *demonstrated*
choice rather than an assumed default.
