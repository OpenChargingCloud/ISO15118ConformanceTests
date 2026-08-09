# everest-contactor-probe — running the claim in the contactor report instead of reading it

```bash
bash tools/everest-contactor-probe/build.sh                                  # ~/everest/everest-core
EVEREST_CORE=/path/to/everest-core bash tools/everest-contactor-probe/build.sh
```

**Exit 0 means the defect is still there.** As with
[`cbv2g-defect-probe`](../cbv2g-defect-probe/README.md), a *failure* is the good news: something was
fixed upstream and the filing can be closed.

Compiles [`probe.cpp`](probe.cpp) against EVerest's **own** `iso15118/d20/control_event.hpp`, with
EVerest's **own** warning set — `-Wall -Wextra -Wno-unused-function -Werror`, from
`lib/everest/iso15118/CMakeLists.txt:53`. Both halves are deliberate:

- the class under test is theirs, not a retyped copy that might differ in the one detail that matters;
- that the assignment survives `-Werror` is half the finding. An implicit pointer-to-`bool` conversion
  in an assignment is well-formed C++ and neither GCC nor Clang diagnoses it, so a strict warning set
  gives no cover here.

Needs a checkout of `everest-core` and a C++17 compiler. It reads headers only — nothing is built,
started or connected to, and nothing in `dotnet test` touches it.

## What it prints

```
everest-core: /home/ahzf/everest/everest-core
commit:       b61bb12  2026.02.1
the report cites b61bb12  2026.02.1

compiled clean under -Wall -Wextra -Werror

EVerest libiso15118 — PowerDelivery::feed, ClosedContactor{false}

  as written    ac_connector_closed = control_data    -> true  (contactor treated as CLOSED)
  as intended   ac_connector_closed = *control_data   -> false (contactor open, as reported)

  DEFECT PRESENT: a contactor reported open latches the state to closed,
  the "Waiting until the contactor is closed" branch is unreachable, and
  PowerDelivery answers OK and enters AC_ChargeLoop.
```

## What it does not show

The mechanism, not the consequence. It proves that `ClosedContactor{false}` reaching
`power_delivery.cpp:53` sets `ac_connector_closed` to `true`; it does not run a station, so it does not
show a charger answering `PowerDeliveryRes(OK)` on the wire. That reproduction is the first unticked
item in the report's *Before sending* checklist and is the reason the report is not ready to post.

Full account: [`docs/reports/everest-iso20-ac-contactor-latch.md`](../../docs/reports/everest-iso20-ac-contactor-latch.md).
