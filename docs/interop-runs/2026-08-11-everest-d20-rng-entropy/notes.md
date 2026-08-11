# 2026-08-11 — how many bits are in a SessionID: an entropy audit of four ISO 15118-20 stacks

The `-20` session-id probe earlier today asked whether a station *compares* the SessionID it was
sent. This asks the question one layer down — how much is in the value the station **generates** —
and it is the first run here whose measurement substrate was already in this repository before the
question was asked.

| | |
|---|---|
| Requirement | `[V2G20-835]` a cryptographically secure RNG *shall* be used; `[V2G20-2621]` SessionID ≥ **58 bits** of entropy; `[V2G20-697]`/`[V2G20-698]` GenChallenge 128 bits wide and ≥ **120 bits** of entropy; `[V2G20-2608]` used only once. `[V2G2-835]`/`[V2G2-697]`/`[V2G2-698]` are the identical rules under the identical numbers in `-2` |
| Measured | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12b8`), `lib/everest/iso15118/`, and standalone [`EVerest/libiso15118`](https://github.com/EVerest/libiso15118) `5c81c92` |
| Read | [SwitchEV/iso15118](https://github.com/SwitchEV/iso15118) `d645255`; [eVDriveFlow](https://github.com/EDF-Lab/eVDriveFlow) `60249c3`; EVerest `EvseV2G` at the same tag; our own stack |
| Outcome | Josev **correct**, `EvseV2G` **correct**, EVerest `-20` **≤ 32 bits**, eVDriveFlow **26,6 bits**. Filed: [`everest-d20-rng-entropy.md`](../../reports/everest-d20-rng-entropy.md) and [`evdriveflow-session-id-entropy.md`](../../reports/evdriveflow-session-id-entropy.md) |
| Artifacts | [`seedsearch.log`](seedsearch.log) · [`collide.log`](collide.log) · [`sessionids.txt`](sessionids.txt) · tool: [`tools/everest-rng-probe/`](../../../tools/everest-rng-probe/README.md) |

## The measurement: 49 of 49 SessionIDs recovered from a 32-bit seed space

EVerest's `-20` library fills both its SessionID and its Plug & Charge `GenChallenge` with the same
five lines — `std::random_device rd; std::mt19937 generator(rd()); …` — and `std::mt19937`'s scalar
constructor takes **one 32-bit seed**. So the array's width is decorative: 8 bytes or 16, its
contents are a pure function of 32 bits.

That is a claim about their source. What makes it a claim about their **binary** is that this
repository already held 49 distinct SessionIDs their station had issued, read out of their own log
line `New session created with session_id: 0x…` across **twenty interop runs between 2026-08-03 and
2026-08-11** — the AC and DC matrices, MCS, BPT, the TLS runs, the contactor injection, the meter-info
control, the sequence-timeout arms. None of those runs was looking at RNG quality.

[`seedsearch`](../../../tools/everest-rng-probe/seedsearch.cpp) reproduces their five lines verbatim,
built with the same libstdc++, and walks the whole 2³² space:

```
searching all 2^32 mt19937 seeds for 49 SessionID(s)
threads: 16
  RECOVERED D3760BB5DA9E3DCE  <- mt19937 seed 0x03f4644f
  …
