#!/usr/bin/env python3
"""
Read a tux-evse scenario file and say which of its expectations our station can possibly satisfy.

Their injector compares each response against an `expect` block lifted from a packet capture, so the block
holds *that* charger's answers -- `"id": "DE*PNX*E12345*1"` is the EVSE ID of the charger somebody captured,
not a protocol constant. Against our station those comparisons fail on a session that is otherwise perfect.

Knowing which ones before the run turns a wall of red into a short list worth reading. Everything this
prints is about *their* file; nothing here talks to anything.

Usage:  scenario-expectations.py <scenario.json> [--verbose]
        scenario-expectations.py --selftest
"""

import json
import sys

# Fields of an `expect` block that describe the protocol rather than the station. Anything else is the
# captured charger's own choice, and a foreign station is entitled to answer differently.
PROTOCOL_FIELDS = {
    "rcode",     # the ResponseCode -- ours should match, and a mismatch here is a real finding
    "tagid",     # which message came back
    "proto",     # iso2 / din / iso20
    "msgid",     # position in their own message table
    "stamp",
}

# ...with one caveat worth spelling out: `rcode` is only comparable when the session is going the same way.
# A station that legitimately says "ongoing" where the capture said "finished" is not wrong.


def scenarios(document):
    """Their two shapes: a binding wrapper (shipped scenarios) or a bare pcap-iso15118 output."""
    if isinstance(document, dict):
        if "scenarios" in document:
            yield from document["scenarios"]
        for binding in document.get("binding", []):
            yield from binding.get("scenarios", [])


def report(document, verbose=False):

    lines = []
    station_specific = 0
    total = 0

    for scenario in scenarios(document):

        transactions = scenario.get("transactions", [])
        lines.append(f"## {scenario.get('uid', '<unnamed>')}"
                     f"  ({len(transactions)} transactions, timeout {scenario.get('timeout', '?')})")

        for transaction in transactions:

            verb   = transaction.get("verb", "?")
            expect = transaction.get("expect")

            if transaction.get("injector_only"):
                lines.append(f"  {verb:34} (injector only -- no response compared)")
                continue

            if not isinstance(expect, dict):
                lines.append(f"  {verb:34} (no expectation)")
                continue

            total += 1
            theirs = sorted(k for k in expect if k not in PROTOCOL_FIELDS)

            if theirs:
                station_specific += 1
                detail = ", ".join(f"{k}={json.dumps(expect[k])}" for k in theirs) if verbose \
                         else ", ".join(theirs)
                lines.append(f"  {verb:34} station-specific: {detail}")
            else:
                lines.append(f"  {verb:34} rcode/tagid only -- comparable as-is")

        lines.append("")

    lines.append(f"{station_specific} of {total} compared responses carry at least one field the captured")
    lines.append("charger chose for itself. Those mismatches are expected against our station and are not")
    lines.append("findings; read them for the shape of the difference, not for the fact of it.")
    lines.append("")
    lines.append("No compaction mode changes this: `compact` acts at pcap-import time, and the player aborts")
    lines.append("the whole scenario at the first expect mismatch (confirmed on the wire, 2026-08-06).")
    lines.append("Run scenario-relax.py to reduce the expects to the protocol fields before an interop run.")

    return "\n".join(lines), station_specific, total


def selftest():
    """The traversal, over both documented shapes. Not a check of their format -- a check that this
    script survives meeting it."""

    wrapped = {"binding": [{"scenarios": [{"uid": "s1", "timeout": 748, "transactions": [
        {"verb": "iso2:sdp_evse_req", "injector_only": True},
        {"verb": "iso2:session_setup_req",
         "expect": {"id": "DE*PNX*E12345*1", "rcode": "new_session", "tagid": "session_setup_res",
                    "proto": "iso2", "msgid": 1}},
        {"verb": "iso2:session_stop_req", "expect": {"rcode": "ok", "tagid": "session_stop_res"}},
    ]}]}]}

    bare = {"uid": "pcap", "scenarios": [{"uid": "scenario-1", "transactions": [
        {"verb": "session_setup_req", "expect": {"id": "[00]", "rcode": "ok", "tagid": "session_setup_res"}},
    ]}]}

    text, specific, total = report(wrapped)
    assert (specific, total) == (1, 2), (specific, total)
    assert "station-specific: id" in text, text
    assert "rcode/tagid only" in text, text
    assert "injector only" in text, text

    _, specific, total = report(bare)
    assert (specific, total) == (1, 1), (specific, total)

    assert list(scenarios({})) == []
    assert list(scenarios([])) == []

    print("selftest: ok")


if __name__ == "__main__":

    if "--selftest" in sys.argv:
        selftest()
        sys.exit(0)

    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if len(args) != 1:
        print(__doc__.strip(), file=sys.stderr)
        sys.exit(2)

    with open(args[0], encoding="utf-8") as handle:
        document = json.load(handle)

    text, _, _ = report(document, verbose="--verbose" in sys.argv)
    print(text)
