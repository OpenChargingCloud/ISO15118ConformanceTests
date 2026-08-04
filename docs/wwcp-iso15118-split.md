# Splitting the repository: codec → WWCP_ISO15118, conformance stays

This repository currently holds two things that have grown together but do not belong together:

1. **an ISO 15118 EXI codec** — a source generator, the schemas it consumes, and the tests that
   pin its output byte for byte;
2. **a conformance and interoperability harness** — SECC/EVCC state machines, TLS profiles,
   SLAC/SDP, and the live runs against Josev, EVerest and EVDriveFlow.

The first is a library other projects want to reference. The second is a test rig, and its value
is precisely that it is *not* the implementation under test. This document records which files go
where, and — more usefully — the three places where the split is not mechanical.

Written after the namespace rewrite (`Vanaheimr.V2G.*` → `cloud.charging.open.protocols.*`), which
is the step that made the seam visible.

## The dependency graph already agrees

Nothing in the codec set references anything in the conformance set. The arrows all point one way:

```
  EVSimulatorApp.Codegen ──┐  Exi.SourceGenerator ─────────┐   (Roslyn; netstandard2.0)
  (Kotlin/TS/Swift back    │           ▲                   │
   ends; linked source) ───┘           │ Analyzer          │
                                       │                   ▼
                        Exi.Prototype ──── BitReader/BitWriter, ExiPrimitives,
                              ▲            AppProtocol codec, V2GTP header
            ┌─────────────────┼──────────────────┬──────────────┐
            │                 │                  │              │
     Exi.Iso15118_2   Exi.Iso15118_20.*    Exi.XmlDsig    Exi.Dispatch
            │                 │                  │              │
            └─────────────────┴──────────────────┴──────────────┘
                              │
        ╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┼╌╌╌╌╌╌╌ the seam ╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌
                              │
            ┌─────────────────┴──────────────┐
     Vanaheimr.V2G.Simulation        Experiments.Pqc
            │                                │
     Simulation.Cli ── Simulation.Tests ── Pqc.Tests
```

The conformance side additionally references `WWCP_ISO15118_SDP`, `_NetworkInterfaces`, `_SLAC` and
`_PKIBuilder` — that is, it *already* depends on WWCP_ISO15118. After the move it depends on it for
the codec too, and this repository's own `libs/WWCP_ISO15118` submodule keeps working unchanged.
No cycle appears in either direction.

## What moves

| Project | .cs | What it is |
|---|--:|---|
| `WWCP_ISO15118_EXI_SourceGenerator` | 31 | XSD → grammar → codec, plus the C# back end — see "the port back ends" below |
| `WWCP_ISO15118_EXI` | 12 | `BitReader`/`BitWriter`, EXI primitives, the hand-written AppProtocol codec, the V2GTP header |
| `WWCP_ISO15118_2` | 2 | -2 schemas + `PhysicalValue`, `V2GSignature` |
| `WWCP_ISO15118_20.CommonMessages` | 2 | schemas + `RationalNumber`, `V2GSignature` |
| `WWCP_ISO15118_20.{DC,AC}` | 2 each | ditto |
| `WWCP_ISO15118_20.{AC_DER_IEC,AC_DER_SAE}` | 1 each | ditto |
| `WWCP_ISO15118_20.{WPT,ACDP}` | 0 | schema-only; everything is generated |
| `WWCP_ISO15118_XMLDSig` | 0 | schema-only |
| `WWCP_ISO15118_EXI_Dispatch` | 2 | payload type ↔ message set |
| `ChargingSimulation` (demos/) | 6 | the "every line is a real EXI round-trip" console demo |
| `WWCP_ISO15118_EXI_Tests` | 55 | codec tests, minus `Interop/` and the port-emitter tests |
| `WWCP_ISO15118_EXI_Tests/Vectors/` | 16 files | the byte-level oracle |
| `tools/cbv2g-ref/`, `tools/exificient-ref/` | — | the reference encoders that *produce* those vectors |

Roughly 130 source files. The generated code is not among them: it exists only in `obj/`, produced
at build time from the XSDs, so "the generated code moves" means the schemas and the generator move.

## What stays

