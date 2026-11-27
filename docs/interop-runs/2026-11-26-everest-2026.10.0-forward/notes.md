# 2026-11-26/27 — EVerest 2026.10.0, forward: our car against the released station

**What this run is:** the forward half of the matrix, our EVCC against their stations, repeated on the
**`2026.10.0` release** (`74845c57`). Earlier the same day the
[release run](../2026-11-26-everest-2026.10.0-release/notes.md) re-ran only the reverse arms (their EV
against our SECC) and the PaymentDetails probe. The forward rows of the matrix last ran against
**2026.02.1** ([2026-08-05](../2026-08-05-everest-2026021-matrix/notes.md) and the August runs after
it). This run puts every one of them in front of the release.

**The headline:** `-2` holds without exception. **Every Scheduled `-20` session failed**, 16 of 16, at
`PowerDeliveryRes = FAILED_EVPowerProfileInvalid`. The fault was **ours**: our car anchored its
`EVPowerProfile` at `TimeAnchor` 0, which is 1970. 2026.10.0 is the first station here that checks the
profile against the offer, and it was right to refuse it. The car was fixed the same night
(stack `3d99345`, below). With the fix all ten DC, MCS and IsoMux Scheduled arms complete. `-20` AC
then meets a second wall that is new in the release: the station waits for the contactor to *open* at
`PowerDelivery(Stop)`. That wait is right under `[V2G20-848]`. A rig trigger that moves the SIL car to
state B on the stop cue makes `-20` AC complete again, 5 of 5. On 2026-11-27, **`-20` Plug & Charge**
ran against the release for the first time. Three defects of ours had to go first (Finding 4). After
that it runs **3 of 3 to `SessionStop`**, with a P-521 contract chain of ours: the release's own MO test
PKI is P-256 and cannot pass its own station's `-20` check.

| | |
|---|---|
| Counterparty | EVerest **2026.10.0** (`74845c578`), `~/everest/dist-2610`, 22 modules, built 2026-11-26 |
| Their stations | `EvseV2G` (`-2`), `Evse15118D20` (`-20`, MCS), `IsoMux` (both, plain and TLS) |
| Ours, before the fix | WWCP_ISO15118 `1287d9c`, EVSimulatorApp `ddc37cf` |
| Ours, after the fix | WWCP_ISO15118 `3d99345` (branch `evcc20-power-profile-anchor`), same tree as the run |
| Fixture | `EverestInteropTests.OurEvcc_AgainstTheirEvseV2G_RunsToCompletion`, from `~/i15118` in WSL, straight to their link-local endpoint |
| Driven by | [`scripts/fwd-2610.sh`](scripts/fwd-2610.sh), one arm per manager start. The `sil-car.sh` car goes in **after** the manager every time, a fresh multicast SDP probe runs per session, and `carsim-on-trigger.sh` handles `-20` AC |
| Configs | [`configs/`](configs/): the 2026-08 `config-*-ours.yaml` with one change. The release has no `PyEvJosev`, so the parked idle car is `Ev15118` on `lo`, plaintext, `supported_protocols` set ([`convert-fwd-2610.py`](scripts/convert-fwd-2610.py)) |
| TLS material | the release's own test PKI: trust anchor `V2G_ROOT_CA`, our client chain `VEHICLE_LEAF ← VehicleSubCA2 ← VehicleSubCA1`, PnC contract `MO_CERT_CHAIN.p12`. Nothing was minted, except for `-20` PnC (Finding 4) |

Every session's flow is in [`sessions/`](sessions/), and the one-line verdicts are in
[`results.txt`](results.txt), produced by [`sum-2610.py`](scripts/sum-2610.py).

## The matrix

