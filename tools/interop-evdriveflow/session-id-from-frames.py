#!/usr/bin/env python3
"""
Read eVDriveFlow's SessionIDs straight out of recorded `SessionSetupRes` bytes.

`evdriveflow-session-id-entropy.md` says their `generate_random_session_id()` returns
`str(secrets.randbelow(100000000)).zfill(8).encode('ascii')` — eight ASCII digits in the 8-byte
SessionID field, so ~26,6 bits where `[V2G20-2621]` asks for 58. This is how that claim was taken off
the wire instead of out of the source.

**No EXI decoder is needed.** In a `-20` `SessionSetupRes` the SessionID sits at a **one-bit** offset
inside the payload, so shifting the whole payload left by one bit makes the eight bytes appear as
literal ASCII digits, which a regex then finds. That is a happy accident of their choice of encoding:
a SessionID of arbitrary bytes would not be readable this way, and the fact that it *is* readable is
itself the finding.

    python3 session-id-from-frames.py 'docs/interop-runs/2026-08-15-edf-session-id-entropy/sessions/*.log'

Written for that run; see its notes for what 24 of 24 showed.
"""

import glob
import re
import sys


def shift_left_one_bit(data: bytes) -> bytes:
    return bytes(((data[i] << 1) | (data[i + 1] >> 7)) & 0xFF for i in range(len(data) - 1))


def session_ids(path: str) -> list[str]:
    found = []
    text = open(path, encoding='utf-8', errors='replace').read()
    for _, payload in re.findall(r'\[(\d+)\] SessionSetupRes[^\n]*\n\s+([0-9a-f]+)', text):
        shifted = shift_left_one_bit(bytes.fromhex(payload))
        found += [m.group().decode() for m in re.finditer(rb'[0-9]{8}', shifted)]
    return found


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 2

    everything: list[str] = []
    for pattern in sys.argv[1:]:
        for path in sorted(glob.glob(pattern, recursive=True)):
            for value in session_ids(path):
                print(f'{path}: {value}')
                everything.append(value)

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
