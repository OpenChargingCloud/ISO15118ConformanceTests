# 2026-08-13 — standing in as the contract-validating backend

**Their station's Plug & Charge verdict is now ours to set, and it reaches the ISO 15118-2 wire.
`AuthorizationRes = FAILED_CertificateRevoked`, measured — a response code nothing in their SIL could
produce before. And the reason every earlier PnC run dead-ended turned out to be one missing line of
configuration, not a missing backend.**

| | |
|---|---|
| Counterparty | [EVerest](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12`), native WSL2 build |
| Ours | harness `f464baa`, stack `da76eee` |
| Config | [`config-dc2-pnc-validator-ours.yaml`](config-dc2-pnc-validator-ours.yaml) — `config-dc2-pnc-ours.yaml` plus **one connection** |
| Credential | **theirs**: `tests/ocpp_tests/test_sets/everest-aux/certs/client/mo/MO_CERT_CHAIN.p12`, password `123456` |
| Session | -2 DC over **TLS 1.2**, forward (our EVCC → their `EvseV2G`) |
| Arm | [`contract-validator-arm.sh`](../../../tools/interop-everest/contract-validator-arm.sh) + [`contract-validator.py`](../../../tools/interop-everest/contract-validator.py) |
| Artifacts | [`token-accepted.jsonl`](token-accepted.jsonl), [`token-revoked.jsonl`](token-revoked.jsonl), [`flow.accepted.md`](flow.accepted.md), [`flow.revoked.md`](flow.revoked.md) |

## What was actually missing

`docs/open-work.md` has carried this since 2026-08-03 as *"their SIL has no contract-validating
backend, so nothing decides whether the contract is good."* Half of that was right and half of it hid
something simpler.

**Right:** the decider is `DummyTokenValidator`, which returns a value from its own config file and
never looks at the token —

```cpp
ret.authorization_status = types::authorization::string_to_authorization_status(config.validation_result);
// modules/Testing/DummyTokenValidator/main/auth_token_validatorImpl.cpp:21
```

**Hidden:** in every plain SIL config the contract token never reaches that validator either. The path
is `EvseV2G` → `require_auth_pnc` → `EvseManager` → its own `token_provider` implementation → `Auth`,
and the last hop is a *connection in the config file*:

```
      token_provider:
      - module_id: token_provider
        implementation_id: main
      - module_id: evse_manager          # <- only in the two OCPP PnC configs
        implementation_id: token_provider
```

`grep -rn 'implementation_id: token_provider' config/*.yaml` in everest-core 2026.02.1 returns exactly
four lines, all of them in `config-sil-ocpp-pnc.yaml` and `config-sil-ocpp201-pnc.yaml`. Every other
config — `config-sil-dc.yaml`, `config-sil-dc-tls.yaml`, and ours derived from them — publishes the
Plug & Charge token to a variable nobody subscribed to.

**That is invisible from every direction.** `PaymentDetailsRes` is `OK`, the contract signature
verifies, `EvseV2G` calls `publish_require_auth_pnc` — and then the session polls `AuthorizationReq`
until `auth_timeout_pnc` and answers `FAILED`, with no token in any log and no error anywhere.
Measured twice before the cause was found: 55 s and 56 s of `EVSEProcessing = Ongoing`, **366
`AuthorizationReq` polls**.

This is **not a defect**. PnC in EVerest is expected to be wired with OCPP, and their plain SIL
configs are simply not PnC configs. It is a fact about driving their harness, and it belongs in the
README beside the other four.

**It also corrects our own record.** [`pnc-authorize.sh`](../../../tools/interop-everest/pnc-authorize.sh)
says their auth module answers `NO_CONNECTOR_AVAILABLE` "even with the connector free". That is a true
observation of a different thing: `EvseV2G` builds the token *without* `connectors`
(`iso_server.cpp:1118-1125`) and `EvseManager` adds them on the way through
(`EvseManager.cpp:1047`) — so hand-forwarding the raw `require_auth_pnc` payload skips the hop that
makes it routable. That script is now redundant rather than blocked: with the connection in place,
EVerest forwards its own token correctly and no script needs to.

## The arm

No patch, no new module, no manifest. The manager is started with the validator **withheld** and the
module id it already declares is answered over MQTT by a Python process using their own `everestpy`:

```bash
manager --config <cfg> --standalone token_validator &
python3 contract-validator.py --config <cfg> --policy policy.json
```

```
[INFO] manager :: Not starting standalone module: token_validator
[INFO] manager :: Modules started by manager are ready, waiting for standalone modules.
[INFO] manager :: Standalone module token_validator initialized.
[INFO] evse_manager :: 🌀🌀🌀 Ready to start charging 🌀🌀🌀
```

`--standalone` is the mechanism their own `everest-testing` `ProbeModule` uses; pointing it at a module
id the config already carries is what removes the rest of the setup. The declared type stays
`DummyTokenValidator` and its config keys go unread.

## What their station hands over

One `validate_token` call per PnC session, logged whole:

```
#1 <- PlugAndCharge eMAID=UKSWI123456789A  chain=3 certs, 2698 B  ocsp_hash_data=absent  connectors=[1]
```

The eMAID is read off the contract leaf's CN, the chain is the leaf plus `MO_SUB_CA2` and `MO_SUB_CA1`
in PEM, and `connectors` was added by `EvseManager`.

**`iso15118CertificateHashData` is absent** — the key is not in the object at all, on a chain that
verified. `EvseV2G` fills it from `call_get_mo_ocsp_request_data(contract_cert_chain_pem)` on the
success branch (`iso_server.cpp:1108-1110`), and their own log says why it came back empty:

```
[ERRO] iso15118_charge :: OcspCache::lookup: not in cache: d8817041a94bb65646ea392c812fcb4978ae4cf6
```

Nothing has ever stapled OCSP for this chain in this SIL, so there is nothing to hand over. **Not
filed, and deliberately so:** it is the expected consequence of an unstapled test PKI, and its
interesting half — that a backend is therefore handed no revocation material at all — is already the
subject of [`everest-evse-security-ocsp-dropped`](../../reports/everest-evse-security-ocsp-dropped.md)
from the other end of the same pipe. Worth re-measuring once that one lands.

## What their station does with a verdict

Two sessions, same station, same credential, differing only in the JSON file the validator re-reads on
every call.

| verdict returned | `AuthorizationRes` | then |
|---|---|---|
| `Accepted` | `OK` on the 3rd poll | `ChargeParameterDiscovery`, `CableCheck` — 408 frames, stops for want of hardware |
| `Invalid` + `certificate_status: CertificateRevoked` | **`FAILED_CertificateRevoked`** on the 2nd poll | session ends |

The first is **the first -2 Plug & Charge session in this project to get past `Authorization` against
any EVerest station.** Until today the row in the matrix stopped exactly there.

The second is the one worth the arm. `iso_server.cpp:1217-1225` maps a rejected PnC authorization to
`FAILED_CertificateRevoked` when `session.certificate_status` says revoked, and to plain `FAILED`
otherwise — and **`DummyTokenValidator` cannot set `certificate_status` at all**. Its config carries
`validation_result` and nothing else, so `evse_managerImpl.cpp:386` fills in
`certificate_status.value_or(Accepted)` and that branch is dead. A plain `FAILED` was always reachable
by editing their config; `FAILED_CertificateRevoked` was not reachable by any configuration of their
SIL, and it is now measured.

## Traps paid for on the way

- **`tariff_messages` is required** in `ValidationResult` (`types/authorization.yaml:159-161`). A reply
  without it fails schema validation and is dropped, and `Auth` then waits for an answer that was
  sent — indistinguishable from a validator that never ran.
- **Block-buffered stdout.** Redirected to a file, the validator's registration lines do not appear
  until enough output accumulates, so a healthy run looks like a process that never registered. The
  script line-buffers.
- **One EIM token poisons the next PnC session.** A token published by hand during setup started a
  transaction; the PnC session that followed ran to `auth_timeout_pnc` without ever producing a token.
  Restart the station between arms, not just the validator.
- **`pkill -f` on a pattern the wrapper command contains kills the wrapper**, and through
  `wsl.exe -- bash -lc` that is the shell running the `pkill` — the command exits 15 with the station
  still up. Kill the validator by pid and the station on `--prefix`, and read `pgrep -af` afterwards.
  Cost three teardowns here; the README already carried the rule and it was still easy to trip.
- **`config-dc2-pnc-ours.yaml`'s `token_provider.connector_id: 2` is deliberate**, not a typo — a
  connector that does not exist means *do not swipe*, so the EIM token never lands and `Contract` is
  still on offer when the car asks. `everest-cross-validation.md:122` says so; it was nearly
  "corrected" here.

## Next

- **A metering receipt under a decided contract.** The one PnC message type still unexercised against
  them, and now there is a session that gets far enough to carry one.
- **The other verdicts.** `Blocked`, `Expired`, `NoCredit` all map to plain `FAILED` on the `-2` wire by
  reading; only `Accepted` and `CertificateRevoked` are measured.
- **`-20`.** `Evse15118D20` has its own `authorization_response` handler
  (`Evse15118D20/charger/ISO15118_chargerImpl.cpp:811`) whose `certificate_status` parameter is
  `[[maybe_unused]]`. The same arm reaches it; whether the verdict survives to the `-20` wire is a
  separate measurement and the signature above says it may not.
