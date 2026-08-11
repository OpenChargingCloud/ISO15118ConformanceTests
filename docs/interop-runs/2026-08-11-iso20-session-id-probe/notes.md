# 2026-08-11 — the `-20` SessionID probe: one instrument, three stacks, one defect

The `-2` session-id probe earlier today found EVerest's `EvseV2G`
[serving the all-zero id](../2026-08-11-everest-iso2-session-id-zero/notes.md). This is the `-20`
version of the same question, asked of every `-20` stack this suite can reach — and it ends with a
filing against the one that does not implement the rule at all.

| | |
|---|---|
| Instrument | `Evcc20Base.SendSessionId` — new, the `-20` twin of `Evcc2.SendSessionId`; `V2G_INTEROP_SESSIONID=zero` reaches it |
| Measured | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12b8`), `Evse15118D20`, DC over plain TCP |
| Read | [eVDriveFlow](https://github.com/EDF-Lab/eVDriveFlow) `60249c3`; [SwitchEV/iso15118](https://github.com/SwitchEV/iso15118) `d645255c` |
| Outcome | EVerest `-20` **correct**, Josev **correct**, eVDriveFlow **does not implement it**. Filed: [`evdriveflow-session-id.md`](../../reports/evdriveflow-session-id.md) |
| Artifacts | [`their-charger.control.log`](their-charger.control.log) · [`their-charger.zero.log`](their-charger.zero.log) · [`ours.control.log`](ours.control.log) · [`ours.zero.log`](ours.zero.log) · [`sid20-run.sh`](sid20-run.sh) |

## The measurement, which is a positive control

`[V2G20-460]`: any request except `SessionSetupReq` whose SessionID is not the stored one shall be
answered `FAILED_UnknownSession`. Two arms against a freshly started `Evse15118D20`:

| arm | our EV sends | their station |
|---|---|---|
| **control** | the id it was issued | full DC session — charge loop, welding detection, `SessionStopRes` |
| **zero** | eight zero bytes from `AuthorizationSetupReq` on | **`FAILED_UnknownSession`**, and our EV ended the session on it |

**This run exists to prove the instrument, not to find a defect.** Their `-20` implementation is
correct, and the run says so — which is what makes it usable as the reference answer in a filing about
a station that is not.

## The three-way result

| stack | how it handles a foreign SessionID |
|---|---|
| **EVerest `-20`** (`libiso15118`) | `validate_and_setup_header` in **15 of 17** states; the two without it are `session_setup` and `supported_app_protocol` — exactly the two the rule excludes. Measured above |
| **Josev** (all three protocols) | one guard in `secc/states/secc_state.py`, excluding `SessionSetupReq` of all three protocols and `SupportedAppProtocolReq`, `FAILED_UNKNOWN_SESSION` otherwise. No exemptions. Source only |
| **EVerest `-2`** (`EvseV2G`) | checks, and exempts `received_session_id != 0` — [filed](../../reports/everest-evsev2g-session-id-zero.md), measured |
| **eVDriveFlow `-20`** | **never reads the incoming header.** Fifteen `process_*_request.py` handlers write `self.session_parameters.session_id` into the response and none compares; `FAILED_UnknownSession` exists only in the generated bindings. Source only — **filed** |

Two of four correct, one narrow defect, one absent. Worth stating as a table because each project can
see from it that the requirement is neither obscure nor uniformly met.

## A correction to my own earlier grep

The first pass at EVerest's `-20` reported ten states checking the session id and left the impression
that the charge loop might not. It does — the grep output had been truncated by a `head`, and the full
sweep over all 17 state files shows 15 checks and two deliberate omissions. **A truncated grep and a
short list look identical.** The same lesson as the regex that could not match `next_msg_timeout` a few
hours earlier, in a different disguise.

## And an open question of ours, closed on the way past

The [sequence-timeout filing](../2026-08-11-everest-d20-sequence-timeout/notes.md) left one: does
ISO 15118-2 also override `V2G_SECC_Sequence_Timeout` per message, the way `-20`'s Tables 216/217 do?

**It does not.** Table 108 was re-extracted page by page — `pdftotext -layout` had flattened its
five stacked parameter names into one column, which is exactly the mangling that nearly produced a
wrong reading of the `-20` DC table. Read properly, the message list belongs to
`V2G_SECC_Msg_Performance_Time` (`CurrentDemandRes` 0,025 s — a different timer, how fast the SECC must
*answer*), and the sequence timeout sits in the `(all messages)` row at **60 s**, with
`V2G_EVCC_Sequence_Performance_Time` 40, `V2G_EVCC_Ongoing_Timeout` 60 and
`V2G_SECC_Ongoing_Performance_Time` 55 beside it — the same four values, in the same order, as `-20`'s
Table 215.

So the charge-loop override is an addition of the newer document, `EvseV2G`'s flat 60 s is **correct**
for `-2`, and our own `-2` station's flat timeout is correct too. Recorded in
[`open-work.md`](../../open-work.md), which had been carrying it as a question.

## What this does not decide

- **eVDriveFlow was not run.** Their rig needs docker, a prepared clone and the IPv6 bridge; the filing's
  first checklist item is that run, with the command.
- **Only `AuthorizationSetupReq` was probed** on EVerest. The guard is per state and 15 states carry it,
  so the expectation is uniform, but one message was measured.
- **Josev was read, not run** — as in the [charge-loop timeout audit](../2026-08-11-josev-charge-loop-timeout-audit/notes.md).
- **tux-evse is not in the table**: it speaks `-2` and DIN only, so `[V2G20-460]` does not apply. Its
  `-2` responder remains the obvious next target for the `-2` probe.

## Reproduce

```bash
~/everest/dist/bin/manager --config ~/everest/configs-ours/config-d20-ours.yaml &
CP_AT_PLUGIN=1 bash ~/everest/sil-car.sh &
bash ~/everest/sdp-probe.sh eth0

V2G_INTEROP_SECC='[<addr>%eth0]:<port>' V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=dc \
V2G_INTEROP_SESSIONID=zero \
  dotnet test -c Release ISO15118ConformanceTests.Simulation \
    --logger "console;verbosity=detailed" \
    --filter "FullyQualifiedName~EverestInteropTests.OurEvcc"
```

A refusal makes the fixture fail, which is the **expected** outcome against a conformant station: our
EVCC ends the session on any `FAILED_*`. Read the abort message, not the exit code.
