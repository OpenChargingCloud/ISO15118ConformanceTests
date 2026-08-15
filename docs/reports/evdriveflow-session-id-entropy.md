# Draft report to EDF Lab (eVDriveFlow) — the SessionID is eight ASCII digits, so 26,6 bits where 58 are required

Status: **draft, not sent.** The finding is in one line of your source and needs no session to see —
and it was taken off the wire anyway on 2026-08-15, **24 sessions against your own station**. Post it
under your own name; see *Before sending* at the bottom.

Evidence in this repository:
[`2026-08-11-everest-d20-rng-entropy`](../interop-runs/2026-08-11-everest-d20-rng-entropy/notes.md)
— an entropy audit across four ISO 15118-20 stacks, of which yours is one of the two that come up
short — and
[`2026-08-15-edf-session-id-entropy`](../interop-runs/2026-08-15-edf-session-id-entropy/notes.md), the
24 SessionIDs and the frame logs they came out of.

Four other reports for the same project — issues 1 to 5 — are in
[`evdriveflow-headless-session.md`](evdriveflow-headless-session.md),
[`evdriveflow-authorization-setup.md`](evdriveflow-authorization-setup.md),
[`evdriveflow-service-discovery-filter.md`](evdriveflow-service-discovery-filter.md) and
[`evdriveflow-session-id.md`](evdriveflow-session-id.md). **File this one separately** from the last
of those: same field, different requirement, different line, different fix — and, as the section
below says, the two are worth fixing in a particular order.

---

**Title:** `EVSESession.generate_random_session_id()` draws from 10⁸ values, so the 64-bit SessionID
carries ~26,6 bits of entropy where `[V2G20-2621]` requires at least 58

**Version:** `eVDriveFlow` `60249c3` (2023-04-17), still `origin/main` on 2026-08-11.

## The defect

`secc/evse_session.py:104–111`:

```python
@staticmethod
def generate_random_session_id() -> str:
    """
    Generates a random 8-length int number. See [V2G20-2621] for more detail. Might have security issues.

    :return: str -- the resulting id.
    """
    return str(secrets.randbelow(100000000)).zfill(8).encode('ascii')
```

`secrets` is the right module — this is a cryptographically secure generator, so `[V2G20-835]` is
satisfied and that half is not the problem. What is short is the **range**:

| | |
|---|---|
| values reachable | 10⁸ = 100 000 000 |
| entropy | log₂(10⁸) = **26,6 bits** |
| `[V2G20-2621]` requires | a 64-bit nonce with **at least 58 bits** |
| short by | **31,4 bits** |

The eight bytes that reach the wire are ASCII decimal digits, every one of them in `0x30`–`0x39`,
so `SessionID` never leaves a 10⁸-wide corner of the 2⁶⁴ the field provides.

**In practice:** by the birthday bound a station repeats a SessionID after about √10⁸ = **10 000
sessions**. That is a few years of one charge point, or a season of a fleet — not an astronomical
number, which is what a 58-bit floor is there to make it.

Your own docstring names the requirement and says it *might have security issues*, so this report
is mostly a measurement of by how much, and a one-line fix.

## On the wire, 24 times

Your SECC, `60249c3`, plain TCP, 24 consecutive sessions from our EV against one station — every
SessionID read out of your own `SessionSetupRes`:

```
50475261  66517944  96751133  26079747  40482773  76881948  19193724  86292746
75776846  40738080  17203515  41960978  67275833  52324782  64493806  58476268
47267897  35647279  20819696  30812432  84089082  92122701  47445612  39261178
```

**24 of 24 eight ASCII digits, 24 distinct**, minimum 17 203 515, maximum 96 751 133 — every one inside
`[0, 10⁸)`. The field is eight bytes and full; it is the *alphabet* that is ten symbols wide, which is
where 8 × log₂10 = 26,6 comes from.

**And reading them needed no EXI decoder.** The SessionID sits at a one-bit offset in the payload, so
shifting it left by one bit makes the digits appear as text a regex finds:

```
payload   ...041b9c1c1c9c1b9b1a8ecfab8d3062...
<< 1 bit  ...  37 38 38 39 38 37 36 35  ...   =  "78898765"
```