searched 2^32 seeds in 639.0 s
recovered 49 of 49 SessionID(s)
```

**49 of 49, in eleven minutes.** `[V2G20-2621]` asks for 58 bits in that field; every value they have
ever shown this suite carried at most 32.

**The exit code is the control**, and it was designed in before the run: `seedsearch` exits non-zero
if *any* target fails to recover. A toolchain whose `std::uniform_int_distribution<uint8_t>` differed
from the station's would have reported "not found" for everything — which looks exactly like a strong
RNG, and would have been the quietest possible way to get this wrong. 49 of 49 says the reproduction
is exact, and it says so from the data rather than from an argument about libstdc++ versions.

## What 32 bits costs a 128-bit challenge

[`collide`](../../../tools/everest-rng-probe/collide.cpp) draws N `GenChallenge`s their way and N from
`/dev/urandom`, and counts repeats. Both arms use the real `std::random_device`, so this is their code
path and not a model of it:

| N = 262 144 | distinct | repeated |
|---|---|---|
| libiso15118 (`mt19937`), run 1 | 262 136 | **8** |
| libiso15118 (`mt19937`), run 2 | 262 139 | **5** |
| control (`/dev/urandom`), both | 262 144 | **0** |

The birthday bound over a 2³² value space predicts N²/2³³ = 8,0. The count varies run to run because
it is a measurement, which is why both runs are in [`collide.log`](collide.log).

A 16-byte challenge that repeats after ~2¹⁶ draws is what `[V2G20-2608]` forbids — a value generated
under `[V2G20-835]` shall be used only once — and the repeat is the thing the challenge exists to
prevent. `[V2G20-2565]` has the SECC check that the `GenChallenge` echoed in `AuthorizationReq` is the
one it sent; that check is what makes a captured, signed `AuthorizationReq` useless the second time,
and it stops being useless when the challenge comes round again.

## The four-stack table

| stack | SessionID | GenChallenge | verdict |
|---|---|---|---|
| **Josev** (SwitchEV) `-2` · `-20` · DIN | `secrets.token_bytes(8)` — 64 bits | `secrets.token_bytes(16)` — 128 bits | **correct**, one helper (`shared/security.py:95`) doing all of it |
| **EVerest `EvseV2G`** (`-2`, DIN) | `/dev/urandom`, 8 bytes | `/dev/urandom`, `GEN_CHALLENGE_SIZE` | **correct** — `tools.cpp:38` |
| **EVerest `libiso15118`** (`-20`) | `mt19937`, one 32-bit seed | same | **≤ 32 bits** — measured above, **filed** |
| **eVDriveFlow** (`-20`) | `secrets.randbelow(10⁸)` as 8 ASCII digits | *(no PnC)* | **26,6 bits** — source, **filed** |
| *(ours)* | `RandomNumberGenerator.GetBytes(8)` | `…GetBytes(16)` | correct in both protocols |

Two of four right, and the two that are wrong are wrong in different ways — one has a correct
generator used over a 10⁸ range, the other a full-width range filled by a generator that is not
cryptographic. A report that had assumed the second cause for the first would have been wrong about
eVDriveFlow's code, which is why the tool measures rather than the reader infers.

**And the two defects sit in the same repository as a correct implementation each time.** EVerest's
`-2`/DIN module reads `/dev/urandom` for exactly the same two values that its `-20` library seeds a
Twister for. That is the second time in two days that a rule EVerest gets right in one module it gets
wrong in the other — the [`[V2G2-460]` zero exemption](../2026-08-11-everest-iso2-session-id-zero/notes.md)
was the same shape in the opposite direction.

## What a corpus cannot see, and why this class kept hiding

**No amount of message-level conformance testing can find any of this.** The bytes are the right
width, in the right field, schema-valid, and different every session; every oracle in this repository —
the vector corpus, EXIficient, V2Gdecoder, the loopback E2Es — would pass all four stacks. An entropy
requirement is invisible to anything that reads one message, and reachable only two ways: read the
generator, or collect enough values to count collisions. This run did both, on different stacks.

Worth putting beside the other "a question our car cannot ask" findings, because it is the same lesson
from a different side: those were gaps in what we could *send*, this is a gap in what a message can
*carry*. Some requirements have no observable at all.

## A correction this run made to its own write-up

The first draft of the [`normative-basis.md`](../../normative-basis.md) section said ISO 15118-2 has
no equivalent of any of this. **It has most of it, under the same requirement numbers**: `[V2G2-835]`
is word-for-word `[V2G20-835]`, and `[V2G2-697]`/`[V2G2-698]` are `[V2G20-697]`/`[V2G20-698]` — the
same pattern as `[V2G2-169]`/`[V2G20-169]`. What `-20` genuinely adds is the SessionID's 58-bit floor
(`[V2G20-2621]`; `-2` has only `[V2G2-750]`, generate a fresh non-zero value), plus `[V2G20-2607]`,
`[V2G20-2608]` and the 116-bit default.

That correction is not cosmetic — it decides what may be said to whom. It removes any "this is a `-20`
novelty" defence for the challenge, since `EvseV2G` in the same repository already satisfies the `-2`
form of the identical rule; and it stops the finding being pointed at a DIN or `-2` module's
*SessionID*, which no entropy requirement covers. Third time this month that reading one clause
further changed a claim.

## What this does not decide

- **The `GenChallenge` was not observed on the wire**, from anyone. EVerest's `-20` module has PnC
  commented out (*"Currently Plug&Charge is not supported and ignored"*), so their station never emits
  one today; the finding is about the code that will run when it does, and the collision measurement is
  of that code rather than of their traffic. Said plainly in the filing.
- **`std::random_device` itself was not audited.** If it were weak on some platform the finding would
  be worse, not better; the 32-bit ceiling holds either way, because it is the seed *width* that
  bounds it.
- **eVDriveFlow was read, not run.** One session would show it — their SessionID is eight ASCII digits
  — and their rig needs docker; the filing's checklist says so.
- **No claim about their PRNG's predictability across values.** Each site constructs a fresh
  `random_device` and a fresh generator, so recovering one seed does not predict the next. The finding
  is the 32-bit ceiling and the collisions it implies, not a stream attack.
- **tux-evse is not in the table**: `-2` and DIN only, and `-2` has no SessionID entropy requirement.
  Their `GenChallenge` handling would be in scope for `[V2G2-698]` and was not looked at.

## Reproduce

```bash
g++ -O3 -march=native -pthread -o seedsearch tools/everest-rng-probe/seedsearch.cpp
awk '{print $1}' docs/interop-runs/2026-08-11-everest-d20-rng-entropy/sessionids.txt > ids.txt
./seedsearch ids.txt          # ~11 min on 16 threads; exit 0 only if every target recovers

g++ -O3 -march=native -o collide tools/everest-rng-probe/collide.cpp
./collide 262144
```

Build both with the toolchain that built the station under test. That is what makes the reproduction
exact instead of approximate, and the non-zero exit is what catches it when it is not.
