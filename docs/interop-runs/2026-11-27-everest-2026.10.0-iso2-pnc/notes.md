# 2026-11-27 — `-2` Plug & Charge against EVerest 2026.10.0, and what the "validator wall" was

**`-2` PnC charges against the release: 30,16 kWh, 81 loops, 20 % → 70 %, over TLS 1.2.** That is
the August result on 2026.02.1, with the same numbers. The forward run of 2026-11-26 had called this
cell blocked by *"no validator behind their SIL"*. That was wrong. The stall came from one connection
missing in our config. Their `DummyTokenValidator` was there all along, and once the token reached it,
it accepted.

A second reading from August falls too. `iso15118CertificateHashData` was absent then, and the note
blamed an empty OCSP cache. In fact their station never verified the contract chain at all:
`verify_contract_cert_chain` defaults to `false`. With it set, the backend receives three hash-data
entries that match the chain we sent.

| | |
|---|---|
| Counterparty | everest-core **2026.10.0** (`74845c57`), `dist-2610`, in WSL |
| Session | `-2` **DC**, forward (our EVCC → their `EvseV2G`), **TLS 1.2**, `Contract` |
| Credential | **theirs**: `dist-2610/etc/everest/certs/client/mo/MO_CERT_CHAIN.p12`, eMAID `UKSWI123456791A` |
| Our side | ISO15118ConformanceTests `c94b663` (stack `23ed486`), built in `~/i15118` |
| Harness | [`fwd-2610v.sh`](scripts/fwd-2610v.sh) (the 2026-11-26 `fwd-2610.sh` plus a `VALIDATOR` knob), [`pnc2-2610.sh`](scripts/pnc2-2610.sh) |

## The arms

Each arm starts a fresh manager. The SIL car is plugged in after the manager, every session.

