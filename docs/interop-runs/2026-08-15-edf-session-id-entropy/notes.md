# 2026-08-15 — eVDriveFlow's SessionID, on the wire, 24 times

[The entropy filing](../../reports/evdriveflow-session-id-entropy.md) reads one line of their source and
says *"eight ASCII digits, so 26,6 bits where `[V2G20-2621]` asks for 58"*. Its first checklist line
offered a choice: *"Run it against your station **if you want the number on the wire rather than in the
source**."* This takes it. One session would have been enough for the shape; twenty-four were taken
because the claim is about a **range**, and a range needs more than one point.

| | |
|---|---|
| Counterparty | [eVDriveFlow](https://github.com/EDF-Lab/eVDriveFlow) **`60249c3`** (2023-04-17), still `origin/main` — their SECC, plain TCP |
| Ours | our EVCC through `EvDriveFlowInteropTests`, 24 consecutive sessions against one station |
| Outcome | **24 of 24 SessionIDs are eight ASCII digits**, 24 distinct, every one inside `[0, 10⁸)` |

## What their station issued

```
50475261  66517944  96751133  26079747  40482773  76881948  19193724  86292746
75776846  40738080  17203515  41960978  67275833  52324782  64493806  58476268
47267897  35647279  20819696  30812432  84089082  92122701  47445612  39261178
```

Minimum **17 203 515**, maximum **96 751 133**, no repeats. Exactly what
`str(secrets.randbelow(100000000)).zfill(8).encode('ascii')` produces, and nothing a 64-bit field would
produce if it were being filled as one.

**The mechanism, stated as the numbers show it:** the SessionID field is eight bytes wide and full, but
every byte is constrained to `0x30`–`0x39`. Ten symbols per byte is log₂10 ≈ 3,32 bits, so the field
carries 8 × 3,32 = **26,6 bits** — the field is not short, its *alphabet* is. That distinction is the
whole report, and it is visible in the octets rather than argued from them.

**One honest gap in the sample:** all 24 draws happen to be ≥ 10⁷, so none of them exercises `zfill`'s
leading zero. That is expected — only one draw in ten falls below — and `zfill(8)` guarantees the width
regardless. Worth saying because *"always eight digits"* is a claim about the code here, not something
these 24 samples prove on their own.

## No EXI decoder was needed, and that is not a convenience

The SessionID sits at a **one-bit** offset inside the `SessionSetupRes` payload, so shifting the whole
payload left by one bit makes the eight bytes appear as literal ASCII digits that a regex finds:

```
payload   ...041b9c1c1c9c1b9b1a8ecfab8d3062...
<< 1 bit  ...  37 38 38 39 38 37 36 35  ...   =  "78898765"
```

That is [`session-id-from-frames.py`](../../../tools/interop-evdriveflow/session-id-from-frames.py), and
it works on any recorded session — including the one already in this repository from **2026-08-01**,
whose SessionID reads `78898765`. So the first data point predates the report by ten days and nobody had
looked.

**A SessionID of arbitrary bytes could not be read this way.** That it is legible in a hex dump, with no
decoder and no key, is the same fact as the entropy shortfall wearing different clothes.

## What this settles

**Settled:** the claim is measured. Their station issues eight-ASCII-digit SessionIDs on the wire, across
24 independent sessions, on the revision the report names.

**Unchanged:** everything the report already said about *consequence*. It buys nothing until
[`evdriveflow-session-id`](../../reports/evdriveflow-session-id.md) is fixed — **a value nobody compares
does not have to be hard to guess** — and both filings say so. The order matters and this run does not
change it.

**Not attempted:** the `[V2G20-460]` half itself. That needs a request carrying a *wrong* SessionID and a
look at what their station does with it, which is a different probe. It is now cheap, though: the rig
below is the hard part and it is written down.

## The rig, and the one thing that cost time

Their SECC binds **`[fd00:edf::2]:49152`** — an IPv6 ULA on the `edfnet` docker network, at an ephemeral
port — and **their log names neither**. It says only `Starting TCP server.` Port 15118, which is what one
assumes, is their **UDP discovery** port and answers `Connection refused`. Read the endpoint out of the
container instead:

```bash
docker exec edf-secc ss -lnt | grep -oE '\[fd00:[0-9a-f:]+\]:[0-9]+'
```

```bash
docker run -d --name edf-secc --network edfnet edf-ev-unpatched \
    sh -c "cd /app/secc && python3 start_evse.py > /tmp/secc.log 2>&1; sleep infinity"
```

```bash
V2G_INTEROP_SECC='[fd00:edf::2]:49152' V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=dc \
V2G_INTEROP_DYNAMIC=1 V2G_INTEROP_RECORD=/tmp/edfsid/s1 \
  dotnet test -c Release --filter "FullyQualifiedName~EvDriveFlowInteropTests.OurEvcc"
```

Their station took 24 consecutive sessions without a restart, which is worth recording as a positive:
nothing in their SECC needed re-plugging between them.

## Artifacts

[`sessions/`](sessions/) — the frame log of each of the 24 sessions, and [`their-secc.log`](their-secc.log).

Offline gate: **1 405 green**, four assemblies, exit code 0.

## Next

- **The `[V2G20-460]` sibling**, [`evdriveflow-session-id`](../../reports/evdriveflow-session-id.md),
  whose own checklist has the same *"run it against your station"* line. It is the one that matters more,
  and the rig above is now written down for it.
