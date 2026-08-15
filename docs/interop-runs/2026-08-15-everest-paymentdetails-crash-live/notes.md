# 2026-08-15 — the PaymentDetails crash, on a running station

[The filing](../../reports/everest-evsev2g-paymentdetails-crash.md) was written on 2026-08-11 from the
source and an isolated C reproduction, and it carried one open checklist item: *"put it on a running
station"*. This is that. It is the report's last unticked technical line and the reason it has sat at
number **1** in [`sending-order.md`](../../reports/sending-order.md) for four days without going out.

| | |
|---|---|
| Counterparty | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12`), `EvseV2G`, debug build |
| Config | [`config-dc2-pnc-crashprobe.yaml`](config-dc2-pnc-crashprobe.yaml) — their `-2` PnC config with **one line changed**, `tls_security: allow` → `force` |
| Ours | `PaymentDetailsCrashProbe`, an `[Explicit]` probe that builds its own five frames |
| Outcome | **`SIGABRT`, twice** — and the manager does not restart the module, it **terminates every module and exits** |

## What happened

Three arms per run, each on its own connection, and the run is two of them:

```
control (well-formed, untrusted)   ANSWERED   answered OK
probe  (non-empty, unparseable)    no answer  connection closed mid-frame
liveness (control, repeated)       no answer  Connection refused
```

From their side, in their words (both runs identical but for timestamps and pid):

```
13:48:45  [INFO] iso15118_charge :: SelectedPaymentOption: Contract          ← the control
13:48:46  [ERRO] iso15118_charge :: v2g_incoming_v2gtp() (previous message "Payment Details") failed
13:48:46  [INFO] iso15118_charge :: v2g_dispatch_connection exited with -1
13:48:46  [INFO] iso15118_charge :: SelectedPaymentOption: Contract          ← the probe
          iso15118_charger:EvseV2G: …/openssl_util.cpp:775:
              openssl::certificate_subject(const X509*): Assertion `cert != nullptr' failed.
13:48:47  [CRIT] manager :: Module iso15118_charger (pid: 2802) exited with status: 134.
                            Terminating all modules.
13:48:47  [CRIT] manager :: Exiting manager.
```

**`openssl_util.cpp:775` is the assert the filing named**, read out of their source four days earlier
and reached here by one frame. Status **134** is SIGABRT — the debug build, so the assert fires before
`X509_get_subject_name` can; a release build takes the SIGSEGV one line further on.

## The control is the load-bearing arm

Both arms send a certificate their station has no reason to accept. The **only** variable is whether the
bytes parse: the control's leaf is a freshly minted self-signed P-256 certificate, well-formed and
chaining to nothing; the probe's is 64 random bytes with a leading `0xFF`, which cannot begin a DER
`SEQUENCE`. Without the control, a crash could have been anywhere in TLS, SAP or service selection.

The control was answered **`OK`**, which is worth one careful sentence and not more: their station
accepts a contract certificate whose issuer it does not know, at this message. That is not obviously
wrong — `-2` puts the chain check at the signed `AuthorizationReq`, and this project's own station does
the same thing and says so in the filing's three-stack table. It is recorded because it is what the
control measured, not as a finding.

The `v2g_incoming_v2gtp() … failed` line between the two arms is **ours**: the probe closes each
connection after reading its response, and that is their read of the next frame finding it gone.
Reading it as a defect would be reading our own disconnect back as theirs.

## What is new against the filing, and one line of it matters

The draft said the module dies and *"everest's manager may restart the module"*. It does not. The
manager's own two lines are **`Terminating all modules.`** and **`Exiting manager.`** — the whole
charger process tree goes down on one frame from an unauthenticated peer, and the liveness arm three
seconds later gets `Connection refused` rather than a slow answer.

So the severity paragraph the report already had was, if anything, understated, and the correction is
in the direction that makes the report easier to act on rather than louder: there is no supervision
question to argue about, because there is no surviving supervisor.

## Reachability, confirmed rather than argued

The filing reasons that the peer is unauthenticated at this point because `-2` TLS is unilateral. The
run does not have to reason: `tls_security: force`, our probe presents **no client certificate**, and
their station carried it through SAP, `SessionSetup`, `ServiceDiscovery` and
`PaymentServiceSelection(Contract)` to `handle_iso_payment_details` regardless. Five frames from TCP
connect to process death, no credential of any kind.

## Why the probe does not go through our own car

`Evcc2` parses its own contract certificate before sending it — `ContractEmaid()` needs the Common Name
— so it cannot carry bytes that do not parse, and teaching it to would put a fault-injection switch into
the program a user runs. [`PaymentDetailsCrashProbe`](../../../ISO15118ConformanceTests.Simulation/Interop/PaymentDetailsCrashProbe.cs)
therefore builds its own frames over `V2GTPStream`. For a security report that is the better shape
anyway: no state machine is doing anything clever on the way, and the frame on the wire is the frame in
the file. It is **not a fuzzer** and sends exactly one shape — the claim under test is a use-before-check
on one line, not a survey of parser inputs.

## Reproduce

```bash
sed 's/tls_security: allow/tls_security: force/' config-dc2-pnc-ours.yaml > config-dc2-pnc-crashprobe.yaml
~/everest/dist/bin/manager --config .../config-dc2-pnc-crashprobe.yaml
```

`EvseV2G` binds its TCP server at startup and logs the endpoint, so there is nothing to discover — the
difference from `Evse15118D20` that [`sdp-probe.sh`](../../../tools/interop-everest/sdp-probe.sh) exists
for. Take it from their line *"TLS server on eth0 is listening on port …"*, then:

```bash
V2G_INTEROP_SECC='[fe80::…%eth0]:64109' V2G_INTEROP_TLS=1 \
  dotnet test -c Release --artifacts-path ~/wsl-artifacts \
              --filter FullyQualifiedName~PaymentDetailsCrashProbe
```

Expect to restart their manager between runs; it does not come back by itself. `mosquitto` must be up
first, or the manager exits on `Cannot connect to MQTT broker`.

## Artifacts

[`run1/`](run1/) and [`run2/`](run2/) — their manager's full log and our probe's output, ANSI colour
stripped and nothing else changed. [`config-dc2-pnc-crashprobe.yaml`](config-dc2-pnc-crashprobe.yaml) is
the config as run.

Offline gate: **1 405 green**, four assemblies, exit code 0 — unchanged, because an `[Explicit]` test is
filtered out of the run rather than counted as skipped. Worth stating: the first draft of this line said
1 406, on the assumption that adding a test adds to the total. It does not, and the gate says so.

## Next

- **Nothing technical.** The filing's remaining two boxes are *report it through their security policy
  rather than as a public issue* and *post under your own name* — both of them a person's, deliberately.