| arm | config | decider | sessions | result |
|---|---|---|---|---|
| `stock` | [yesterday's](../2026-11-26-everest-2026.10.0-forward/configs/config-dc2-pnc-fwd2610.yaml) | `DummyTokenValidator` | 1 | 366 × `AuthorizationReq`, then `FAILED` after 56 s (`auth_timeout_pnc` 55 s), 371 pairs |
| `hop-dummy` | [`+ token_provider` hop](configs/config-dc2-pnc-hop-fwd2610.yaml) | `DummyTokenValidator` | **2 of 2** | `OK` on the 4th/5th poll; CableCheck, PreCharge, 3 loops, `SessionStop`; 46 and 49 pairs |
| `val-accepted` | hop | ours, `Accepted` | **2 of 2** | the same; 47 and 45 pairs |
| `val-revoked` | hop | ours, `Invalid` + `CertificateRevoked` | **2 of 2** | **`FAILED_CertificateRevoked`**, 8 and 7 pairs |
| `val-verify` | [hop + `verify_contract_cert_chain`](configs/config-dc2-pnc-verify-fwd2610.yaml) | ours, `Accepted` | **2 of 2** | the same as `val-accepted`, and `iso15118CertificateHashData` = 3 entries |
| `val-charge` | hop | ours, `Accepted` | 1 | **81 loops, 30,16 kWh, 20 → 70 %**, 1 min 38 s, 123 pairs |

`val-revoked` first ran 1 of 2. The second session got no SDP answer: their station switches
`Set PWM On` once more while the refused session unplugs, and the arm script took that for the next
plug-in and spent its three probes before `SDP dlink_ready set to true`. The script now waits for
`dlink_ready`, and the re-run went 2 of 2. That was the rig, not their station.

## Finding 1 — the wall was the `token_provider` hop, not the validator

The only difference between `stock` and `hop-dummy` is two lines in `auth.connections.token_provider`:

```yaml
      - module_id: evse_manager
        implementation_id: token_provider
```

`EvseManager` republishes the PnC token through its own `token_provider`. Only their two OCPP PnC configs
connect it to `auth`. Without the hop the token goes to a variable nobody reads, so the session polls
`AuthorizationReq` until `auth_timeout_pnc` and gets `FAILED`. This was known since
[2026-08-13](../2026-08-13-everest-contract-validator/notes.md), and
[`contract-validator-arm.sh`](../../../tools/interop-everest/contract-validator-arm.sh) refuses a config
without it. Yesterday's `-20` PnC config had the hop ([`make-d20-pnc.py`](../2026-11-26-everest-2026.10.0-forward/scripts/make-d20-pnc.py));
the `-2` one, carried over from the August `config-dc2-pnc-ours.yaml`, did not. So the 11-26 note's
*"`-20` uses `DummyTokenValidator` directly"* and *"`-2` has no validator"* describe the same validator
behind two different configs.

**Nothing of theirs changed between the arms**, and the dummy decides as well as our arm does. Our
validator adds the two things the dummy cannot do: it records what their station hands over, and it can
answer `certificate_status`.

## Finding 2 — `iso15118CertificateHashData` needs local chain verification, not an OCSP cache

In `val-accepted` the token carries the eMAID, the chain (3 certificates, 2 694 B) and `connectors`.
It carries no `iso15118CertificateHashData`: the key is missing, not empty. In `val-verify` it holds three
entries:

| `serialNumber` | certificate | `responderURL` |
|---|---|---|
| `3044` | leaf, `CN=UKSWI123456791A` | `https://www.example.com/` |
| `3043` | `PKI-Ext_CRT_MO_SUB2_VALID` | same |
| `3042` | `PKI-Ext_CRT_MO_SUB1_VALID` | same |

`SHA256` throughout, each with `issuerNameHash` and `issuerKeyHash`
([`val-verify.tokens.jsonl`](sessions/val-verify.tokens.jsonl)). The serials match the chain cert for
cert; the responder URL is the AIA of their test PKI.

**Why** (`EvseV2G/iso_server.cpp`, 2026.10.0): the hash data is derived only inside
`if (session.verify_contract_cert_chain)`, after `Verified`, from
`call_get_mo_ocsp_request_data(chain)` (`:1253`–`:1318`). `verify_contract_cert_chain` defaults to
`false` in the manifest. It does in 2026.02.1 and 2026.02.2 as well, and no config of ours ever set it.
Their log says the rest: `Validation of the contract certificate was successful!` appears in `val-verify`
and nowhere else.

**What this corrects.** The [2026-08-13 note](../2026-08-13-everest-contract-validator/notes.md) said
the key was absent *"on a chain that verified"* and blamed
`OcspCache::lookup: not in cache`. Both halves were wrong:

- **The chain was never verified by them.** What happened at `PaymentDetails` was our signature checked
  against the leaf's key. The chain itself was forwarded unverified, which is what the default asks for.
- **The `OcspCache` line belongs to the TLS handshake.** It comes within 51 ms of
  `V2G TCP/TLS connection established` in all eleven sessions, before or after it, and names the same
  hash (`bef42004…`) in every arm: with or without the hop, with or without chain verification. It is their station's lookup of an OCSP response
  to staple for its **own** chain. That is what
  [`everest-evse-security-ocsp-dropped`](../../reports/everest-evse-security-ocsp-dropped.md) is about,
  and that report is untouched by this run.

So the item open-work carried, *"a measurement to take once the OCSP fix landed"*, never depended on that
fix. It is answered: **with local verification on, a backend receives revocation material for every
certificate of the contract chain.**

## Finding 3 — the verdict reaches the wire as before

`Invalid` + `certificate_status: CertificateRevoked` → `AuthorizationRes = FAILED_CertificateRevoked`,
2 of 2, on the 2nd or 3rd poll. The dummy cannot set `certificate_status`, so this path is reachable only
with a real decider. The station stays up and serves the next session.

## What this does *not* say

- **The contract is still validated by nobody.** Our arm answers what it is told. With
  `verify_contract_cert_chain` their station checks the *chain* against its MO root. Neither one
  decides whether the *contract* is good. The cell keeps its `▢` for that, as on 2026.02.1.
- **No OCSP request was made.** The responder URL is `example.com`. What is measured is that the
  hash data is built and handed over, not that anybody asks.
- **`-2` PnC without TLS** stays as on 11-26: their station strips `Contract`, which is `-2`'s own
  *no Contract without TLS* rule. It was not re-run.

## Artifacts

- [`sessions/`](sessions/): per arm, the session flow (`*.flow.md`), our summary lines (`*.ours.txt`),
  their log with colour stripped (`*.their-side.log`), and for the validator arms the validator log and
  every `validate_token` call whole (`*.tokens.jsonl`).
- [`configs/`](configs/): the two configs this run added. `stock` is yesterday's.
- [`scripts/`](scripts/): the arm driver and the batch.

## Next

Nothing on this cell. The tooling lessons are in
[`tools/interop-everest/README.md`](../../../tools/interop-everest/README.md): plug-in detection waits for
`dlink_ready`, and `verify_contract_cert_chain` decides whether hash data is handed over.
