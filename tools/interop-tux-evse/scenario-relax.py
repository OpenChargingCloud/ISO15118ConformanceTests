#!/usr/bin/env python3
"""
Reduce a tux-evse scenario's `expect` blocks to their protocol fields, for a run against OUR station.

Their injector compares each response against an `expect` block lifted from a packet capture, the
comparison is `Jequal::Partial` (every expected field must match), and a mismatch aborts the whole
scenario -- `job_scenario_exec` propagates the first `Fail` (`injector-binding-rs/src/controller.rs`).
So against a station that is not the captured charger, the shipped scenario dies at the first field
that station chooses for itself: the 2026-08-06 run stopped at `SessionSetupRes.id`, three messages in.

This keeps the comparison honest instead of deleting it: `rcode`, `tagid`, `proto`, `msgid`, `stamp`
survive -- their injector still verifies *which* message came back and *with which response code* --
and the captured charger's identity, schedules and measurements stop being treated as expectations.
The classification is `scenario-expectations.py`'s; run that first to see what a file will lose.

A transaction with NO `expect` block is not checked at all (`expects.count() == 0` short-circuits to
`Done`), which is why the blocks are reduced rather than removed.

`--autorun` also sets `binding[0].autorun = 1` -- without it their binder loads the scenario and
waits for a devtools click (workaround 4 of the 2026-08-01 run, still true at HEAD).

Usage:  scenario-relax.py <scenario.json> <out.json> [--autorun]
        scenario-relax.py --selftest
"""

import json
import sys

# Keep in step with scenario-expectations.py, which prints what this script would strip.
PROTOCOL_FIELDS = {"rcode", "tagid", "proto", "msgid", "stamp"}


def relax(document, autorun=False):
    """Returns (document, stripped-field-count). Mutates and returns the given document."""

    stripped = 0

    def scenarios():
        if isinstance(document, dict):
            yield from document.get("scenarios", [])
            for binding in document.get("binding", []):
                yield from binding.get("scenarios", [])

    if autorun:
        for binding in document.get("binding", []) if isinstance(document, dict) else []:
            binding["autorun"] = 1

    for scenario in scenarios():
        for transaction in scenario.get("transactions", []):
            expect = transaction.get("expect")
            if isinstance(expect, dict):
                kept = {k: v for k, v in expect.items() if k in PROTOCOL_FIELDS}
                stripped += len(expect) - len(kept)
                transaction["expect"] = kept

    return document, stripped


def selftest():
    """Both of their document shapes, the field split, and the no-expect passthrough."""

    wrapped = {"binding": [{"autorun": 0, "scenarios": [{"transactions": [
        {"verb": "iso2:sdp_evse_req", "injector_only": True},
        {"verb": "iso2:session_setup_req",
         "expect": {"id": "DE*PNX*E12345*1", "rcode": "new_session", "tagid": "session_setup_res",
                    "proto": "iso2", "msgid": 1}},
        {"verb": "iso2:authorization_req"},
    ]}]}]}

    document, stripped = relax(wrapped, autorun=True)
    assert stripped == 1, stripped
    assert document["binding"][0]["autorun"] == 1
    expect = document["binding"][0]["scenarios"][0]["transactions"][1]["expect"]
    assert expect == {"rcode": "new_session", "tagid": "session_setup_res",
                      "proto": "iso2", "msgid": 1}, expect
    assert "expect" not in document["binding"][0]["scenarios"][0]["transactions"][2]

    bare = {"scenarios": [{"transactions": [
        {"verb": "session_setup_req", "expect": {"id": "[00]", "rcode": "ok"}}]}]}
    document, stripped = relax(bare)
    assert stripped == 1
    assert document["scenarios"][0]["transactions"][0]["expect"] == {"rcode": "ok"}

    print("selftest: ok")


if __name__ == "__main__":

    if "--selftest" in sys.argv:
        selftest()
        sys.exit(0)

    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if len(args) != 2:
        print(__doc__.strip(), file=sys.stderr)
        sys.exit(2)

    with open(args[0], encoding="utf-8") as handle:
        document = json.load(handle)

    document, stripped = relax(document, autorun="--autorun" in sys.argv)

    with open(args[1], "w", encoding="utf-8") as handle:
        json.dump(document, handle, indent=2)

    print(f"{args[1]}: {stripped} station-specific expect field(s) stripped"
          + (", autorun=1" if "--autorun" in sys.argv else ""))