| Scenario | 2026.02.1 (August) | **2026.10.0, before the fix** | **after the fix** |
|---|---|---|---|
| `-2` DC EIM, two sessions in one `EvseV2G` process | ✅ ×2 | ✅ **×2** (45/43 pairs), every response `OK` | — |
| `-2` AC EIM | ✅ ×2 | ✅ **×2** (13/13) | — |
| `-2` AC over TLS 1.2 | ✅ | ✅ | — |
| `-2` via IsoMux, plain and TLS 1.2 | ✅ | ✅ both | — |
| `-20` DC Scheduled, ×2 | ✅ | ⛔ `FAILED_EVPowerProfileInvalid` ×2 | ✅ **×2** (71/59) |
| `-20` DC Dynamic | ✅ | ✅ ×4 | — |
| `-20` DC_BPT | ✅ | ⛔ Scheduled · ✅ Dynamic | ✅ Scheduled |
| `-20` DC over mutual TLS 1.3, ×2 | ✅ | ⛔ Scheduled ×2 · ✅ Dynamic | ✅ **×2** |
| MCS (service 8) | ✅ | ⛔ Scheduled · ✅ Dynamic | ✅ |
| MCS_BPT (service 9) | ✅ | ⛔ Scheduled · ✅ Dynamic | ✅ |
| IsoMux `-20` / both, `-2` first | ✅ (routed `-20`) | ⛔ Scheduled · ✅ Dynamic | ✅ both |
| IsoMux over TLS, both-offer | ✅ (TLS 1.2, routed `-20`) | ⛔ Scheduled · ✅ Dynamic | ✅ |
| IsoMux over TLS, `-20` only | alert 70 | `AuthenticationException`: unchanged, TLS 1.2 only | — |
| `-20` AC, AC_BPT (plain, TLS 1.3) | ✅ (window trigger) | ⛔ `FAILED_EVPowerProfileInvalid` | ⛔ `FAILED_ContactorError` at `PowerDelivery(Stop)`, after 3 loops · ✅ **5 of 5 with a state-B trigger at the stop** (Finding 3) |
| `-2` PnC over TLS 1.2 | stalls in `Ongoing`, then `FAILED` | **same**, 369 pairs, `FAILED` | — |
| `-2` PnC without TLS | Contract stripped | **same**: 60 s of `Ongoing` | — |
| `-20` DC PnC over mutual TLS 1.3 | not offered | ⛔ `FAILED_SignatureError` ×3: we signed `ecdsa-sha256` | ✅ **×3** (62/65/65 pairs), Scheduled ×2 and Dynamic, P-521 contract (Finding 4) |

`manager alive at end: 1` on every arm: no crash, no zombie loop.

## Finding 1 — our `EVPowerProfile` was anchored in 1970, and 2026.10.0 is the first to check

All three EVCCs, C#, Kotlin and Swift, built the same constant profile for `PowerDeliveryReq(Start)`:
`TimeAnchor` 0 and one 3600 s entry at 10 kW. The station's check is new in the release. 2026.02.2 does
not contain `FAILED_EVPowerProfileInvalid` at all.
`lib/everest/iso15118/src/iso15118/d20/state/power_delivery.cpp` now walks every profile entry against
every slot of the selected tuple. Time that no slot covers counts as zero power.

The requirement text (FDIS) supports the station on every point:

- **`[V2G20-1861]`**: `TimeAnchor` is the moment the first entry becomes active. Table 103 encodes it in
  **microseconds** of SECC time. A `PowerSchedule`'s anchor (Table 112) is in **milliseconds**, and
  their code divides by exactly those two factors.
- **`[V2G20-1529]`, `[V2G20-1536]`**: SECC time is the station's clock, defined by the `TimeStamp` of
  its `SessionSetupRes` header.
- **`[V2G20-1559]`**: no entry may exceed the selected schedule at any time. **`[V2G20-1811]`**: before
  the first and after the last schedule entry the power is zero. **`[V2G20-478]`**: a violation draws
  `FAILED_EVPowerProfileInvalid`.

A profile active in 1970 therefore asks for 10 kW where the schedule allows zero. Dynamic mode carries
no schedule (their check returns early for it), which is why every Dynamic arm completed and every
Scheduled one did not. **Not a finding against EVerest. It is the first station to hold us to a rule we
broke.**

