# EVerest's EV is Josev, and what that costs a column

**Matrix cell:** The counterparty columns, read together

Back to the [interop matrix](../../README.md).

---

**Their EV is Josev** — `PyEvJosev` wraps EVerest's fork of the same codebase the Josev column tests.
So the codec flips with the direction (cbV2G forward, EXIficient reverse), and a `←SECC` run here is
largely a re-run of that column — which is why the reverse direction was spent only on **MCS**, and why
-2 reverse against EVerest has deliberately never been run.

**That premise has an end date, and it is a pull request.** [everest-core#3031](https://github.com/EVerest/everest-core/pull/3031)
replaces `PyEvJosev` in every config with `Ev15118` — an EV written in C++ on `libiso15118`, so **cbV2G in
both directions** and a state machine that is EVerest's own rather than Josev's. Run against its head on
2026-11-06: six of seven scenarios complete, the ≈532 ms charge-loop turnaround is gone, and `-2` DC stops on
an optional element our station never sends
([details](everest-ev15118-reverse.md)). Once it merges, a `←SECC` run against EVerest measures
something the Josev column does not. *(The paragraph above also predates 2026-08-15, when `-2` AC did run
in reverse against `PyEvJosev` — the matrix row carries it.)*
