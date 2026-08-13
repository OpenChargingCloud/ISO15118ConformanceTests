# 2026-08-13 — EVerest `-20` AC over **mutual TLS 1.3**

**The AC cells are conformant now, not merely complete.** Four ISO 15118-20 AC sessions — two `AC`, two
`AC_BPT` — from our EVCC on Windows to `Evse15118D20` over mutual TLS 1.3, with their station verifying
our vehicle certificate and our EVCC validating their chain against a supplied anchor. 15/15 `OK`.

This is the second half of [the morning's run](../2026-08-13-everest-d20-ac-contactor-window/notes.md).
That one got AC past `PowerDelivery` for the first time; every session in it was plain TCP, which
`[V2G20-1237]` (car) and `[V2G20-2356]` (station) both forbid and Table 5 puts `-20` in the TLS 1.3 row
alone. **A green cell over a transport the standard does not allow is worth saying out loud, and it is
why this run existed.**

| | |
|---|---|
| Counterparty | [EVerest](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12`), native build in WSL |
| Config | [`config-ac20-tls-ours.yaml`](config-ac20-tls-ours.yaml) — the morning's AC config plus `ENFORCE_TLS` and `enforce_tls_1_3: true`, two lines |
| Ours | `EverestInteropTests.OurEvcc_…`, `V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=ac`, **BouncyCastle** backend |
| PKI | regenerated with their own `create_certs.sh -v iso-20`, installed wholesale, **restored afterwards** |
| Outcome | **AC 2/2, AC_BPT 2/2**, all four `Handshake complete!` + `Verify certificate result is okay` |

## The measurement

The contactor window is unchanged by TLS, which was the open question — a handshake in front of the
session could have eaten the margin, and it does not:

| session | service | window opens | `PowerOn` | `PowerDeliveryRes` |
|---|---|---|---|---|
| 1 | `AC` | 19:34:59.582 | +1048 ms | **+1089 ms** `OK` |
| 2 | `AC` | 19:36:30.104 | +1015 ms | **+1060 ms** `OK` — recorded |
| 3 | `AC_BPT` | 19:37:34.418 | +832 ms | **+878 ms** `OK` |
| 4 | `AC_BPT` | 19:38:42.266 | +840 ms | **+886 ms** `OK` |

Against the 3 000 ms `CONTACTOR` timeout, and within 60 ms of the plain-TCP figures from the morning
(783–1005 ms). The handshake costs ~180 ms and it is spent *before* `PowerDelivery`, so it never enters
the window.

Their side, all four sessions:

```
Start TLS server [fe80::215:5dff:feda:2c1f%eth0]:50000
Client supports TLS1.3: Change verify mode to SSL_VERIFY_PEER and SSL_VERIFY_FAIL_IF_NO_PEER_CERT
Handshake complete!
Verify certificate result is okay
```

[`flow.md`](everest-iso15118-20-ac-forward.flow.md) from our own recorder — 15 pairs, every code `OK`,
and `SessionTrace.Build` accepted the session.

## Three things that cost an attempt each, all ours

**The client chain was one certificate short.** The first attempt exported `vehicle.p12` with
`VEHICLE_SUB_CA2` only, and their station answered
`tls_process_client_certificate:certificate verify failed` — correctly: the path to the V2G root it
trusts runs `VEHICLE_LEAF ← VehicleSubCA2 ← VehicleSubCA1 ← V2GRootCA`, and without SubCA1 it cannot be
built. Kept as [`their-charger.chain-too-short.log`](their-charger.chain-too-short.log) because the
station's own error message is the clearest statement of what a `-20` EV owes.

**The leftover credentials from 2026-08-06 do not fit and cannot be made to.** Their V2G root is
`5E:77:33:20…`, the installed one was `88:F8:C2:D5…` — because that run *restored* the pristine tree
afterwards, deliberately, so later PnC runs would not stand on generated material. The consequence is
that a `-20` TLS run always begins by regenerating: their PKI ships no vehicle credential at all, and
`Evse15118D20` demands one the moment the client offers 1.3. **This run backed the tree up first and
restored it at the end**; the pristine root is back and the backup is at
`~/everest/pki-backup-acwin-260813-172842.tgz`.

**Their SDP is one-shot per session.** A probe that is not followed by a connection leaves the station
answering *"Ignoring sdp request message because a session is already created and running"* to every
later probe — so probe and connect belong in one sequence, and a failed attempt needs a manager restart
rather than another probe. Two attempts died on this before it was read out of their log.

## Seen in passing — the loop-shutdown defect, live

The refused handshake in the first attempt produced their own:

```
[ERRO] Shutdown loop() because of: Failed to SSL_accept(): 1: …certificate verify failed
```

That is [`everest-loop-shutdown.md`](../../reports/everest-loop-shutdown.md) happening: one refused
handshake and the whole V2G event loop is gone for the life of the process, sockets still bound, the
station silent. **Not a new finding and not written up again** — but it is the first time this project
has hit it while doing something else, which is worth one line in the report's favour when it is sent.

## Reproduce

```bash
bash tls-pki-setup.sh                                  # back up, regenerate, install, export
~/everest/dist/bin/manager --config ~/everest/configs-ours/config-ac20-tls-ours.yaml &
CP_AT_PLUGIN=0 bash ~/everest/sil-car.sh &             # hold at state B
SECURITY=00 bash ~/everest/sdp-probe.sh eth0           # 00 = TLS; note the port, connect promptly
bash tools/interop-everest/carsim-on-trigger.sh --watch charger.log &

V2G_INTEROP_SECC=127.0.0.1:15141 V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=ac \
V2G_INTEROP_TLS=1 V2G_INTEROP_TLS_TRUST=…/trust.pem V2G_INTEROP_TLS_CLIENT=…/vehicle.p12:123456 \
V2G_TLS_BACKEND=BouncyCastle \
  dotnet test -c Release --no-build --filter FullyQualifiedName~OurEvcc_AgainstTheirEvseV2G_RunsToCompletion
```

`V2G_INTEROP_BPT_FIRST=1` for the AC_BPT half. `V2G_TLS_BACKEND=BouncyCastle` is the Windows half of
[the 2026-08-06 finding](../2026-08-06-everest-iso20-tls13-windows/notes.md): Schannel will not present
a client chain whose root the system store does not trust. **Restore the PKI afterwards.**

## Next

- The `-2` AC cell is still plain TCP, and `-2` AC over TLS 1.2 has never run against this counterparty.
- The `-20` AC **reverse** direction — their `PyEvJosev` against our SECC — remains the one AC shape
  never attempted, over any transport.
