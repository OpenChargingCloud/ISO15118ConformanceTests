# Replacing Seed Vectors with cbV2G Reference Output

The vectors in `AppProtocol.vectors.json` were initially **self-encoded by the
codec under test**. They prove only internal self-consistency — not wire
conformance. To upgrade them to true conformance vectors, regenerate
`expectedHex` using cbV2G (or OpenV2G) as the external reference encoder.

## Recommended workflow

1. **Build cbV2G locally** as a small CLI that takes the vector input JSON on
   stdin and emits hex on stdout. cbV2G already has a SAP (Supported App
   Protocol) encoder — just wire it to your input shape.

2. **Pin a cbV2G commit** in the test repo. Wire-format conformance is only
   meaningful relative to a specific reference; pin the SHA in
   `vectors_seed.json` under a new `referenceEncoder` field, e.g.:

   ```json
   "referenceEncoder": {
     "name": "cbV2G",
     "repo": "https://github.com/EVerest/cbv2g",
     "commit": "abc1234...",
     "buildFlags": "-DCBV2G_DEBUG_OUTPUT=ON"
   }
   ```

3. **Regenerate** for every vector in `AppProtocol.vectors.json`:

   ```bash
   for v in $(jq -c '.vectors[]' AppProtocol.vectors.json); do
       hex=$(echo "$v" | jq '.input' | ./cbv2g_encode_sap)
       # patch back into JSON via jq
   done
   ```

4. **Strip the `generatorNote`** that warns about self-encoding once the
   vectors come from cbV2G. Update `generator` to `"cbV2G@<sha>"`.

5. **Run the test suite.** Mismatches are now meaningful: each failure is a
   bug in the C# codec relative to the reference. The `HexUtil.Diff` output
   will pinpoint the first differing bit.

## Vector inputs that should be added before this is "done"

Beyond what's in the seed file, conformance against cbV2G needs:

- All three `ResponseCode` values × {SchemaID present, absent}.
- Maximum-size request (20 entries) — exercises the `Req_20` 0-bit terminator.
- Single-entry request — exercises `Req_0` followed by EE, the most common
  real-world path.
- `Priority` at every boundary: 1, 2, 19, 20.
- Long protocol namespaces near the schema's `maxLength="100"` limit.
- Non-ASCII characters in the namespace (legal per xs:string, will exercise
  multi-byte rune encoding).

## On the divergence between Python sim and C# codec

If the Python simulator and the C# codec ever produce different bytes, the
seed vectors will fail in CI. That's a feature, not a bug: it means one of
the two has drifted. Resolve by inspection (the Python simulator is deliberately
the same shape as the C# code so a diff is short) and only **then** regenerate.

Once cbV2G is the reference, the Python simulator can be deleted — its only job
was to bootstrap the test harness before the external encoder was wired up.