**The fix** (stack `3d99345`, ports and corpus in EVSimulatorApp `e1247de`, both on
`evcc20-power-profile-anchor`) is `Iso20EvPowerProfile`. The anchor is the station's latest response
`TimeStamp` in µs, taken from the response rather than from our clock, so a recorded session stays a
pure function of its responses. The entries follow the first tuple's charging schedule from that moment,
each capped at the lower of 10 kW and the slot. Time before the schedule gets a zero entry, nothing runs
past its end, and a schedule that has already ended leaves one zero entry. The same session corpus is
replayed byte for byte by C# (136/136) and Kotlin (18/18). Swift is covered only by the macOS CI.

**And after the fix**, every DC, MCS and IsoMux Scheduled arm completes, 10 of 10. Their log for the
first of them, [`sessions/fix-d20.their-side.log`](sessions/fix-d20.their-side.log), shows
`PowerDeliveryRes` going straight into the charge loop. The failed one before it,
[`sessions/d20.their-side.log`](sessions/d20.their-side.log), stops there.

## Finding 2 — our own SECC offers its schedule at `TimeAnchor` 0 too

`Secc20Base.ScheduleExchange` answers a Scheduled car with
`PowerSchedule(TimeAnchor: 0, 3600 s at 100 W)`, plus a `PriceLevelSchedule` at 0 as well. By
`[V2G20-1811]` that schedule ended in 1970. So the corrected car, replayed against our own corpus,
projects **zero power**: one 0-W entry, the only compliant answer to a schedule that has already ended.
Nothing of theirs found this; the car's fix exposed it. A station that anchors its schedule at the
present is the station-side half of Finding 1, and it is left for its own change. Their `Ev15118`
evidently does not check the schedule's anchor: the reverse arms of the release run completed against
this very offer.

**Fixed the same night** (stack `731d7a1`, corpus in EVSimulatorApp `c92c3c2`, both on
`secc20-schedule-anchor`). Every anchor in our `ScheduleExchangeRes` is now the `TimeStamp` that response
carries, each in its own table's unit: ms for the `PowerSchedule` (Table 112), µs for the
`PriceLevelSchedule` and `AbsolutePriceSchedule` (Tables 156, 143). Against our station the car now
projects the slot's 100 W instead of zero. `OfferedScheduleAnchorTests` pins that, and fails 0/2 with
the old anchors. Re-run against 2026.10.0, their `Ev15118` takes the anchored offer in all three
Scheduled reverse arms, `-20` AC, DC and AC over mutual TLS 1.3, each to `SessionStop` (900, 691 and
918 loop lines; WSL run dirs `run/2610/anchor-*`).

## Finding 3 — `-20` AC now waits for the contactor to open at `PowerDelivery(Stop)`

With the profile fixed, `-20` AC passes `PowerDelivery(Start)`. The window trigger still works,
`PowerOn` arrives 0,8 s into the 3 s close window, and three `AC_ChargeLoop` pairs follow. Then
`PowerDelivery(Stop)` draws `FAILED_ContactorError`:

```
iso15118_charge :: Waiting for contactor is opened
evse_manager    :: CAR ISO AC HLC Open contactor
iso15118_charge :: AC contactor is not opened within 1400ms
```

([`sessions/fix-ac20-trigger.their-side.log`](sessions/fix-ac20-trigger.their-side.log)). This is
new in 2026.10.0. On 2026.02.1 the same arm reached `SessionStop`
([2026-08-13](../2026-08-13-everest-d20-ac-contactor-window/notes.md)). It mirrors the close wait
`carsim-on-trigger.sh` was built for: the release now waits for an *open* event as well, and the SIL car
is still holding CP at state C (`draw_power_fixed 0,0`), so their IEC layer never opens the relay. It is
the same in 7 of 7 AC sessions that got this far: four Scheduled after the fix (AC ×2, AC_BPT, AC over
TLS 1.3) and three Dynamic before it.

**Decided: the rig's, and the station is right.** `[V2G20-848]` requires the EVCC to signal CP state B
no later than 250 ms after sending `PowerDeliveryReq(Stop)`. A station that waits for the contactor to
open before answering is therefore waiting for something the car owes it. Our SIL car never left state C.
In this rig the car's CP line belongs to their simulated car, not to our EVCC, so the rig has to make
that change.

