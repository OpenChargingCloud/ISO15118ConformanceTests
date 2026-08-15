#!/usr/bin/env python3
"""
Read `-20` SessionIDs straight out of recorded frame bytes — theirs, and ours.

`evdriveflow-session-id-entropy.md` says their `generate_random_session_id()` returns
`str(secrets.randbelow(100000000)).zfill(8).encode('ascii')` — eight ASCII digits in the 8-byte
SessionID field, so ~26,6 bits where `[V2G20-2621]` asks for 58. This is how that claim was taken off
the wire instead of out of the source.

**No EXI decoder is needed.** In a `-20` message the SessionID sits at a **one-bit** offset inside the
payload, so shifting the whole payload left by one bit makes their eight bytes appear as literal ASCII
digits, which a regex then finds. That is a happy accident of their choice of encoding: a SessionID of
arbitrary bytes would not be readable this way, and the fact that it *is* readable is itself the
finding.

    python3 session-id-from-frames.py 'docs/interop-runs/2026-08-15-edf-session-id-entropy/sessions/*.log'

**`--requests` is the other half, and it exists for a different reason.** `[V2G20-460]` is measured by
sending a *wrong* SessionID, and against a station that never compares one, a correct probe and a
broken probe produce the same complete session. So the instrument has to be checked from the bytes:
this mode reads the id out of every request our EV sent and says whether it was the station's, all
zeroes, or something else — the `--expect` value.

    python3 session-id-from-frames.py --requests --expect deadbeefdeadbeef '/tmp/edf-460/*/*.frames.log'

Written for the 2026-08-15 runs; see their notes for what 24 of 24 and three arms showed.
"""

import glob
import re
import sys


def shift_left(data: bytes, bits: int) -> bytes:
    """The buffer shifted left by `bits` (0-7), one byte shorter."""
    if bits == 0:
        return data
    return bytes(((data[i] << bits) | (data[i + 1] >> (8 - bits))) & 0xFF
                 for i in range(len(data) - 1))


def frames(path: str, direction: str) -> list[tuple[str, bytes]]:
    """(message name, payload) for one direction of a recorded session."""
    text  = open(path, encoding='utf-8', errors='replace').read()
    parts = text.split('## ')
    block = next((p for p in parts if p.startswith(direction)), '')
    return [(name, bytes.fromhex(payload))
            for name, payload in re.findall(r'\[\d+\] (\w+)[^\n]*\n\s+([0-9a-f]+)', block)]


def station_ids(path: str) -> list[str]:
    found = []
    for name, payload in frames(path, 'station -> EV'):
        if name != 'SessionSetupRes':
            continue
        found += [m.group().decode() for m in re.finditer(rb'[0-9]{8}', shift_left(payload, 1))]
    return found


def locate(payload: bytes, wanted: bytes) -> tuple[int, int] | None:
    """Where `wanted` sits in `payload`, as (bit offset, byte index), searching every alignment."""
    for bits in range(8):
        at = shift_left(payload, bits).find(wanted)
        if at >= 0:
            return bits, at
    return None


def read_requests(path: str, expect: bytes | None) -> int:
    """What our EV put in the header of every request after SessionSetupReq. Returns frames read."""

    issued = station_ids(path)
    issued_bytes = issued[0].encode() if issued else None

    print(f'{path}')
    print(f'  station issued: {issued[0] if issued else "(no SessionSetupRes in this log)"}')

    seen = 0
    for name, payload in frames(path, 'EV -> station'):

        # The two the rule excludes, and the one that is not an ISO 15118-20 message at all.
        if name in ('SupportedAppProtocolReq', 'SessionSetupReq'):
            continue

        seen += 1
        verdict = 'not found at any bit alignment'

        for label, wanted in (('the station\'s own id', issued_bytes),
                              ('eight zero bytes',     bytes(8)),
                              ('--expect',             expect)):
            if wanted is None:
                continue
            where = locate(payload, wanted)
            if where:
                bits, at = where
                verdict = f'{label} ({wanted.hex()}) at bit offset {bits}, byte {at}'
                break

        print(f'  {name:34} {verdict}')

    return seen


def main() -> int:

    args    = [a for a in sys.argv[1:] if not a.startswith('--')]
    reqs    = '--requests' in sys.argv
    expect  = None

    if '--expect' in sys.argv:
        raw = sys.argv[sys.argv.index('--expect') + 1]
        expect = bytes(8) if raw == 'zero' else bytes.fromhex(raw)
        args = [a for a in args if a != raw]

    if not args:
        print(__doc__)
        return 2

    paths = [p for pattern in args for p in sorted(glob.glob(pattern, recursive=True))]

    if reqs:
        total = sum(read_requests(p, expect) for p in paths)
        print(f'\n{total} request(s) read across {len(paths)} session(s)')
        print('A station that never compares the id answers all of them the same way — which is why this '
              'says what we sent, and their log says what they did with it.')
        return 0 if total else 1

    everything = [v for p in paths for v in station_ids(p)]
    for p in paths:
        for v in station_ids(p):
            print(f'{p}: {v}')

    if not everything:
        print('no SessionSetupRes payloads found')
        return 1

    values = [int(v) for v in everything]
    print(f'\n{len(everything)} SessionID(s), {len(set(everything))} distinct')
    print(f'every one eight ASCII digits: {all(len(v) == 8 and v.isdigit() for v in everything)}')
    print(f'min {min(values)}  max {max(values)}   (the range is 0 .. 99999999)')
    print(f'entropy of the alphabet: 8 x log2(10) = 26.6 bits; [V2G20-2621] asks for 58')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
