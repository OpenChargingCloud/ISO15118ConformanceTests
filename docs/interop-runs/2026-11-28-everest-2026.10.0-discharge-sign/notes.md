# 2026-11-28 — EVerest 2026.10.0 sends its discharge limits negative, on both sides; ours are positive

**The open question from earlier the same day is decided, and against us.** `[V2G20-1213]` reads as if
every parameter used for discharging, limits included, is to be negative. The *Open questions* entry left
our positive limits unchanged because "no reference encoder or counterparty says otherwise". One does.
EVerest's EV, `Ev15118` in the 2026.10.0 release, puts every discharge limit on the wire as a negative
number, in `ChargeParameterDiscoveryReq` and again in every `ChargeLoopReq`, AC and DC. EVerest's station
does the same with its own limits by default. Our two cars and two stations send positive magnitudes.

| | |
|---|---|
| Counterparty | everest-core **2026.10.0** (`74845c57`), `dist-2610`, in WSL |
| Arms | **reverse**: their `Ev15118` against our SECC, one session each for `dc_bpt` and `ac_bpt`. **Forward**, re-read and not re-run: our EVCC against their `Evse15118D20`, recorded on 2026-11-26 ([`…-2026.10.0-forward`](../2026-11-26-everest-2026.10.0-forward/notes.md), arms `d20-bpt`, `d20-bpt-dyn`, `ac20-bpt-trigger`, `ac20-bpt-dyn-trigger`) |
| Configs | the 2026-11-06 reverse configs ([`config-dc20-…`](../2026-11-06-everest-ev15118/config-dc20-reverse-ev15118.yaml), [`config-ac20-…`](../2026-11-06-everest-ev15118/config-ac20-reverse-ev15118.yaml)) with the session started as `dc_bpt` / `ac_bpt` instead of `DC` / `AC`; nothing else changed ([`configs/`](configs/)) |
| Ours | `EverestInteropTests.TheirPyEvJosev_AgainstOurSecc_RunsToCompletion` from `~/i15118` (the tree as mirrored on 2026-11-27, before the discharge work), `V2G_INTEROP_BPT_FIRST=1`, so the fixture asserts a bidirectional service was negotiated |
| Result | both reverse sessions **passed** to `SessionStop`: their EV selected `DC_BPT` (6) and `AC_BPT` (5) and charged; nothing was discharged |
| Evidence | per arm our recorder's octet streams, `frames.log`, `trace.json` and flow, plus both logs. The limits are decoded with our own codec by [`scripts/decode-limits/`](scripts/decode-limits/Program.cs) into [`decoded-limits.txt`](decoded-limits.txt), with the frame hex beside each value |

## What is on the wire

Values as decoded, `value × 10^exponent`. Every one of them is in [`decoded-limits.txt`](decoded-limits.txt)
with its frame.

| who | message | max discharge power | min discharge power | max discharge current | min discharge current |
|---|---|---|---|---|---|
| **their EV**, DC_BPT | `DC_ChargeParameterDiscoveryReq` | **−150 000 W** (−1500·10²) | 0 | **−300 A** (−3000·10⁻¹) | 0 |
| **their EV**, DC_BPT | `DC_ChargeLoopReq` (Scheduled) | **−150 000 W** | 0 | **−300 A** | — |
| **their EV**, AC_BPT | `AC_ChargeParameterDiscoveryReq` | **−3 680 W** | **−1 380 W** | — | — |
| **their EV**, AC_BPT | `AC_ChargeLoopReq` (Scheduled) | **−3 680 W** | **−1 380 W** | — | — |
| **their station**, DC_BPT | `DC_ChargeParameterDiscoveryRes` (2026-11-26) | **−150 000 W** | **−200 W** | **−200 A** | **−1 A** |
| their station, AC_BPT | `AC_ChargeParameterDiscoveryRes` (2026-11-26) | 0 | 0 | — | — |
| our EVCC, DC_BPT | `DC_ChargeParameterDiscoveryReq` (2026-11-26) | +50 000 W | 0 | +200 A | 0 |
| our EVCC, AC_BPT | `AC_ChargeParameterDiscoveryReq` (2026-11-26) | +22 000 W | 0 | — | — |
| our SECC, DC_BPT | `DC_ChargeParameterDiscoveryRes` (today) | +50 000 W | 0 | +200 A | 0 |
| our SECC, AC_BPT | `AC_ChargeParameterDiscoveryRes` (today) | +22 000 W | 0 | — | — |