**And with it, `-20` AC is green again.** A second `carsim-on-trigger.sh` arms on
`Waiting for contactor is opened` and sends `pause;sleep 600`, which puts their simulated car at
`PLUGGED_IN`, state B ([`scripts/stop-2610.sh`](scripts/stop-2610.sh), `STOP_TRIGGER=1` in
[`scripts/fwd-2610.sh`](scripts/fwd-2610.sh)). In the first session the watcher published 62 ms after
the cue. Their IEC layer moved `Charging->StoppingCharging` about 0,6 s later, `PowerDeliveryRes`
followed 0,75 s after the cue, then `PowerOff`, inside their 1 400 ms. **5 of 5 sessions reach
`SessionStop`**: AC ×2, AC_BPT, Dynamic and AC over mutual TLS 1.3 (`sessions/stop-*`).

The 0,6 s is their simulator's reaction to the CP change, not ours. A real car would have to fit the
whole change into 250 ms, and nothing here measures that for our EVCC, which does not drive a CP line.

## Finding 4 — `-20` Plug & Charge: three defects of ours, and a test PKI that cannot pass its own check

`Evse15118D20` offers PnC in a release for the first time (`ISO15118_chargerImpl.cpp:1684`) and verifies
the signature itself (`d20/crypto.cpp`). The arm is DC over mutual TLS 1.3, with `EvseManager` offering
Contract only and `DummyTokenValidator` accepting whatever reaches it
([`make-d20-pnc.py`](scripts/make-d20-pnc.py)). It needed four runs, and each one got further than the
last.

**1. Our signature declared the wrong algorithm** ([`pnc20-2610.sh`](scripts/pnc20-2610.sh)). All three
sessions: `AuthorizationRes = FAILED_SignatureError`, with their log saying `unsupported SignatureMethod
'…#ecdsa-sha256'` ([`sessions/pnc20-tls13.their-side.log`](sessions/pnc20-tls13.their-side.log)). Our
`-20` car always signed in Josev's form, `ecdsa-sha256` over a SHA-256 digest, because Josev verifies
nothing else and was the only station that had verified us. `[V2G20-2473]` and `[V2G20-2475]` ask for
`ecdsa-sha512` and `sha512`, and that is exactly what their verifier requires. **Ours.**

The grammar was never the problem. With only the two algorithm names and hashes changed, in the WSL copy
([`diag512.sh`](scripts/diag512.sh)), their station accepted the signature. Our `SignedInfo` is encoded
over the standalone xmldsig grammar, the entry point `[V2G20-1449]` names. Their verifier re-encodes
with cbV2G's `iso20_xmldsigFragment`, and the bytes agree. Josev and EVerest are now two independent
verifiers of the same octets.

**2. Their verdict on the contract, and our car ignoring it.** Past the signature, the station checks the
chain against the `-20` contract profile (Tables B.9/B.10). The release's own `MO_LEAF` is P-256:

```
PnC: leaf certificate violates the Annex B profile: public key is neither secp521r1 nor Ed448
```

and it answers `WARNING_CertificateValidationError`, `Finished`. That is the code `[V2G20-2215]` (and
its note on curve mismatch) prescribes. **Their station is right.** Our car read `Finished` and asked
for services, and was answered `FAILED_SequenceError`
([`sessions/pnc20-diag512.s1.flow.md`](sessions/pnc20-diag512.s1.flow.md)). `[V2G20-1583]` gives the car
four options after such a WARNING: authorize again, switch between PnC and EIM, install a contract, or
stop. Moving on to ServiceDiscovery is not one of them. **Ours.**

**3. Our chain went out in the wrong order.** With a P-521 hierarchy of ours
([`mint-mo521.sh`](scripts/mint-mo521.sh)), the station's check moved up the chain:

```
PnC: sub-CA certificate 0 violates the Annex B profile: sub-CA pathLenConstraint does not match its position
```

The PKCS#12 was exported with Sub-CA2 before Sub-CA1. Our loader passed the sub-CAs on in the order the
bag returned them, which was Sub-CA1 first. `SubCertificates` belong leaf-issuer first, and their check
reads pathLen 0 at index 0. **Ours.**

**The fix** (stack `195c4e1`, branch `evcc20-xmldsig-sha512`) has three parts:

