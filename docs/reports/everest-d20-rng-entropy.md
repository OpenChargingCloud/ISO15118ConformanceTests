# Draft report to EVerest (libiso15118) — the SessionID and the PnC GenChallenge carry 32 bits, whatever their width

Status: **draft, not sent.** Post it under your own name; see *Before sending* at the bottom.

Evidence in this repository:
[`2026-08-11-everest-d20-rng-entropy`](../interop-runs/2026-08-11-everest-d20-rng-entropy/notes.md),
and the two programs that produced it,
[`tools/everest-rng-probe/`](../../tools/everest-rng-probe/README.md).

---

**Title:** `d20::Session` and `AuthorizationSetup` seed a Mersenne Twister with 32 bits, so the
64-bit SessionID and the 128-bit `GenChallenge` both carry at most 32 bits of entropy —
`[V2G20-835]`, `[V2G20-2621]`, `[V2G20-698]`

**Version:** everest-core **2026.02.1** (`b61bb12b8`), `lib/everest/iso15118/`, and **unchanged on
everest-core `main`** (`ebcd36d`, checked 2026-08-11) — so unlike three of its neighbours in this
directory, this one has not been overtaken.

## The defect

Four places fill a security-relevant array with the same five lines:

```cpp
// src/iso15118/d20/session.cpp — Session::Session() and two sibling constructors, 86–116
std::random_device rd;
std::mt19937 generator(rd());
std::uniform_int_distribution<uint8_t> distribution(0x00, 0xff);

for (auto& item : id) {
    item = distribution(generator);
}
```

```cpp
// src/iso15118/d20/state/authorization_setup.cpp:44–50 — the Plug & Charge challenge
auto& pnc_auth_mode = res.authorization_mode.emplace<dt::PnC_ASResAuthorizationMode>();

std::random_device rd;
std::mt19937 generator(rd());
std::uniform_int_distribution<uint8_t> distribution(0x00, 0xff);

for (auto& item : pnc_auth_mode.gen_challenge) {
    item = distribution(generator);
}
```

`std::mt19937`'s scalar constructor takes **one 32-bit seed**. Everything the generator produces
afterwards is a pure function of those 32 bits, so the array's width does not matter: `SessionId` is
`std::array<uint8_t, 8>` and `GenChallenge` is `std::array<uint8_t, 16>`, and both take at most
2³² distinct values.

## What the standard asks for, and what this delivers

| value | width on the wire | entropy required | this code gives | short by |
|---|---|---|---|---|
| `SessionID` | 64 bits | **≥ 58** — `[V2G20-2621]` | ≤ 32 | 26 bits |
| PnC `GenChallenge` | 128 bits — `[V2G20-697]` | **≥ 120** — `[V2G20-698]` | ≤ 32 | 88 bits |

And separately from the two numbers, the generator itself:

- **`[V2G20-835]`** — a *shall*, and it is unconditional: wherever this document needs a random
  number, a state-of-the-art **cryptographically secure** RNG is to be used. The Mersenne Twister is
  not one; the *Random number generation* subclause's own explanation of the DRBG/NRBG distinction
  says in as many words that not every DRBG is cryptographically secure and that a DRBG's output is
  only as good as its seed.
  <br>**ISO 15118-2 carries the identical rule under the identical number**, `[V2G2-835]`, and so do
  the challenge's two: `[V2G2-697]`/`[V2G2-698]` are word-for-word `[V2G20-697]`/`[V2G20-698]`. So
  this is not a `-20` novelty that a `-2`-era codebase could not have known about — it is the rule
  `EvseV2G` in the same repository already satisfies. Only the SessionID's 58-bit floor is new in
  `-20`; `-2` asks merely for a fresh non-zero value (`[V2G2-750]`).
- **`[V2G20-2607]`** — the value used to seed a DRBG shall itself carry the minimum entropy the
  function needs. Here one 32-bit draw seeds a generator asked for a 120-bit value.
- **`[V2G20-2108]`** points the `GenChallenge` at those requirements explicitly; **`[V2G20-2110]`**
  and `[V2G20-697]` fix its length at 128 bits.
- **`[V2G20-2608]`** — such a value shall be used **only once**. See the next section for how often
  that stops being true.

## Measured, twice

### 1. Every SessionID your station ever gave us, recovered from the 32-bit seed space

This repository has recorded **49 distinct SessionIDs** from `Evse15118D20` — read out of your own
station's log line `New session created with session_id: 0x…`, across twenty interop runs between
2026-08-03 and 2026-08-11. None of those runs was looking at RNG quality; they were recorded for
other reasons and used here as found.