That one is from a session recorded on 2026-08-01, ten days before this report was written. A SessionID
of arbitrary bytes would not be legible that way at all — **that it can be read out of a hex dump with
no decoder is the same fact as the shortfall, wearing different clothes.**

One thing the sample does not show: all 24 draws are ≥ 10⁷, so none exercises `zfill`'s leading zero.
`zfill(8)` guarantees the width either way; the sample simply does not happen to contain the case.

## Suggested fix

```python
return secrets.token_bytes(8)
```

Same module, same call shape, 64 bits instead of 26,6. `[V2G20-2106]` also wants the generated
SessionID to differ from zero, which `token_bytes` satisfies with probability 1 − 2⁻⁶⁴; if you
prefer it explicit, draw again on the all-zero result.

If the ASCII form is load-bearing somewhere — a log line, a dict key — `secrets.token_bytes(8)` with
`.hex()` at the presentation site keeps that and still puts 64 real bits in the header.

## Fix `[V2G20-460]` first, or this one buys nothing

Worth saying plainly, because the two interact and the order matters.

[The other SessionID report](evdriveflow-session-id.md) is that your SECC never reads the incoming
`SessionID` at all: fifteen `process_*_request.py` handlers write their own id into the response
header and none compares. **While that is true, the entropy of the id is irrelevant** — a peer does
not have to guess a value that is never checked.

So: `[V2G20-460]` is what makes a SessionID mean anything, and `[V2G20-2621]` is what makes it hard
to guess once it does. Fixing this one alone changes nothing observable; fixing that one alone
leaves a 26,6-bit secret guarding the session. They are two issues because they are two lines in two
files with two requirements behind them, and they are worth doing together.

## Context: four stacks, three answers

| stack | SessionID | entropy |
|---|---|---|
| Josev (SwitchEV), `-2` · `-20` · DIN | `secrets.token_bytes(8)` | 64 bits |
| EVerest `EvseV2G` (`-2`, DIN) | 8 bytes from `/dev/urandom` | 64 bits |
| EVerest `libiso15118` (`-20`) | `std::mt19937` seeded with one 32-bit draw | **≤ 32 bits** — [filed separately](everest-d20-rng-entropy.md) |
| **eVDriveFlow (`-20`)** | `secrets.randbelow(10⁸)` as 8 ASCII digits | **26,6 bits** |

Two of the four meet `[V2G20-2621]`. The EVerest one is a different defect against the same
requirement — a non-cryptographic generator rather than a narrow range — and was measured rather
than read: 49 of 49 SessionIDs their station had issued were recovered from the 2³² seed space.

**Ours** draw both the SessionID and the challenge from the .NET CSPRNG at full width, which is why
this report has no *we had it too* paragraph. It is not from higher ground: the audit that found
this ran the morning after we finished closing a different SessionID gap of our own.

---

## Before sending

- [x] **Read the source at the current head.** `60249c3` is still `origin/main`, unchanged since
      2023-04-17.
- [x] **Check the requirement rather than the instinct.** The generator is `secrets` and is fine;
      only the range is short. A report that said "use a CSPRNG" would have been wrong about your
      code.
- [x] **Say where the requirement is not exotic.** Two of the three other stacks meet it, and the
      third fails it a different way.
- [x] **Run it against your station — done 2026-08-15, 24 sessions.** 24 of 24 eight ASCII digits, 24
      distinct, all inside `[0, 10⁸)`, read out of `SessionSetupRes` with no EXI decoder. Twenty-four
      rather than one because the claim is about a **range** and a range needs more than one point.
      [`…-edf-session-id-entropy`](../interop-runs/2026-08-15-edf-session-id-entropy/notes.md).
      <br>Two things for whoever repeats it: your SECC binds `[fd00:edf::2]:49152` — an IPv6 ULA at an
      ephemeral port — and your log names neither, saying only `Starting TCP server.`; port 15118 is the
      UDP discovery port and refuses the connection. And your station took all 24 sessions without a
      restart, which is worth saying as the positive it is.
- [ ] **File it separately from the `[V2G20-460]` report**, and say in both that the order matters.
- [ ] **Decide issue or PR, and expect a slow response.** Re-checked 2026-08-11: no commit on `main`
      since `60249c3` (2023-04-17), **three years and four months**. A PR is more likely to be useful
      than an issue, and this one is a single line. Same caveat as the other five.
- [ ] **Post under your own name, in your own words.**
