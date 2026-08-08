# interop-exificient — the second opinion for ISO 15118-20

**347 frames, 339 byte-exact, 8 mismatches, nothing unreadable, 3.7 seconds.**
The first run was 332 / 9 / **6**; all six causes are settled.
Runs: [`2026-08-07-exificient-iso20`](../../docs/interop-runs/2026-08-07-exificient-iso20/notes.md) —
what it found; [`2026-08-08-schema-conformant-acdp-wpt`](../../docs/interop-runs/2026-08-08-schema-conformant-acdp-wpt/notes.md) —
what was decided about it.

## Why

[`interop-v2gdecoder`](../interop-v2gdecoder/README.md) closed the independence gap for ISO 15118-2 and
could not close it for -20: RISE-V2G predates the standard and ships no schemas for it. So -20 had
exactly one byte oracle — **libcbv2g**, which is also our vector generator, and also what EVerest and
tux-evse encode with. Every `-20` byte agreement this project could show was agreement with a single
implementation.

EXIficient is a general schema-informed EXI processor, not a V2G tool. Point it at ISO's own `-20`
schemas and it becomes the second opinion, from the codec family that shares no line with cbexigen.

    our bytes --(EXIficient decode)--> XML --(EXIficient encode)--> bytes  ==?  our bytes

No new download: `EXIficient` is inside the V2Gdecoder fat jar that
[`interop-v2gdecoder/setup.sh`](../interop-v2gdecoder/setup.sh) already fetches. One download, two
oracles. Schemas are ISO's, read in place from the app submodule and staged to a scratch directory at
run time; nothing here copies them into this repository.

## Running it

```bash
bash tools/interop-v2gdecoder/setup.sh
python3 tools/interop-exificient/roundtrip20.py \
    libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_EXI_Tests/Vectors/Iso15118_20.*.vectors.json \
    ISO15118ConformanceTests.Simulation/Vectors/Session.iso20-*.trace.json
```

Needs a **JDK**, not just a JRE: `roundtrip20.py` compiles `Roundtrip20.java` on the way in. Vector
files pick their schema by filename; session traces pick it per frame from the V2GTP payload type,
which is what actually separates CommonMessages from AC from DC on the wire. Our streams are
schema-informed and **not** strict — `-strict` makes EXIficient throw on the first frame, which is the
expected answer and not a finding.

This is a tool, not part of `dotnet test`: the offline run stays green without Java.

## Two things that will bite anyone building this

**Never let a grammar build touch the network.** W3C's `xmldsig-core-schema.xsd` — pulled in by ISO's
`V2G_CI_CommonTypes.xsd`, and therefore by every `-20` message set — opens with a DOCTYPE pointing at
`http://www.w3.org/2001/XMLSchema.dtd`. Xerces fetches it on **every** grammar build, and W3C has
rate-limited that traffic for years. Once the requests start being refused, the failure is reported
against the **local** file: `Failed to read schema document 'xmldsig-core-schema.xsd'`, naming a path
that is present, readable and correct. It looks like flaky I/O and it is a throttled HTTP GET. The
`XMLEntityResolver` in `Roundtrip20.java` resolves every remote entity to an empty stream, which is
both the fix and what an offline harness should be doing regardless.

**Build each grammar once.** EXIficient's own CLI rebuilds the schema model per invocation, so a corpus
run is ~700 model builds — 700 chances at the above, and a great deal of wall clock. `Roundtrip20.java`
is one JVM with a grammar cache: eight builds for the whole corpus, and the run takes under four
seconds.

## What the first run found

The full account is in the run notes. In short: 332 frames round-tripped byte-exact — both control
modes, AC and DC, EIM and Plug & Charge, five complete sessions, signed messages and certificate chains.

Nine mismatches, of which seven and one have the shape of the EXI value-partition difference already
recorded for `-2` in `Interop/ExiStringTableTests.cs`, and one (`ACDP_ConnectRes`, two bytes) was
unexplained — it turned out to be the other half of the first cause below, and cleared with it.

**Six frames EXIficient could not read at all** — four WPT, one ACDP, one AC_DER_SAE — all in message
sets the interop matrix marks *codec only*, whose expected bytes had never been judged by anything but
the generator that produced them. Cleared up the same day, three separate causes and all three ours:

- **ACDP element numbering.** We number the schema's global elements so that the two sharing an aliased
  type land adjacent; EXI requires sorting by qname. Indices 1 and 2 are swapped, and those are exactly
  the two ACDP frames that failed — one unreadable, one silently decoded as a different message. ACDP is
  the only `-20` set with an aliased type, and cbV2G numbers them the same way we do, so nothing caught it.
- **WPT FinePositioning.** Our generator reproduced cbV2G's grammar for two optional particles instead of
  the schema's, and says so in the generated code. With both absent we write event code 1 for the
  end-element where the schema has a start-element there. Exactly the four failing messages.
- **`AC_ChargeParameterDiscoveryRes_DER`** — a plain defect of ours, and fixed the same day. A particle
  with `minOccurs="2"` forces its second occurrence, so that occurrence's start-element is a one-bit
  code with nothing to choose from; we wrote the two-bit loop code and every bit after it was one
  position out. ISO has five such particles and all five are in sets no reference encoder covers, which
  is why nothing had ever caught it. Unlike the two above this needed no switch — there were no
  reference bytes to stay compatible with.

**The first two were decided on 2026-08-08: follow the schema.** EXI 1.0 §8.5.1 sorts global elements
by qname with no exception for a shared type, and cbexigen's WPT grammar contradicts its own input
schema badly enough that valid documents cannot be encoded at all. `Directory.Build.props` in the app
now sets both properties; six vectors moved and say so in their corpus headers; both findings are
drafted for libcbv2g in [`docs/reports/`](../../docs/reports/libcbv2g-grammar-deviations.md). **The
corpus went to 339 / 8 / 0 — nothing in `-20` is unreadable any more, and all eight remaining
mismatches are the one value-partition cause.**

Full account in the run notes.

## Walking one frame

`roundtrip20.py` says *whether* a frame reads. When it does not, the message is `Premature EOS`, which
says only that EXIficient ran out of bits — not where, and the difference matters: a frame fails that
way both when the first event code was wrong and when 240 of 241 bytes were fine.

```bash
python3 tools/interop-exificient/walk20.py <vectors.json> <FrameName> [<FrameName>...]
```

drives EXIficient's event API instead of its SAX bridge ([`Walk20.java`](Walk20.java)) and prints every
event as it is decoded, indented by depth, then the exception and the element stack it died on. Pass a
working sibling alongside the failing frame — a trace reads far better against one that is known good.
This is what located cause C to a single particle.