[`seedsearch`](../../tools/everest-rng-probe/seedsearch.cpp) walks all 2³² seeds, reproducing your
five lines verbatim and built with the same libstdc++, and reports which SessionIDs it reproduces:

```
searching all 2^32 mt19937 seeds for 49 SessionID(s)
threads: 16
  RECOVERED D3760BB5DA9E3DCE  <- mt19937 seed 0x03f4644f
  RECOVERED B477E13D7C7F2DED  <- mt19937 seed 0x096d0f3a
  RECOVERED DA70ACE0D2D25596  <- mt19937 seed 0x0b1bd639
  …
  RECOVERED 62A98E1503949A9F  <- mt19937 seed 0xf55a388b

searched 2^32 seeds in 639.0 s
recovered 49 of 49 SessionID(s)
```

**49 of 49, in eleven minutes on a laptop.** Each 64-bit SessionID your station issued is the output
of one 32-bit seed, and the seed can be found. This is a statement about the running binary rather
than about a reading of the source: the values came off the wire, the search reproduced them, and
none of the runs that recorded them was looking for this.

### 2. What 32 bits costs a 128-bit challenge

[`collide`](../../tools/everest-rng-probe/collide.cpp) draws N `GenChallenge`s your way and N from
`/dev/urandom`, and counts repeats:

```
drawing 262144 16-byte GenChallenges each way
expected repeats for a 32-bit value space: 8.0
expected repeats for a 128-bit value space: ~0

  libiso15118 (mt19937):       262144 draws, 262136 distinct, 8 repeated
  control (/dev/urandom):      262144 draws, 262144 distinct, 0 repeated
```

Eight predicted, eight observed, zero in the control — and 5 against 0 on a second run, since this
is a measurement rather than a fixed vector. A 16-byte challenge that repeats after ~2¹⁶
draws is the case `[V2G20-2608]` forbids, and the repeat is what the challenge exists to prevent:
`[V2G20-2565]` has the SECC check that the `GenChallenge` echoed in `AuthorizationReq` is the one it
sent, and that check is what makes a captured, signed `AuthorizationReq` useless the second time. It
stops being useless when the challenge comes round again.

**The challenge is not on your wire today**, and this report should say so before you check: your
`-20` module has PnC commented out — *"Currently Plug&Charge is not supported and ignored"* — so
`AuthorizationSetupRes` never carries a `GenChallenge` in the shipped configuration. The measurement
above is of the code that runs when it does, not of your traffic. That makes it the cheaper of the two
to fix, not the less real: it is four lines away from being live, and the SessionID beside it already
is.

**A bound worth stating with it, so the severity is not overstated.** For the SessionID this is a
conformance shortfall and a collision risk rather than a session hijack, for two reasons that are
both yours: your `-20` resume is bound to the vehicle certificate from the TLS handshake
(`[V2G20-2545]`, the worked example at `[V2G20-2677]`), which you implement; and a conformant `-20`
connection is TLS 1.3, so the identifier is neither observable nor injectable by a network attacker.
Both of those are load-bearing, and the second is weaker on your station than on the standard —
`[V2G20-2400]` and the TLS 1.2 path are [a separate filing](everest-d20-client-auth.md). The
`GenChallenge` case is the one with teeth.

## Your own ISO 15118-2 module does this correctly

Worth knowing before deciding how this happened, because it is the same repository and the same two
values:

```cpp
// modules/EVSE/EvseV2G/tools.cpp:38
int generate_random_data(void* dest, size_t dest_len) {
    fd = open("/dev/urandom", O_RDONLY);
    …
}
```

- `iso_server.cpp:581` — the ISO 15118-2 SessionID, 8 bytes from `/dev/urandom`
- `iso_server.cpp:1114` — the ISO 15118-2 `GenChallenge`, `GEN_CHALLENGE_SIZE` bytes from the same
- `din_server.cpp:353` — the DIN 70121 SessionID, likewise

Three values in that module come from the kernel CSPRNG at full width. The two in `libiso15118` do
not.

## Suggested fix

The values are 8 and 16 bytes drawn once per session; nothing here needs a generator at all. Three
shapes, and which one belongs in your tree is yours to choose:

1. **OpenSSL, which `libiso15118` already links for TLS** — `RAND_bytes(id.data(), id.size())`,
   with the return value checked and the session failed if it is not 1. One line per site.
