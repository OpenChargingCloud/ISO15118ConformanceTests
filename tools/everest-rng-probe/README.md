# everest-rng-probe — how many bits are really in a SessionID

Two small C++ programs that measure the entropy of the random values EVerest's `libiso15118`
puts into an ISO 15118-20 session, rather than arguing about them.

Their `d20::Session` constructor and their `AuthorizationSetup` handler both fill an array like
this:

```cpp
std::random_device rd;
std::mt19937 generator(rd());
std::uniform_int_distribution<uint8_t> distribution(0x00, 0xff);
for (auto& item : id) { item = distribution(generator); }
```

`std::mt19937`'s scalar constructor takes **one 32-bit seed**, so the array — 8 bytes for the
SessionID, 16 for the Plug & Charge `GenChallenge` — is a pure function of 32 bits however wide it
is. Both programs below copy those lines verbatim rather than modelling them, and both are meant to
be built with the same libstdc++ that built the station under test: then a result is a statement
about their binary, not about our reading of their source.

## `seedsearch` — recover the seed from a SessionID they emitted

```bash
g++ -O3 -march=native -pthread -o seedsearch seedsearch.cpp
./seedsearch --bench        # single-thread throughput, to size the run
./seedsearch ids.txt        # one 16-hex-digit SessionID per line
```

Walks all 2^32 seeds and reports which of the given SessionIDs it reproduces. ~1,3 µs per seed, so
about 94 core-minutes for the whole space — six minutes on sixteen cores.

**Exit code 0 only if every target was recovered.** A toolchain whose `std::uniform_int_distribution`
differs from the station's would otherwise report "not found", which looks exactly like a strong
RNG; this makes that case fail loudly instead.

## `collide` — what 32 bits costs a 128-bit value

```bash
g++ -O3 -march=native -o collide collide.cpp
./collide 262144
```

Draws N `GenChallenge`s their way and N from `/dev/urandom`, and counts repeats in each. With a
32-bit value space the birthday bound puts the expected number of repeats at `N² / 2^33`; at
N = 262 144 that is 8. A 128-bit value would repeat at ~2^60 draws, so the control is the
comparison that makes the number mean something.

Uses the real `std::random_device`, so the count varies run to run around its expectation. That is
deliberate — it is a measurement, not a fixed vector.

## What it was used for

[`2026-08-11-everest-d20-rng-entropy`](../../docs/interop-runs/2026-08-11-everest-d20-rng-entropy/notes.md)
— every SessionID this repository had on record from their station, recovered from the 32-bit seed
space — and the filing it produced,
[`everest-d20-rng-entropy.md`](../../docs/reports/everest-d20-rng-entropy.md).
