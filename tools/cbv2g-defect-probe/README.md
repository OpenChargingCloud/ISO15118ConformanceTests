# cbv2g-defect-probe — running the claims in the libcbv2g report instead of reading them

```bash
bash tools/cbv2g-defect-probe/build.sh
```

Compiles [`probe.c`](probe.c) against **libcbv2g `03350be048b3`** — the commit the report cites, and
still upstream `HEAD` — and exercises the two WPT defects in
[`docs/reports/libcbv2g-grammar-deviations.md`](../../docs/reports/libcbv2g-grammar-deviations.md)
through the public API, `encode_iso20_wpt_exiDocument`. Exit 0 means every claim held.

It reuses the clone the app's `cbv2g-ref` harness already fetched, and clones the pinned commit itself
otherwise. Needs a C compiler and, on a cold machine, network. Nothing in `dotnet test` touches it.

## What it prints

```
Part 1 — LF_SystemSetupData with VendorSpecificDataContainer EMPTY
  control, field absent      must encode        -> encoded              (0)  23 B
  receiver-2, empty vsdc     ?                  -> encoded              (0)  23 B
      ^ same length as the message WITHOUT the field: encoded "successfully",
        with LF_SystemSetupData silently dropped

Part 2 — the same, with ONE VendorSpecificDataContainer item so the suffix has a code
  no LF branch               must encode        -> encoded              (0)  28 B
  receiver-2                 claim: fails       -> UNKNOWN_EVENT_CODE   (-150)  50 B
  transmitter-2              claim: fails       -> UNKNOWN_EVENT_CODE   (-150)  53 B
  package-spec-2             claim: fails       -> UNKNOWN_EVENT_CODE   (-150)  53 B
```

## Why it exists, and what writing it changed

The report's issue C was derived by reading the generated C: three `minOccurs="2"` particles whose
`LOOP` grammar state has no exit production. A defect report that says *"we traced the control flow"*
invites the reply *"did you try it"*, so this tries it. Two things came out of doing so that reading
had not.

**The first run contradicted the report.** All three cases encoded cleanly, and the claim looked
wrong. It was not: **issue B masks issue C.** With an empty `VendorSpecificDataContainer` the suffix
`LF_SystemSetupData` has no event code, so the encoder never descends into it — it returns success and
writes a message the same length as one without the field. That is not a byte-level difference; it is
**silent data loss**, and it is why nobody has hit C in practice. Give the container one item and the
suffix becomes reachable, and C fires immediately.

That interaction is now the strongest part of the report, and it exists only because the probe was
built. The lesson generalises unpleasantly: a defect that drops a field quietly hides every defect
underneath it.

**Then the probe lied to us once more, and the compiler had already said so.** The first version wrote
`report(..., encode(&doc, &n), n)`. C does not order argument evaluation, so `n` was read before
`encode` filled it, and Part 1 printed `0 B` for both rows — from which the probe cheerfully concluded
"identical length, therefore dropped". The conclusion happened to be right and the evidence was
garbage. GCC had warned `'n' is used uninitialized` in the same output that was being read for the
result. Encode into a local, then report; and read the warnings in your own build before the numbers
in your own output.

## What this does *not* establish

Only issues B and C, and only for WPT. **Issue A** — the ACDP document element numbering — is not
exercised here: it is a difference in which byte gets written, not a failure, so the evidence for it is
the byte diff against EXIficient and EXI 1.0 §8.5.1, both in the report.
