# interop-exificient — a second opinion for ISO 15118-20. **Not yet concluded.**

> **Status: the tool works, the run does not.** Enough frames have round-tripped byte-exact to show the
> approach is sound, and no frame has ever come back *different*. But the rig cannot run it reliably
> end to end, for a reason that has nothing to do with V2G, so **there is no verdict over the -20
> corpus yet.** Read *Where it stands* before quoting anything from here.

## Why

[`interop-v2gdecoder`](../interop-v2gdecoder/README.md) closed the independence gap for ISO 15118-2 and
could not close it for -20: RISE-V2G predates the standard and ships no schemas for it. So -20 has
exactly one byte oracle — **libcbv2g**, which is also our vector generator, and also what EVerest and
tux-evse encode with. Every `-20` byte agreement this project can currently show is agreement with a
single implementation.

EXIficient is a general schema-informed EXI processor, not a V2G tool. Point it at an XSD and it encodes
and decodes against it; point it at ISO's own `-20` schemas and it becomes the second opinion, from the
codec family that shares no line with cbexigen.

    our bytes --(EXIficient decode)--> XML --(EXIficient encode)--> bytes  ==?  our bytes

No new download is needed: `com.siemens.ct.exi.main.cmd.EXIficientCMD` is inside the V2Gdecoder fat jar
that [`interop-v2gdecoder/setup.sh`](../interop-v2gdecoder/setup.sh) already fetches. Driving that class
directly skips RISE-V2G's -2-only wrapper. One download, two oracles.

Schemas are ISO's, read in place from the app submodule where `download-schemas.sh` puts them, and
staged to a scratch directory at run time. Nothing here copies them into this repository.

## Running it

```bash
bash tools/interop-v2gdecoder/setup.sh          # the jar
python3 tools/interop-exificient/roundtrip20.py \
    libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_EXI_Tests/Vectors/Iso15118_20.*.vectors.json \
    ISO15118ConformanceTests.Simulation/Vectors/Session.iso20-*.trace.json
```

Vector files pick their schema by filename; session traces pick it per frame from the V2GTP payload
type, which is what actually separates CommonMessages from AC from DC on the wire. Our streams are
schema-informed and **not** strict — passing `-strict` makes EXIficient throw on the first frame, which
is the expected answer and not a finding.

## Where it stands

| | |
|---|---|
| Frames that round-tripped **byte-exact** | 20 of 26 `CommonMessages` vectors in one run; 14 of 14 SAP frames in every run |
| Frames that came back **different** | **none, ever** — no mismatch has been observed at any point |
| Frames that failed | always the same way, and always *before* decoding: schema-model construction |

The failure is `Problem occured while building XML Schema Model` — Xerces reporting it cannot read
`xmldsig-core-schema.xsd`, an import sitting next to the schema that imports it, byte-identical to its
source (digests compared), well-formed (parsed), and readable by the shell at that moment.

It is not deterministic, which is what makes it expensive: the identical command against the identical
file failed **0 times in 30** in one window and **12 times in 12** an hour later. Measured and excluded,
rather than assumed: memory, file descriptors, JVMs left running, a full `/tmp`, a private
`java.io.tmpdir`, invocation rate, a corrupted copy off the Windows mount, the working directory, the
input and output paths, and a fresh WSL distro.

One thing does correlate, and it is the lead worth following: **the only schema with no imports — the
SupportedAppProtocol one — has never failed**, in any run, while every schema with an import chain has.
`AC_DER_IEC`, whose chain is two levels deep, failed all 16 frames even with twelve attempts each.

`roundtrip20.py` retries **only** this specific error, and reports the count. That is not papering over
a codec problem: a real codec failure is deterministic and reports something else, so retrying this one
error separates noise from signal. In the worst run it took 3,663 retries and still did not finish,
which is why there is no verdict.

## The way out

Every conversion currently costs its own JVM, and every JVM rebuilds the schema model from the XSDs —
694 of them for a full corpus run. **A single long-lived process that builds each grammar once would
avoid the whole problem**, and EXIficient's API supports exactly that (`GrammarFactory.createGrammars`
once, then `EXISource`/`EXIResult` per frame — the shape V2Gdecoder's own `dataprocess.java` uses).

That needs about forty lines of Java and a `javac`. This rig has six JVM installations and **not one
JDK** — all JREs — so it could not be compiled here. Installing one is the next step, and it should
make the run both reliable and roughly a hundred times faster.

Until then: the tool is committed because it is correct and because the exclusions above are worth more
than starting over. It has not yet produced a result anyone should cite.
