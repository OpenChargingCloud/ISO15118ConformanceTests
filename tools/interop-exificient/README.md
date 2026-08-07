# interop-exificient — the second opinion for ISO 15118-20

**347 frames, 332 byte-exact, 9 mismatches, 6 unreadable, 3.7 seconds.**
Run of 2026-08-07: [`docs/interop-runs/2026-08-07-exificient-iso20/`](../../docs/interop-runs/2026-08-07-exificient-iso20/notes.md).

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
recorded for `-2` in `Interop/ExiStringTableTests.cs`, and one (`ACDP_ConnectRes`, two bytes) is
unexplained.

**Six frames EXIficient cannot read at all** — four WPT, one ACDP, one AC_DER_SAE. Those are precisely
the message sets the interop matrix marks as *codec only — no independent stack implements session
state machines for them*, so their expected bytes had never been judged by anything but the generator
that produced them. Whether the fault is ours or EXIficient's is **not yet established**; five of the
six are under 25 bytes, so a bit-level walk against the schema should settle it.