| Project | .cs | Why it is not the codec |
|---|--:|---|
| `Vanaheimr.V2G.Simulation` | 53 | SECC/EVCC state machines, TLS profiles, SLAC, metering, OCPP |
| `Vanaheimr.V2G.Simulation.Cli` | 5 | the harness binary |
| `Vanaheimr.V2G.Simulation.Tests` | 68 | loopback, traces, and `Interop/` — Josev, EVerest, EVDriveFlow |
| `Vanaheimr.V2G.Experiments.Pqc` (+ Tests) | 6 | ML-DSA in a V2G signature; research, not the standard |
| `WWCP_ISO15118_EXI_Tests/Interop/` | 6 | `Josev*` — see the boundary question below |
| `tools/interop-{josev,everest,evdriveflow}/` | — | live harnesses |
| `docs/interop-runs/` | 30+ dirs | evidence of runs; belongs with the rig that produced it |

## The port back ends did not go to WWCP_ISO15118

The generator emits C#, Kotlin, Swift and TypeScript. Only the first is of use to a .NET
consumer, and the sizes say the rest loudly:

| | lines |
|---|--:|
| front end (`Xsd/` + `Grammar/`) | 1 771 |
| `Emit/` shared base | 2 634 |
| **C# back end** | **715** |
| Kotlin / Swift / TypeScript | 8 651 |

So the three port back ends, the `Codegen` driver that runs them, and their nine test files live
in the app instead — `tools/EVSimulatorApp.Codegen` and `.Codegen.Tests`. The app is their only
consumer, and putting them in an ISO 15118 library would have meant 8 651 lines of Kotlin and
Swift templating that no .NET caller will ever reach.

Nothing new was invented to make this work. `Codegen` never referenced the generator as an
assembly — the generator is a netstandard2.0 analyzer with a Roslyn dependency the driver must
not carry — so it always compiled `Xsd/`, `Grammar/` and `Emit/` in as linked source. The app does
the same, across the submodule boundary.

Two consequences worth stating plainly:

- **`CodecEmitter` is the base class all four back ends specialise, and it stays here.** A change
  to it is a change to two repositories. That is the price of the split, and it is not hidden:
  the app's build breaks immediately if the base moves out from under it.
- **The differential tests moved too.** Every Swift and Kotlin test asserts against what the C#
  back end emits for the same schema — that is what caught a TypeScript emitter spelling an
  optional type the way Kotlin does. They still work, because the C# emitter arrives through the
  same linked sources; they simply run in the app's suite now. `EmitterHarness` is linked rather
  than copied so the two sides cannot drift about what "emit this schema" means.

Verified by regenerating rather than by reasoning: all 1 395 checked-in Kotlin files, and the
Swift and TypeScript AppProtocol sets, come out byte-identical from the driver's new home.

## Three things the split is not mechanical about

### 1. V2GTP exists twice

`WWCP_ISO15118_V2GTP` already implements V2GTP as `V2GTP_Frame` / `V2GTP_Header` /
`V2GTP_PayloadType` in `cloud.charging.open.protocols.ISO15118.V2GTP`, with its own exception
hierarchy and its own tests under `tests/V2G.V2GTP.Tests/`. This repository implements the same
eight header bytes again in `WWCP_ISO15118_EXI/V2GTP/V2GTP.cs` plus
`WWCP_ISO15118_EXI_Dispatch`, in a nullable-return style with no exceptions.

The rename surfaced this rather than causing it, and it surfaced it *hard*: mapping the framing
namespace onto `cloud.charging.open.protocols.ISO15118.V2GTP` does not even compile, because the
class is called `V2GTP` and the namespace would shadow it for everything under
`cloud.charging.open.protocols.ISO15118`. It is parked at
`cloud.charging.open.protocols.ISO15118.EXI.Dispatch` for now — a name that says what the project
is, and deliberately does not squat on the namespace the two implementations will have to merge
into.

Merging them is a decision about wire behaviour (exceptions vs nullable returns on a malformed
frame), so it is not a rename. It should happen *before* the move, not after — afterwards there are
two V2GTPs in one repository.

### 2. Josev is both an oracle and a counterparty

The user rule is "interoperability tests stay". That is unambiguous for
`Vanaheimr.V2G.Simulation.Tests/Interop/` and the `tools/interop-*` scripts: those drive a live
peer over a socket.

