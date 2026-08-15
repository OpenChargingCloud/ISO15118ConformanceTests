#!/usr/bin/env python3
"""
Split a Josev log into the three costs of one message exchange.

Josev logs three lines per message at INFO, each timestamped to the millisecond, and between them they
bracket everything a peer sees as "the time that side took":

    Sent <X>                    the request goes out
    Decoded message (...)       the answer has arrived and come back from the EXI codec
    Message to encode (...)     the state machine has decided what to send next
    Sent <Y>                    the next message goes out

so:

    SENT   -> DECODED   wait for the peer + socket read + EXI *decode*
    DECODED-> to-encode the state machine's own handling
    to-enc -> SENT      EXI *encode* + socket write

Both codec halves cross a py4j gateway into a JVM (`ExificientEXICodec`), which is why the split is
worth taking: it is the difference between "their codec is slow" and "their codec is not the cost".
Written for `josev-iso20-evcc-charge-loop-pacing.md`, whose §4 asked for exactly this and had to be
answered with a measurement rather than a suspicion — see
`docs/interop-runs/2026-08-15-josev-evcc-pacing-localized/`.

It reads any Josev-style log, either role: their SECC container, their EVCC container, or their EVCC
inside EVerest's `PyEvJosev` — provided the root logger is at INFO. It is not at INFO inside that
module, which is itself part of the finding.

    python3 message-timing.py <log> [<log> ...]
    python3 message-timing.py --repeat 'Sent AC_ChargeLoopReq' <log>    # turnaround between two of a kind
"""

import argparse
import datetime as dt
import re
import statistics as st

TIMESTAMP = re.compile(r'(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3})')


def stamp(line):
    match = TIMESTAMP.search(line)
    return dt.datetime.strptime(match.group(1), '%Y-%m-%d %H:%M:%S,%f') if match else None


def milliseconds(a, b):
    return (b - a).total_seconds() * 1000


def summarise(label, values):
    if not values:
        return f'  {label:<36} (none)'
    ordered = sorted(values)
    return (f'  {label:<36} n={len(ordered):3d}  min={ordered[0]:7.1f}  '
            f'median={st.median(ordered):7.1f}  max={ordered[-1]:7.1f} ms')


def split(path):
    wait, handling, encoding = [], [], []
    last_sent = last_decoded = last_to_encode = None

    for line in open(path, encoding='utf-8', errors='replace'):
        at = stamp(line)
        if at is None:
            continue
        if 'Message to encode' in line:
            if last_decoded:
                handling.append(milliseconds(last_decoded, at))
                last_decoded = None
            last_to_encode = at
        elif ' Sent ' in line:
            if last_to_encode:
                encoding.append(milliseconds(last_to_encode, at))
                last_to_encode = None
            last_sent = at
        elif 'Decoded message' in line:
            if last_sent:
                wait.append(milliseconds(last_sent, at))
                last_sent = None
            last_decoded = at

    return wait, handling, encoding


def repeats(path, needle):
    times = [stamp(line) for line in open(path, encoding='utf-8', errors='replace')
             if needle in line and stamp(line)]
    return [milliseconds(a, b) for a, b in zip(times, times[1:])]


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('logs', nargs='+')
    parser.add_argument('--repeat', metavar='LINE',
                        help='also report the interval between successive lines containing LINE')
    args = parser.parse_args()

    for path in args.logs:
        wait, handling, encoding = split(path)
        print(f'\n=== {path} ===')
        print(summarise('SENT -> DECODED (peer + read + decode)', wait))
        print(summarise('DECODED -> to-encode (handling)', handling))
        print(summarise('to-encode -> SENT (encode + write)', encoding))
        if wait and handling and encoding:
            total = st.median(wait) + st.median(handling) + st.median(encoding)
            print(f'  {"sum of the three medians":<36}          {total:7.1f} ms')
        if args.repeat:
            print(summarise(f'{args.repeat!r} to the next', repeats(path, args.repeat)))


if __name__ == '__main__':
    main()
