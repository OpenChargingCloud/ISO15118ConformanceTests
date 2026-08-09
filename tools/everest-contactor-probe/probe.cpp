// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Runs the claim in docs/reports/everest-iso20-ac-contactor-latch.md instead of reading it.
//
// It compiles against EVerest's *own* control_event.hpp, so the class under test is theirs and not a
// retyped copy of it, and it uses their own warning set — libiso15118 is built with
// "-Wall;-Wextra;-Wno-unused-function;-Werror" (lib/everest/iso15118/CMakeLists.txt:53). That the
// assignment below survives those flags is half the finding: this is the shape of mistake a strict
// warning set does not catch.
//
// Exit 0 means the defect is still there.

#include <iso15118/d20/control_event.hpp>

#include <cstdio>

using iso15118::d20::ClosedContactor;

int main() {

    // The board-support module reporting that the AC contactor did NOT close.
    // Evse15118D20/charger/ISO15118_chargerImpl.cpp:829 constructs exactly this from the
    // ac_contactor_closed(status) command, with status == false.
    const ClosedContactor did_not_close{false};

    // What Context::get_control_event<ClosedContactor>() hands to the state: a pointer, non-null
    // precisely because the event is present. Its value says whether the contactor closed.
    const ClosedContactor* control_data = &did_not_close;

    // power_delivery.cpp:53, verbatim in effect:  ac_connector_closed = control_data;
    // ac_connector_closed is `bool` (power_delivery.hpp:21), so this is a pointer-to-bool
    // conversion — true because the pointer is non-null, never mind what it points at.
    const bool as_written = control_data;

    // What operator bool() on ClosedContactor exists for (control_event.hpp:80-82).
    const bool as_intended = *control_data;

    std::printf("EVerest libiso15118 — PowerDelivery::feed, ClosedContactor{false}\n\n");
    std::printf("  as written    ac_connector_closed = control_data    -> %s\n",
                as_written ? "true  (contactor treated as CLOSED)" : "false");
    std::printf("  as intended   ac_connector_closed = *control_data   -> %s\n",
                as_intended ? "true" : "false (contactor open, as reported)");
    std::printf("\n");

    if (as_written && !as_intended) {
        std::printf("  DEFECT PRESENT: a contactor reported open latches the state to closed,\n");
        std::printf("  the \"Waiting until the contactor is closed\" branch is unreachable, and\n");
        std::printf("  PowerDelivery answers OK and enters AC_ChargeLoop.\n");
        return 0;
    }

    std::printf("  Not reproduced — the conversion behaves as intended. Has it been fixed?\n");
    return 1;
}