Their charge limits stay positive throughout, so this is a sign that encodes direction, not a different
convention for magnitudes. Their AC station's zeros come from the forward config, which gives it no
discharge capability; its code applies the same negation (next section).

## Why they do it, from their source

Read in the `core-2610` worktree at `74845c57`.

- **The EV**: `lib/everest/iso15118/include/iso15118/ev/d20/discharge_sign.hpp`, added with PR #3031
  (2026-11-06). One function, `discharge_on_wire`, returns `-fabs(magnitude)` for every discharge power and
  current the EV sends, AC and DC, in charge-parameter discovery and in the charge loop. Its comment cites
  `[V2G20-1035]`, and **Table 94 of Amendment 1**, which by their reading bounds every EV discharge power
  and current at ≤ 0. The configuration keeps magnitudes (`ac_max_discharge_power_w`, `dc_max_discharge_current_a`, …
  all `minimum: 0`), so the sign is the wire's alone.
- **The station**: `Evse15118D20`'s option `negative_bidirectional_limits` makes it send its own discharge
  limits as `-fabs(...)`. It **defaults to `true`**. Its manifest text still calls the standard unclear on
  the point, but the default is negative.
- **What the station reads**: `discharge_magnitude()` in `charger/utils.cpp` takes `fabs()` of every
  discharge limit the EV sends before merging it with its own. So their station accepts both signs. That
  is why our positive limits never failed against them, and why "EVerest accepted ours" said nothing about
  which sign is right.

## The requirement side

Recorded in [`normative-basis.md`](../../normative-basis.md#the-sign-of-the--20-discharge-limits), clause IDs
and paraphrase only. In short: the general rule makes negative mean energy from the EV, `[V2G20-1213]`
applies it to discharge parameters in both AC and DC BPT, the one stated exception is the discharged-energy
*register*, and every comparison between limits is written on absolute values. Amendment 1 is not in
`onlyLocalDoNotCommit/`, so EVerest's Table 94 citation is theirs, not checked here.

## The other counterparties, from their source

- **Josev** (`d645255`): its EV simulator fills the discharge limits positive (11 kW, 11 A), and its
  station passes on whatever its controller gives it, with no sign check found.
- **eVDriveFlow**: its car sent 48 000 W, positive
  ([2026-08-07](../2026-08-07-edf-dc-bpt/bpt-envelope.txt)).

So the field is split. It is not one shared convention: the stack most recently written against the text
(and its amendment) went negative, and the two older ones are positive.

## What follows for us

- **Ours to change, per the requirement and the reference**: our cars and stations should send discharge
  limits negative, and read incoming ones by magnitude, as EVerest's station does. Reading by magnitude
  matters, because Josev and eVDriveFlow will keep sending positive values. The change spans the C# stack,
  the Kotlin and Swift ports, the app, and every recorded `BPT_*` trace in the corpus.
- **The discharge run against their EV is not blocked by this** (the next run in *Untested*). Their car's
  limits arrive negative, and our discharging stations already clamp against the EV's maximum by magnitude
  (`Math.Abs` in `Secc20Dc` and `Secc20Ac` on stack master). Today's sessions only charged, so this run did
  not exercise that clamp. `Secc20Ac`'s comment there, that field practice leaves the sign positive, is what
  this run contradicts.
- **Not proven by this run**: how Josev's or eVDriveFlow's station reacts to a negative limit from our car.
  That is a run to make after the change, not a reason against it.

## How it was run

Inside WSL, from PowerShell (`wsl.exe bash <script>`), the script in [`scripts/rev-dsign.sh`](scripts/rev-dsign.sh):

```bash
bash rev-dsign.sh dc20-bpt dc
bash rev-dsign.sh ac20-bpt ac
```

It derives the config, stops the rig, starts the broker, builds `~/i15118` (exit code checked), starts our
fixture on `eth0:55000`, waits for the listener, then starts their manager with the derived config and waits
for the test. The system `dotnet` (`/usr/bin/dotnet`, SDK 11.0.100) is the one to use; `~/.dotnet` holds only
SDK 10.0.400, which cannot build the `net11.0` projects.

The decoder is a throwaway console program referencing our built codec assemblies (`V2GTPDispatcher`); it
prints only the discharge and charge limit fields of each decoded message, with the frame hex.
