#!/usr/bin/env python3
"""Stand in as the contract-validating backend their SIL does not ship.

EVerest does not decide by itself whether a Plug & Charge contract is good, and that is by design:
`EvseV2G` verifies the chain against the MO root locally and then hands the whole token to whoever is
wired as `token_validator`. In every SIL config that is `DummyTokenValidator`, which returns a value
from its own config file and never looks at the token —

    ret.authorization_status = string_to_authorization_status(config.validation_result);
    -- modules/Testing/DummyTokenValidator/main/auth_token_validatorImpl.cpp:21

— so a run against their SIL cannot answer "does their station carry a contract decision to the wire
correctly", because nothing decides. In a real deployment the decider is the CSMS, reached through
their OCPP module. This script is that backend, minus the CSMS.

It is *not* a patch and not a mock inside their build: it registers over MQTT as the module the config
already names, using their own `everestpy` bindings, and their `Auth` module cannot tell the difference.
Start the station with the module withheld and this in its place:

    manager --config <cfg> --standalone token_validator &
    python3 contract-validator.py --config <cfg>

`--standalone` (`-s`) makes the manager *not* spawn that child and wait for it — the mechanism their own
everest-testing `ProbeModule` uses, here pointed at a module id the stock configs already declare, so
there is nothing to add to the config and no manifest to write. The declared type stays
`DummyTokenValidator`; we simply answer on its topics.

## What it is for

Two halves, and the first is the one worth the setup:

1. **What their station hands over.** Every `validate_token` call is written to a JSONL file whole —
   the eMAID, the contract chain in PEM, and the OCSP hash data their `EvseSecurity` derived from it.
   That is the artifact: it can be checked against the chain we sent, field by field.
2. **What their station does with an answer.** `--status` / `--certificate-status` drive the reply, and
   the reply reaches the `-2` wire: a rejected PnC token becomes `AuthorizationRes.ResponseCode`, and a
   `certificate_status` of `CertificateRevoked` specifically becomes `FAILED_CertificateRevoked`
   (`iso_server.cpp:1217-1225`). With the dummy in place that branch is unreachable — it never sets
   `certificate_status`, so `evse_managerImpl.cpp:386` fills in `value_or(Accepted)`.

## What it does not test

**Not the chain validation.** That already happened, locally, in `iso_server.cpp:1049`, before the token
was ever built — and we have measured that it works (2026-08-03). By the time this script sees a token,
the chain has been accepted. Only with their `central_contract_validation_allowed` set does a chain that
*failed* locally get forwarded, and then the hash data is absent: `iso15118CertificateHashData` is only
filled on the success branch (`iso_server.cpp:1108-1110`).

So what this measures is the *handover* and the *carriage of the verdict*, not their crypto. Which is
the whole of what a backend is answerable for.

## Three traps, all paid for

- **The stock SIL configs cannot deliver a PnC token to `auth` at all**, so this script would sit idle
  through a perfect Plug & Charge session and look broken. `EvseManager` republishes the contract token
  through its own `token_provider` implementation (`EvseManager.cpp:1044-1058`), and *only*
  `config-sil-ocpp-pnc.yaml` and `config-sil-ocpp201-pnc.yaml` connect that implementation to `auth`.
  Every other config — `config-sil-dc.yaml`, `config-sil-dc-tls.yaml` and anything derived from them —
  wires `auth.token_provider` to `DummyTokenProvider` alone, so the token is published to a variable
  nobody subscribed to and the session runs to `auth_timeout_pnc` and answers `FAILED`. Add

      token_provider:
      - module_id: token_provider
        implementation_id: main
      - module_id: evse_manager          # <- without this there is no PnC token, ever
        implementation_id: token_provider

  `contract-validator-arm.sh` refuses to start a config that lacks it rather than let it look like a
  station that never authorizes.
- **`tariff_messages` is required** in `ValidationResult` (`types/authorization.yaml:159-161`, marked
  required expressly so the vector default-initialises). A reply without it fails schema validation, the
  result is dropped, and `Auth` waits for an answer that was sent — indistinguishable from a validator
  that never ran. Always emit it, empty.
- **`id_token` became an object** on 2026.02.1 (`{"value": …, "type": "eMAID"}`); older images carry a
  bare string. Read defensively, as below, or a run against the other image logs `None` for every eMAID.
"""
import argparse
import json
import os
import sys
import threading
import time
from datetime import datetime, timezone

DIST = os.environ.get("DIST", os.path.expanduser("~/everest/dist"))
sys.path.insert(0, os.path.join(DIST, "lib", "everest", "everestpy"))

# Redirected into a log file, stdout is block-buffered, so the registration lines appear only once
# enough output has accumulated -- for the first ten minutes of a run the log looks like a validator
# that never registered. Line-buffer instead of remembering flush= on every print.
sys.stdout.reconfigure(line_buffering=True)

from everest.framework import Module, RuntimeSession  # noqa: E402  (after the path insert, necessarily)

AUTH_STATUS = ["Accepted", "Blocked", "ConcurrentTx", "Expired", "Invalid", "NoCredit",
               "NotAllowedTypeEVSE", "NotAtThisLocation", "NotAtThisTime", "Unknown",
               "PinRequired", "Timeout"]