2. **`std::random_device` directly**, without the Twister in between:
   `std::generate(id.begin(), id.end(), [&rd] { return static_cast<uint8_t>(rd()); })`. Simplest,
   but the C++ standard permits a deterministic `random_device`, so it is worth pinning to the
   platforms you support.
3. **The function you already have** — `EvseV2G`'s `generate_random_data()`, lifted into a shared
   place. That also gets the two modules to one answer.

Any of them is also *less* code and faster than seeding 2,5 kB of Mersenne Twister state to draw
eight bytes.

**A smaller thing in the same five lines, worth fixing while you are there:**
`std::uniform_int_distribution<uint8_t>` is undefined behaviour by the letter of the standard — the
template is specified for the `short`/`int`/`long`/`long long` families and their unsigned
counterparts, and `uint8_t` is none of them. libstdc++ accepts it; MSVC static-asserts. It is not
what this report is about, but it disappears with any of the fixes above.

## Where else this lives — and why that turned out not to matter

`src/iso15118/d20/session.cpp` and `src/iso15118/d20/state/authorization_setup.cpp` are
**byte-identical** — same SHA-256 — in `EVerest/everest-core` at `lib/everest/iso15118/` and in
standalone `EVerest/libiso15118` at `5c81c92`, and this report was drafted expecting the same
send-it-twice trap as `power_delivery.cpp` in [the contactor filing](everest-iso20-ac-contactor-latch.md).

**It is not a trap, because the standalone repository is not maintained** (checked 2026-08-11).
everest-core's `lib/everest/iso15118/` is the live tree; the mirror is stale, and that it agrees
byte-for-byte here says only that nobody has touched either file. **File once, against everest-core.**
The genuine send-it-twice case in this directory is
[`josev-iso20-pki-curve.md`](josev-iso20-pki-curve.md), where both trees are alive.

## Context: four stacks, three answers

Because it says the requirement is neither obscure nor uniformly met:

| stack | SessionID | PnC GenChallenge |
|---|---|---|
| Josev (SwitchEV), `-2` · `-20` · DIN | `secrets.token_bytes(8)` — CSPRNG, 64 bits | `secrets.token_bytes(16)` — 128 bits |
| EVerest `EvseV2G` (`-2`, DIN) | `/dev/urandom`, 8 bytes | `/dev/urandom` |
| **EVerest `libiso15118` (`-20`)** | **mt19937, 32-bit seed** | **mt19937, 32-bit seed** |
| eVDriveFlow (`-20`) | `secrets.randbelow(10⁸)` as 8 ASCII digits — **~26,6 bits** | *(no PnC)* |

Two of the four get it right. The eVDriveFlow row is a different defect against the same requirement
— a correct generator used over a 10⁸ range, rather than a full range filled by a generator that is
not cryptographic — and is [filed separately](evdriveflow-session-id-entropy.md).

**And ours.** Our own stations draw both values from the .NET CSPRNG
(`RandomNumberGenerator.GetBytes`) in both protocols, which is why this report has no *we had it
too* paragraph — but the reason we looked at all is that we had just finished fixing a different
SessionID gap of our own, so we are not reporting from higher ground.

---

## Before sending

- [x] **Reproduce it yourself, against the running binary.** 49 of 49 SessionIDs recovered from the
      2³² seed space, all of them read out of your station's own log during earlier interop runs.
      The search program copies your five lines rather than modelling them and is built with the same
      libstdc++.
- [x] **Control the measurement.** The collision count has a `/dev/urandom` arm at the same N, and
      `seedsearch` exits non-zero if any target fails to recover — so a toolchain mismatch cannot
      pass as a strong RNG.
- [x] **Check the source at the current head.** Present in everest-core 2026.02.1, and **re-checked
      2026-08-11 against everest-core `main`** (`ebcd36d`): `std::mt19937 generator(rd())` unchanged.
      Also present in standalone `libiso15118` at `5c81c92`, which is unmaintained and proves nothing
      either way.
- [x] **Say where the requirement is not exotic.** Two of the three other stacks draw both values
      from a CSPRNG at full width, and one of the two is your own `EvseV2G`.
- [ ] **Decide one issue or two.** The SessionID and the `GenChallenge` have one cause and one fix,
      so one issue is defensible here — unlike the pairs this directory usually splits. If you split
      them, the `GenChallenge` is the one with a replay consequence and the SessionID the one with a
      number in a requirement.
- [x] **File it in the right tree — answered 2026-08-11: everest-core, once.** The standalone
      `libiso15118` carries the same bytes but is not maintained, so there is no second tree to
      open against and nothing to ask.
- [ ] **Post under your own name, in your own words.**