It is less obvious for `WWCP_ISO15118_EXI_Tests/Interop/` (6 files) and
`JosevCuratedVectorTests.cs`. Those tests never open a socket — they decode bytes Josev *once*
produced, checked into `Vectors/Iso15118_20.DC.josev.vectors.json`. Functionally they are codec
tests whose oracle happens to be a third-party encoder, which is exactly what `CLAUDE.md` demands
("only based on a concrete byte diff against a reference encoder").

Recommendation: **the vectors move with the codec, the live harnesses stay.** A codec that arrives
in WWCP_ISO15118 without its byte-level oracle cannot be changed safely there. The six `Josev*.cs`
files under `Exi.Tests/Interop/` are vector-driven and should move with them; the naming is what
makes them look otherwise. Flagging it rather than deciding it, because the instruction was explicit.

### 3. `RationalNumber` will exist twice, and `V2GSignature` five times

`WWCP_ISO15118_20/CommonTypes/Complex/RationalNumber.cs` and the generated
`cloud.charging.open.protocols.ISO15118_20.{AC,DC,CommonMessages}.RationalNumber` are different
types with the same name. They do not collide — different namespaces, and the generated codec lives
one level down in `.Generated` — so this compiles. It is still two spellings of one concept in one
repository, and someone will pick the wrong one.

The five `V2GSignature.cs` copies (one per -20 message set) are deliberate: the csproj comments
record that CommonTypes is duplicated per message-set assembly to mirror cbexigen/cbV2G, because the
grammars are per-set and self-contained. That reasoning survives the move. Worth a note in the
target repo's README so it does not get "cleaned up".

## Suggested layout in WWCP_ISO15118

Matching the repository's existing `WWCP_ISO15118_<area>` convention:

```
WWCP_ISO15118_EXI/                      ← Exi.Prototype
WWCP_ISO15118_EXI_SourceGenerator/
WWCP_ISO15118_EXI_Dispatch/
WWCP_ISO15118_EXI_Tests/
WWCP_ISO15118_2_EXI/                    ← Exi.Iso15118_2 (schemas)
WWCP_ISO15118_20_EXI_CommonMessages/    ← and DC, AC, AC_DER_IEC, AC_DER_SAE, WPT, ACDP
WWCP_ISO15118_20_EXI_XMLDSig/
tools/cbv2g-ref/, tools/exificient-ref/
```

Directory and assembly names are **not** part of the namespace rewrite that has already happened —
they are part of the move itself, and several tests locate schema sets by walking to a directory of
that exact name (`EmitterHarness.RealSchemaSet("WWCP_ISO15118_2")`). Rename the
directories and those strings in the same commit, or the tests fail in a way that looks like a codec
regression.

## Namespace map, as applied

| was | is |
|---|---|
| `Vanaheimr.V2G.Exi` | `cloud.charging.open.protocols.ISO15118.EXI` |
| `WWCP_ISO15118_EXI_SourceGenerator[.Emit\|.Grammar\|.Xsd]` | `cloud.charging.open.protocols.ISO15118.EXI.SourceGenerator[…]` |
| `EVSimulatorApp.Codegen` | `cloud.charging.open.protocols.ISO15118.EXI.Codegen` |
| `ChargingSimulation` (demos/) | `cloud.charging.open.protocols.ISO15118.EXI.Simulation` |
| `WWCP_ISO15118_EXI_Tests[.Infrastructure\|.Interop]` | `cloud.charging.open.protocols.ISO15118.EXI.Tests[…]` |
| `Vanaheimr.V2G.AppProtocol` | `cloud.charging.open.protocols.ISO15118.AppProtocol` |
| `Vanaheimr.V2G.Tp` | `cloud.charging.open.protocols.ISO15118.EXI.Dispatch` — see §1 |
| `Vanaheimr.V2G.Iso15118_2` | `cloud.charging.open.protocols.ISO15118_2` |
| `Vanaheimr.V2G.Iso15118_20.*` | `cloud.charging.open.protocols.ISO15118_20.*` |
| `Vanaheimr.V2G.XmlDsig` | `cloud.charging.open.protocols.ISO15118_20.XMLDSig` |
| `Vanaheimr.V2G.Simulation.*` | *unchanged* — this is the conformance project |
| `Vanaheimr.V2G.Experiments.*` | *unchanged* |

`AppProtocol` sits under `ISO15118` rather than `ISO15118_2`, because SupportedAppProtocol is what
*chooses* between -2 and -20; it cannot belong to either.
