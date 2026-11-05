# EVerest PaymentDetails crash: `2026.10.0-rc1` aborts, `main` answers

> **Correction, written the same evening.** This note first concluded that the crash was *"still in
> every release"*. `2026.02.2` shipped the fix hours later, and the defect had already been reported
> independently as [GHSA-qg7p-grpm-cg3f](https://github.com/EVerest/EVerest/security/advisories/GHSA-qg7p-grpm-cg3f). The whole picture — all twenty-four reports
> against the 72 advisories, who got the credit, and why the advisory's second patched version does
> not exist — is in the [advisory cross-check](../2026-11-05-everest-advisory-cross-check/notes.md); that is the document to read for it, not
> this one.
>
> What belongs here is the **margin**, because this run is where it is sharpest:
>
> | | |
> |---|---|
> | `2026.02.2` published, with PR #3047 in it | **15:38:02Z** |
> | the commit asserting *"still in every release"* | **15:38:53Z** |
> | the RC measurement below | 15:33Z, five minutes *before* the release |
>
> Fifty-one seconds — and the margin is not the excuse, it is the point. The inference read the
> release *list*, took `2026.10.0-rc1` for the newest release, and never asked the maintenance line at
> all. This is the sharpest instance of the cross-check's second lesson, *"fixed" needs an axis: fixed
> where*, which ran in both directions the same day: one report wrongly live-in-every-release, four
> wrongly fixed.
>
> A trap for whoever re-measures it: the `2026.02.2` tag was **created** at 12:18:18Z and **published**
> at 15:38:02Z. Reading `created_at` gives a different number and a different story.
>
> **The measurements below are unaffected** — the RC build did abort, `main` did answer. What changed
> is what they are for: not a backport request, but the evidence under the
> [advisory amendment](../../reports/everest-evsev2g-paymentdetails-advisory-amendment.md).

An A/B on one machine within one hour: the same probe and the same station config against two
trees, `2026.10.0-rc1` and `main`. One aborts, one answers correctly.

**2026-11-05.** everest-core **`2026.10.0-rc1`** (`f45ba0d0`, tagged 2026-10-22), built from source in
WSL2 and installed beside the 2026.02.1 rig. Station config: `configs-ours/config-dc2-pnc-crashprobe.yaml`
— the ISO 15118-2 Plug & Charge SIL config with `tls_security: force`, the same file the 2026-08-15 run
used against 2026.02.1. Our side: `PaymentDetailsCrashProbe`, run from the WSL mirror of this
repository so it reaches the station's link-local address directly.

## Why this run exists

[`everest-evsev2g-paymentdetails-crash`](../../reports/everest-evsev2g-paymentdetails-crash.md) claimed two
things. The first — fixed on `main` by their PR #3047 (2026-10-27) — was read out of their source. The
second — that no release carried the fix — was an inference from two dates: `2026.10.0-rc1` from
2026-10-22 looked like the newest release, five days older than the merge. An inference from dates is
not a measurement, and this run replaced it with one.

The inference was also **wrong on its own terms**, and in a way this run could not have caught: it took
the release *list* for the release *situation*, and never asked the maintenance line. `2026.02.2` shipped
the fix hours later. What the run does establish is the half that survives — the specific build the
advisory points the pre-2026.02 line at does not exist, and the newest one that does, aborts.

## The three arms, as the probe printed them

```
control (well-formed, untrusted)       ANSWERED   — answered OK
probe (non-empty, unparseable)         no answer  — InvalidDataException: V2GTP frame:
                                                     connection closed before a full 8-byte header arrived
liveness (control, repeated)           no answer  — SocketException: Connection refused
```

The control is the load-bearing arm: a well-formed self-signed P-256 leaf that chains to nothing, which
the station answered `OK` while staying up. So the whole path — TLS, SAP, `SessionSetup`,
`ServiceDiscovery`, `PaymentServiceSelection(Contract)`, `handle_iso_payment_details` — was reached and
survived. The only variable in the second arm is whether the 64 bytes parse.

## What the station said about itself

```
16:33:22  [INFO] iso15118_charge  :: SelectedPaymentOption: Contract
          iso15118_charger:EvseV2G: .../lib/everest/tls/src/openssl_util.cpp:799:
              openssl::certificate_subject(const X509*): Assertion `cert != nullptr' failed.
16:33:23  [CRIT] manager :: Module iso15118_charger exited unexpectedly, terminating remaining modules...
16:33:23  [WARN] manager :: Crash recovery shutdown completed in 63 ms. Modules that did not shut down
                            cleanly:  iso15118_charger (signal=6) api (signal=15) ...
16:33:23  [CRIT] manager :: Unexpected module exit; manager is exiting.
```

Signal 6 is SIGABRT. The assert is the same one as on 2026.02.1, moved from `:775` to `:799`.

**One thing is new in the RC and worth recording, because it changes nothing.** 2026.02.1 logged
*Terminating all modules* and *Exiting manager*. The RC has crash-recovery machinery instead:
`handle_initiate_graceful_shutdown`, a state transition to `ShutdownFinalizing`, and a summary that names
each module's signal. The shutdown is more orderly and the log is far better — and the outcome is
identical. One frame from an unauthenticated peer still takes the whole charger down, and it still does
not come back: the liveness arm three seconds later got `Connection refused`.

## The mechanism, read in both trees

`certificate_subject` is **unchanged between the RC and `main`** — `assert(cert != nullptr)` at
`openssl_util.cpp:799` in both. So is its caller, `getEmaidFromContractCert` in
`modules/EVSE/EvseV2G/crypto/crypto_openssl.cpp`, which still passes `crt.get()` with no null check.

PR #3047 changed neither. It changed the **order** in `handle_iso_payment_details`. On `main`:

```c
if (err != 0) {                 // err from parse_contract_certificate
    memset(res, 0, sizeof(*res));
    res->ResponseCode = iso2_responseCodeType_FAILED_CertChainError;
    goto error_out;
}

auto cert_emaid = getEmaidFromContractCert(contract_crt);
```

On the RC that `err != 0` block sits **after** the `getEmaidFromContractCert` call, so a leaf that failed
to parse reaches the helper as a null pointer. The report's phrase *"the guard now precedes the use"* is
exactly right, and the fix is a reordering rather than a hardening: the helper would abort again for any
future caller that forgets.

## The same probe against `main`

`main` (`01f36de1`) was built from source the same afternoon — fresh build directory, because the
existing one was configured from `ebcd36d` (2026-08-11), before the modules moved under
`modules/EVSE/` and before the layered package build — and installed to its own prefix, so the stale
`dist-main` stays what it was. Configure 84 s, build 6 min 31 s, install clean, 22 modules, `EvseV2G`
among them. It needs the same `-DEVEREST_DEPENDENCY_ENABLED_LIBOCPP=ON` as the RC.

Same probe binary, same config file, same machine, forty minutes after the RC run:

```
control (well-formed, untrusted)       ANSWERED  — answered OK
probe (non-empty, unparseable)         ANSWERED  — answered FAILED_CertChainError
liveness (control, repeated)           ANSWERED  — answered OK
```

The station logged `SelectedPaymentOption: Contract` three times — all three arms reached
`handle_iso_payment_details` — and then nothing: no assertion, no signal, no module exit. It was
still running when the probe finished.

## What this settles

**Both halves are observations now**, which is the point of running it as one A/B:

| station | tree | the probe arm | the station afterwards |
|---|---|---|---|
| `2026.10.0-rc1` | `f45ba0d0`, 2026-10-22 | no answer, connection closed mid-frame | **gone** — SIGABRT, manager exited |
| `main` | `01f36de1`, 2026-11-05 | **answered `FAILED_CertChainError`** | up, and answered the liveness arm `OK` |

Neither half rests on reading their source any more. What that pair is *for* changed the same evening:
with `2026.02.2` out, it is no longer a backport request but the evidence under the advisory amendment,
for the reason set out in the [cross-check](../2026-11-05-everest-advisory-cross-check/notes.md).

**A detail worth keeping.** The report's *"show it is not universal"* box records that Josev and our
own station both survive these bytes but that **neither answers `FAILED_CertChainError`** for the
unparseable case. Fixed EVerest does. On this one narrow point their `main` is now the most correct of
the three stations we have measured, and the report says so rather than leaving the comparison where it
was.

## Rig notes

Four things cost time and are worth writing down:

1. **No broker, no station.** The first boot died at `Lost connection to MQTT broker`. The Debian
   `mosquitto` package is installed at `/usr/sbin/mosquitto`, which is not on the normal user's `PATH`,
   so `which mosquitto` finds nothing and it looks absent. Start it before the manager.
2. **`rsync` into the WSL mirror needs `--delete`.** Without it a file that moved in the Windows tree
   exists twice in the mirror — here an orphaned `HTTP1/Server/URLMapping_old/URLReplacement.cs`, which
   failed the build with `CS0101: already contains a definition`. `bin/` and `obj/` stay protected
   because an excluded path is not deleted.
3. **`--no-build` after a failed build runs the previous binary and reports success.** The first probe
   attempt did exactly that: `dotnet test` printed `Passed! 1 test` while the compiler had just failed
   with two errors, so the arms came from a stale copy of the probe. The crash it produced was real but
   not attributable. Read the build's exit code before trusting a `--no-build` run.
4. **`dotnet test` swallows `TestContext.Out`.** The arm results — the entire finding of this probe — are
   invisible without `--logger "console;verbosity=detailed"`. The assertion only covers the control arm,
   so the test passes either way and the exit code says nothing about what the station did.

## Artefacts

On the rig, not in this repository: `~/everest/rcprobe/station2.log` (the RC station's log, including
the assertion), `~/everest/rcprobe/probe2.log` (the three arms), `~/everest/rc-station2.sh` and
`~/everest/rc-probe2.sh` (the two RC stages); and `~/everest/mainprobe/` with
`~/everest/build-main.sh` and `~/everest/main-probe.sh` for the `main` arm. All re-runnable.