CERT_STATUS = ["Accepted", "SignatureError", "CertificateExpired", "CertificateRevoked",
               "NoCertificateAvailable", "CertChainError", "ContractCancelled"]


def stamp():
    return datetime.now(timezone.utc).strftime("%H:%M:%S.%f")[:-3]


def emaid_of(token):
    """`id_token` is an object on 2026.02.1 and a bare string before it. Both, or log nothing."""
    tok = token.get("id_token")
    if isinstance(tok, dict):
        return tok.get("value"), tok.get("type")
    return tok, None


def describe(token):
    """One readable line per call. The JSONL keeps everything; this is what a run log shows."""
    value, kind = emaid_of(token)
    parts = [f"{token.get('authorization_type')} {kind or '?'}={value}"]

    pem = token.get("certificate")
    if pem:
        parts.append(f"chain={pem.count('BEGIN CERTIFICATE')} certs, {len(pem)} B")
    else:
        parts.append("chain=absent")

    hashes = token.get("iso15118CertificateHashData")
    if hashes:
        parts.append(f"ocsp_hash_data={len(hashes)}")
    else:
        # Absent for EIM, and absent for PnC whenever local verification did not succeed --
        # in central-validation mode that is the interesting case, not a fault.
        parts.append("ocsp_hash_data=absent")

    if token.get("connectors") is not None:
        parts.append(f"connectors={token['connectors']}")
    return "  ".join(parts)


class Validator:
    def __init__(self, args):
        self.args = args
        self.calls = 0
        self.lock = threading.Lock()

    def policy(self):
        """Re-read on every call, so an answer can be changed between sessions without a restart.

        A missing or malformed file falls back to the command line rather than to an exception: a
        validator that dies mid-run leaves their Auth module waiting forever, which is a much worse
        failure than answering the previous verdict once more.
        """
        status, cert = self.args.status, self.args.certificate_status
        if self.args.policy and os.path.exists(self.args.policy):
            try:
                with open(self.args.policy, "r") as f:
                    doc = json.load(f)
                status = doc.get("status", status)
                cert = doc.get("certificate_status", cert)
            except (OSError, ValueError) as e:
                print(f"{stamp()} policy file unreadable ({e}) -- falling back to the command line",
                      file=sys.stderr)
        return status, cert

    def handle(self, args):
        token = args["provided_token"]
        with self.lock:
            self.calls += 1
            n = self.calls

        status, cert = self.policy()
        print(f"{stamp()} #{n} <- {describe(token)}", flush=True)

        if self.args.delay:
            # For driving their auth timeout: EvseV2G answers Ongoing until auth_timeout_pnc and then
            # FAILED (iso_server.cpp:1207-1216), a path the dummy's 0,25 s can never reach.
            print(f"{stamp()} #{n}    holding the answer for {self.args.delay} s", flush=True)
            time.sleep(self.args.delay)

        result = {"authorization_status": status, "tariff_messages": []}
        if cert:
            result["certificate_status"] = cert

        with open(self.args.log, "a") as f:
            f.write(json.dumps({"n": n, "t": stamp(), "token": token, "result": result}) + "\n")

        print(f"{stamp()} #{n} -> {status}"
              + (f" / certificate_status={cert}" if cert else " / certificate_status omitted")
              + f"   (logged to {self.args.log})", flush=True)
        return result


def main():
    p = argparse.ArgumentParser(description="Contract-validating backend for an EVerest SIL station.")
    p.add_argument("--config", required=True, help="the same config file the manager was given")
    p.add_argument("--module-id", default="token_validator",
                   help="config key of the validator (default: token_validator)")
    p.add_argument("--impl-id", default="main", help="implementation id (default: main)")
    p.add_argument("--status", default="Accepted", choices=AUTH_STATUS)
    p.add_argument("--certificate-status", default=None, choices=CERT_STATUS,
                   help="omitted by default -- their EvseManager then fills in Accepted")
    p.add_argument("--delay", type=float, default=0.0, help="seconds to hold each answer")
    p.add_argument("--policy", default=None,
                   help='JSON file, re-read per call: {"status": …, "certificate_status": …}')
    p.add_argument("--log", default="contract-validator.jsonl")
    args = p.parse_args()

    v = Validator(args)
    session = RuntimeSession(DIST, args.config)
    mod = Module(args.module_id, session)
    mod.say_hello()
    mod.implement_command(args.impl_id, "validate_token", v.handle)

    print(f"{stamp()} registered as {args.module_id}/{args.impl_id} against {DIST}")
    print(f"{stamp()} answering {args.status}"
          + (f", certificate_status={args.certificate_status}" if args.certificate_status else "")
          + (f", policy from {args.policy}" if args.policy else ""))

    ready = threading.Event()
    mod.init_done(ready.set)
    print(f"{stamp()} ready sent -- the manager can now start the remaining modules")

    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        print(f"\n{stamp()} {v.calls} validate_token call(s), full tokens in {args.log}")


if __name__ == "__main__":
    main()
