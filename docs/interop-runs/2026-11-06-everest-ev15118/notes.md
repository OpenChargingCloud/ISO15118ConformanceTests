# 2026-11-06 — EVerest's new C++ EV against our station: the half second is gone, and one cell is ours

EVerest is replacing its car. [everest-core#3031](https://github.com/EVerest/everest-core/pull/3031) —
open since 2026-10-08, mergeable, last pushed 2026-11-05 — adds an EV side to `libiso15118` and a module
around it, **`Ev15118`**, puts it in every config, and deletes `PyEvJosev`; the stacked
[#3034](https://github.com/EVerest/everest-core/pull/3034) then drops Josev from the build altogether.
Every EVerest cell in which *their car* drives *our station* rests on `PyEvJosev` — and so does one
drafted report, [`josev-iso20-evcc-charge-loop-pacing`](../../reports/josev-iso20-evcc-charge-loop-pacing.md),
whose whole subject is that module's ≈532 ms charge-loop turnaround. None of our documents knew the PR
existed. This run puts the replacement in front of our SECC before it merges.

| | |
|---|---|
| Counterparty | everest-core **PR #3031 head `35d3eda52`** (`feat/ev15118` = `main` `852ff1859` + 10 commits), built from source in the WSL rig: 22 modules, `Ev15118` in place of `PyEvJosev`, 9 min 25 s configure-to-install |
| Control | everest-core **2026.02.1** (`b61bb12`), `PyEvJosev`, the 2026-08-13 config unchanged |
| Configs | derived from the PR's **own** `config-sil-{ac,dc}-d20-plaintext.yaml`, changed only where the reverse direction needs it — each change is listed in the file's header: [`config-ac20-reverse-ev15118.yaml`](config-ac20-reverse-ev15118.yaml), [`…-dc20-…`](config-dc20-reverse-ev15118.yaml), [`…-ac20-dynamic-…`](config-ac20-dynamic-reverse-ev15118.yaml), [`…-ac20-tls-…`](config-ac20-tls-reverse-ev15118.yaml), [`…-ac2-…`](config-ac2-reverse-ev15118.yaml), [`…-ac2-tls-…`](config-ac2-tls-reverse-ev15118.yaml), [`…-dc2-…`](config-dc2-reverse-ev15118.yaml) |
| Ours | `EverestInteropTests.TheirPyEvJosev_AgainstOurSecc_RunsToCompletion` — the name predates `Ev15118`, nothing in it is specific to either module; listening on `eth0:55000`, advertising over SDP, inside WSL |
| Clock | a packet capture of every arm (`tcpdump -i any`, as root), read by the new [`v2gtp-turnaround.py`](../../../tools/interop-everest/v2gtp-turnaround.py) |
| Outcome | **6 of 7 scenarios complete under our conformant timers.** `-2` DC stops after `CableCheck`, 2 of 2, on an optional element our station never sends — and completes, 2 of 2, once a patched copy of our station sends it |

## The arms

| arm | protocol, mode | transport | result | charge-loop pairs | their EV's turnaround, median (min–max) |
|---|---|---|---|---:|---|
| [`ac20-strict`](ac20-strict/) | `-20` AC, Scheduled | plain | ✅ `SessionStop` | 917 | **25,8 ms** (25,5–27,1) |
| [`dc20-strict`](dc20-strict/) | `-20` DC, Scheduled | plain | ✅ | 692 | **25,7 ms** (25,5–26,5) |
| [`ac20-dynamic`](ac20-dynamic/) | `-20` AC, **Dynamic** (their default) | plain | ✅ Dynamic asserted | 917 | **25,8 ms** (25,4–32,6) |
| [`ac20-tls13`](ac20-tls13/) | `-20` AC, Scheduled | **mutual TLS 1.3** | ✅ | 918 | — (encrypted) |
| [`ac2-plain`](ac2-plain/) | `-2` AC, EIM | plain | ✅ | 237 | **100,7 ms** (100,5–145,8) |
| [`ac2-tls12`](ac2-tls12/) | `-2` AC, EIM | **TLS 1.2** | ✅ | 236 | — (encrypted) |
| [`dc2-plain`](dc2-plain/), [`dc2-plain-2`](dc2-plain-2/) | `-2` DC, EIM | plain | ⛔ **2 of 2** stop after `CableCheck` | 0 | — |
| [`dc2-diag-isolation-valid`](dc2-diag-isolation-valid/), [`…-2`](dc2-diag-isolation-valid-2/) | `-2` DC, EIM — **our station patched**, see below | plain | ✅ 2 of 2 | 177 | **100,8 ms** (100,6–148,3) |
| [`ac20-pyevjosev-control`](ac20-pyevjosev-control/) | `-20` AC — **`PyEvJosev`, 2026.02.1** | plain | ✅ with our charge-loop timer at 20 s | 43 | **535,0 ms** (526,5–578,2) |

Every `Ev15118` arm ran with our station at its **conformant** timers — `V2G_SECC_Sequence_Timeout`
0,5 s in the `-20` charge loop — and with the EV at **its** defaults: `response_timeout_ms` 0, which the
manifest documents as the per-message timeouts of the standard (the PR's own SIL configs set 8000).

## The 532 ms: gone, and measured the same way both times

The 2026-08-13 number came from **their** log — PowerOn to the stop, divided by 44 loops. A number from
a different instrument would not be comparable, so the control arm re-ran that exact config on 2026.02.1
today, on the same host, under the same capture: **535,0 ms median on the wire**, against 532 ms from
the log three months ago. The method reproduces the old measurement to within 3 ms, which is what lets
the next line stand:

**`Ev15118` turns an `AC_ChargeLoop` around in 25,8 ms** — a twentieth of `PyEvJosev`, and a tenth of
the 0,25 s Table 216 gives the EVCC for `AC_ChargeLoopReq` (`[V2G20-1499]`). The strict arms that killed
`PyEvJosev` on the first loop, 2 of 2, ran 917 loops to `SessionStop` here.

**It is not fast by accident; it is paced on purpose, and the pace is the whole number.** Every `-20`
request — not only the charge loop — leaves 25,5–26,5 ms after the response before it, and every `-2`
one 100,5–101 ms after. The one exception in each protocol is the first `CableCheckReq` (304 ms in
`-20` DC, 372–380 ms in `-2` DC), which their EV holds until the control pilot reaches state C
(`CableCheck::begin` in the same `cable_check.cpp`). Both cadences are constants in the PR:

- `-20`: `DEFAULT_SEND_DELAY = 25 ms` (`lib/everest/iso15118/include/iso15118/ev/config/config.hpp:28`),
  documented there as the re-poll cadence that keeps the EV from flooding the SECC;
- `-2` and DIN: `MIN_REQUEST_INTERVAL = 100 ms` (`…/ev/d2/timeouts.hpp:28`, and the same in
  `…/ev/din/timeouts.hpp:26`), commented as parity with `EvseV2G`'s `MAX_RES_TIME`.

So the EV's own processing is under a millisecond, and what a station sees is a deliberate cadence well
inside both standards' budgets. Our station's side, for the record: median 0,2–0,4 ms per charge-loop
response in every arm, 8–16 ms for `SessionSetupRes`.

### What it means for the drafted report

[`josev-iso20-evcc-charge-loop-pacing`](../../reports/josev-iso20-evcc-charge-loop-pacing.md) measured a
property of `PyEvJosev`, and **`PyEvJosev` is still what every released config runs** — 2026.02.1,
2026.02.2 and `main` (`01f36de1`) all ship `modules/EV/PyEvJosev/`. Today's control shows the finding
unchanged in that module. But #3031 removes the module from every config, so once it merges there is
nothing on `main` to report: the report becomes a release-line note, or nothing. That is a decision for
whoever sends it; the report carries the measurement at its top.

## `-2` DC: their EV stops after `CableCheck` — on an element our station never sends

Both plain `-2` DC arms end the same way, 0,9 s into the session: our `CableCheckRes` says `OK` and
`EVSEProcessing=Finished`, and their EV logs that `CableCheck` finished *without a valid isolation
result* and stops.

**Ours.** Our `-2` station builds every `DC_EVSEStatus` with `EVSEIsolationStatus` left out —
`Secc2.cs:1474`, and `CableCheck()` at `:1264` answers `Finished` in one step with that status. The 18-byte
`CableCheckRes` in [`dc2-plain`](dc2-plain/) has no isolation element at all.

**Theirs.** `ev/d2/state/cable_check.cpp:31-34` accepts `Valid` and `Warning` and refuses `Invalid`,
`Fault`, `No_IMD` — **and an absent element**, which the comment beside it names explicitly. A choice,
not an oversight.

**The diagnostic arm, which is why this is not a guess.** In the WSL copy only — never in the tree that
is committed — `CableCheck()` was changed to report `EVSEIsolationStatus=Valid`. The `CableCheckRes`
grows to 19 bytes, and their EV goes on through `PreCharge`, `PowerDelivery`, 177 `CurrentDemand` pairs
and `WeldingDetection` to `SessionStop`, 2 of 2. The absent element is the **only** thing between this
EV and a `-2` DC session with our station.

**Who is right is not decidable from what is here.** ISO 15118-2 makes the element optional — the schema
has `minOccurs="0"`, Table 95 calls it optional and defers its handling to IEC 61851-23, and
`[V2G2-801]` only tells the *EVCC* to read it per Table 96. Nothing in `-2` obliges the SECC to send it
in `CableCheckRes`. IEC 61851-23, which the clause defers to, is not in `onlyLocalDoNotCommit/`. The
2019 manual's worked `CableCheckRes` carries `Valid`, and its sequence figure labels that response
*isolation state* — practice, not obligation.

So: **not a filing in either direction.** Refusing to energise without a stated isolation result is a
defensible safety choice for a car; a schema-valid station that never states one is legal and, for a
simulator whose `CableCheck` claims to have finished a test, the less useful peer. A real station would
say what it found. Whether ours should — it moves our `-2` DC wire output, and with it the session
corpus — is in [`open-work`](../../open-work.md), not decided here.

## A trap of ours that nearly produced a false control

The first repeat of the plain `-2` DC arm **passed**. It should not have, and it had not: restoring the
WSL copy with `rsync -a` put the original `Secc2.cs` back **with its original mtime of 2026-10-30**, older
than the diagnostic build's output, so MSBuild considered the patched `WWCP_ISO15118_Session.dll` up to
date and did not compile — an 8-second "build", exit 0. The arm was the diagnostic binary a second time,
visible in its 19-byte `CableCheckRes`; it is filed as [`dc2-diag-isolation-valid-2`](dc2-diag-isolation-valid-2/)
because that is what it was. `touch` and a rebuild (DLL timestamp checked) produced
[`dc2-plain-2`](dc2-plain-2/), which stops exactly like the first. **A restore by `rsync -a` does not
undo an in-place patch for MSBuild** — check the output's timestamp, not the build's exit code.

## What else this run established

- **Mutual TLS 1.3 over `-20`**: `TLS_AES_256_GCM_SHA384`, their EV presenting its vehicle certificate
  (`CN=WMIV1234567890ABCDEX`, issued by `VehicleSubCA2`), our station serving the PR build's own
  `SECC_LEAF` with both CPO Sub-CAs, verified by their EV against its V2G root. Charge loop as in the
  plain arm.
- **TLS 1.2 over `-2`**: `TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA256`, one of the two suites `-2` prescribes,
  no client certificate — the manifest's `tls_version: auto` doing what it says, 1.3 for `-20` and 1.2
  for `-2`.
- **Dynamic**: offered first, taken — our fixture asserts the EV sent `Dynamic_SEReqControlMode`.
- **SDP and the port**: their EV found our station on 55000 every time. (Josev's own EVCC refuses 15118;
  this one was not asked.)
- **The codec no longer flips with the direction.** `PyEvJosev` encoded with EXIficient; `Ev15118`'s
  message layers (`message/`, `message_2/`) are `libiso15118` over **cbV2G**, the same codec as their
  stations. The premise of [`josev-is-everests-ev`](../../matrix/josev-is-everests-ev.md) — that a `←SECC`
  run against EVerest re-runs the Josev column — ends with #3031: this EV is EVerest's own state
  machine, written against the standard rather than inherited.

## Reproduce

Inside WSL — SDP is multicast on `eth0`. The rig's scripts are in `~/everest/`; what they do:

```bash
# their side: PR head, own worktree, own prefix (no PyEvJosev in the module list, Ev15118 instead)
git -C ~/everest/core-main fetch origin pull/3031/head
git -C ~/everest/core-main worktree add --detach ~/everest/core-ev15118 35d3eda52ebe99e9493df030eb4fac09b2b78f2b
bash ~/everest/build-ev15118.sh           # cmake/ninja, 22 modules, -DEVEREST_DEPENDENCY_ENABLED_LIBOCPP=ON

# one arm: capture as root (this user has no CAP_NET_RAW), then ours first, then their manager
sudo timeout 300 tcpdump -i any -nn -U -w ac20-strict.pcap 'tcp port 55000 or udp port 15118' &
V2G_INTEROP_LISTEN=55000 V2G_INTEROP_SDP=eth0 V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=ac \
V2G_INTEROP_RECORD=$PWD/rec \
  dotnet test ISO15118ConformanceTests.Simulation/ISO15118ConformanceTests.Simulation.csproj -c Release \
    --artifacts-path ~/wsl-artifacts -l "console;verbosity=detailed" \
    --filter FullyQualifiedName~TheirPyEvJosev_AgainstOurSecc &
until ss -lnt | grep -q ':55000 '; do sleep 1; done
~/everest/dist-ev15118/bin/manager --config config-ac20-reverse-ev15118.yaml

python3 tools/interop-everest/v2gtp-turnaround.py ac20-strict.pcap rec/everest-iso15118-20-ac-reverse.flow.md
```

Per arm: `V2G_INTEROP_PROTOCOL=2` for the `-2` arms, `V2G_INTEROP_MODE=dc` for DC,
`V2G_INTEROP_DYNAMIC=1` for Dynamic, `V2G_INTEROP_TLS_SERVER=secc.p12:123456` (the PR build's
`client/cso/SECC_LEAF` with `ca/cso/CPO_SUB_CA{2,1}.pem`) plus `V2G_INTEROP_TLS_REQUIRE_CLIENT=1` for the
`-20` TLS arm. The control: `~/everest/dist/bin/manager` with
[the 2026-08-13 config](../2026-08-13-everest-d20-ac-reverse/config-ac20-reverse-ours.yaml) and
`V2G_INTEROP_CHARGELOOP=20000` — **a run that used it is not a passing charge-loop result**, which is the
point of that arm.

## Artifacts

One directory per arm: our log, their log (ANSI colours stripped), the flow, both octet streams, and —
where captured — the `.pcap` with `turnaround.md` / `turnaround.csv` computed from it. Arms of more than a
hundred exchanges omit `frames.log` and `trace.json`, which are derived from the streams and would add
~0,7 MB each; the short `-2` DC arms keep them. The two TLS arms have no capture worth keeping — the
bytes are encrypted.

## Next

- **Decide whether our `-2` station states its isolation result** — [`open-work`](../../open-work.md).
- **Decide what the pacing report becomes** once #3031 merges: a release-line note or nothing.
- **Re-run when #3031 merges**, against `main`: this ran a PR head, and the head will move.