- `XmlDsigInteropSign` takes an `XmlDsigSuite`: `Iso20` (`ecdsa-sha512`, `sha512`) by default and
  `JosevSha256` on request. The Josev fixture asks for it, and so do the CLI's `--xmldsig josev` and
  `V2G_INTEROP_XMLDSIG=josev`.
- A WARNING in `AuthorizationRes` ends the session with the code in the message. The car takes the
  "stop" option of `[V2G20-1583]`.
- `Credentials.OrderFromLeaf` orders the sub-CAs by issuer name. The CLI uses it, and so do both interop
  loaders.

`Evcc20FailureHandlingTests.AWarningAtAuthorizationEndsTheSession`, `XmlDsigInteropSignTests` and
`ChainOrderTests` pin them.

**And with it, `-20` PnC is ✅ 3 of 3** ([`pnc521-2610.sh`](scripts/pnc521-2610.sh); the two-session
Scheduled arm, whose first attempt never booted the manager, re-ran as
[`pnc521-sched.sh`](scripts/pnc521-sched.sh)). Scheduled ×2 and Dynamic, 62, 65 and 65 pairs, 3 charge
loops each, every response `OK`. Our P-521 root is trusted through `mo_ca_bundle`, as their root plus
ours ([`config-d20-pnc521-fwd2610.yaml`](configs/config-d20-pnc521-fwd2610.yaml)), and nothing of theirs
is modified. Their side:

```
PnC: contract chain verified locally, 4 certificates, trust anchor /CN=MO521 Root (test)/O=ISO15118ConformanceTests
EVSE IEC PnC Authorization received
ConnSetup | HLC_PWM_5%_ON: 675 ms | D-LINK_READY: 1679 ms | Auth PnC Done: 4351 ms | Total: 7288 ms
```

([`sessions/pnc521-tls13-dyn.their-side.log`](sessions/pnc521-tls13-dyn.their-side.log)).
`AuthorizationRes` is `Ongoing` for five polls while their auth module asks the validator, then `OK`. The
control arm, with the release's P-256 contract, now ends where it should. After the WARNING the car
stops, 4 pairs and no ServiceDiscovery ([`sessions/pnc256-stop.s1.flow.md`](sessions/pnc256-stop.s1.flow.md)).

**What it leaves open:**

- **Their test PKI cannot do `-20` PnC at their own station.** `MO_CERT_CHAIN.p12` and every MO CA are
  `prime256v1`, so the release's own material is refused by the release's own `-20` check. This is a
  remark for upstream, not a defect in the station. August's observation that both test PKIs are P-256
  now has a concrete consequence.
- **The Kotlin and Swift ports still sign `ecdsa-sha256`** and still continue past the WARNING. Their
  `V2GSigner` hashes with SHA-256 by contract. The two corpus recordings they replay
  (`iso20-dc-pnc`, `iso20-dc-eim-certinstall`) are pinned to the Josev suite until the ports move,
  because otherwise their gates would go red.
- **The car's contract is ours, and so is its root.** What this shows is that their station verifies a
  conformant chain and signature. It does not show that a real eMSP chain passes.

## What this run did not do

- ~~**Swift was not built**~~: the macOS CI ran it. Both Swift halves, the car's profile and the corpus
  that follows the station, are green there.
- **`-2` PnC is as blocked as in August**: no validator behind their SIL. `AuthorizationReq` stays at
  `Ongoing` until `FAILED` over TLS, and Contract is stripped without TLS. Not re-investigated. `-20`
  PnC, which uses `DummyTokenValidator` directly, runs (Finding 4).
- The 13 still-present findings of the [release sweep](../2026-11-26-everest-2026.10.0-release/notes.md)
  were not driven on the wire. This run asked only whether the matrix holds.

## Next

Findings 1 to 3 are closed. The car's profile is fixed (WWCP_ISO15118#53, EVSimulatorApp#43), the
station's anchor is fixed (#54, #44), and the AC stop is a rig trigger. Finding 4 is fixed in the C#
car (branch `evcc20-xmldsig-sha512`), and `-20` PnC runs. Still open: the ports' signature and WARNING
handling, and `-2` PnC, which has the same validator wall it had in August.
