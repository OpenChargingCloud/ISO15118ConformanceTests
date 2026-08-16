# ISO 15118-2 Plug & Charge against EVerest, and the charge

**Matrix cell:** EVCC · ISO 15118-2 · Plug & Charge · EVerest

Back to the [interop matrix](../../README.md).

---

**The first ISO 15118-2 Plug & Charge session here that actually charged**, and what stood in the way
was a rig fault of ours rather than PnC or their station. Every earlier `-2` PnC run ended in
`CableCheck`; the cause was the SIL car being plugged in **before** the manager, so its plug-in was
consumed by a station that had since restarted — CP never reaches state C, the contactor never closes,
and `EvseManager` ends in `MREC11CableCheckFault` → `Inoperative`, which poisons every following arm.
The only symptom our side sees is a 60 s `CableCheck` timeout; the only symptom theirs shows is an
**empty car log**.
<br>**81 iterations, 30,16 kWh, 20 % → 70 %, 96 s of wall clock**, over TLS 1.2 with their own MO
credential. The size is arithmetic, not a guess: a preceding arm measured **429 Wh per iteration** and
~134 ms per `CurrentDemand` pair, and the estimate came out 13 % short of the 81 actually run.
<br>**Two clocks that are not the same clock.** One iteration stands for a *simulated* minute
(`ChargeLoopSample.Period`), so that session is 81 simulated minutes in 96 real seconds. Pulling them
apart is `V2G_INTEROP_CHARGE_INTERVAL`, deliberately **not** `Evcc2.PollInterval` — that one also paces
the authorization poll, `ChargeParameterDiscovery` and `CableCheck`, and those intervals are what the
charge-loop pacing findings are measured against.
<br>Their station asked the backend **once**, at `Authorization`; the 81 loops needed no further verdict.
[`…-iso2-pnc-charge`](docs/interop-runs/2026-08-16-everest-iso2-pnc-charge/notes.md).

Their rule *"no `Contract` without TLS"* was the first external check of that requirement against us.
A complete charge and a PnC offer never came in the same session — but that is the **intended EIM path**,
not a wall: their `EvseManager` offers `ExternalPayment` alone once a session is authorized, and their
SIL's dummy token provider swipes at plug-in.
<br>**The backend is ours as of 2026-08-13, and the session now gets past `Authorization`.** EVerest
delegates the contract decision to whoever is wired as `token_validator` — the CSMS in a real
deployment, a constant-returning dummy in their SIL — so
[the arm](tools/interop-everest/contract-validator-arm.sh) supplies it over MQTT as a withheld
standalone module, no patch to theirs. `Accepted` carried the session on to `ChargeParameterDiscovery`
and `CableCheck`; `certificate_status: CertificateRevoked` produced `AuthorizationRes =
FAILED_CertificateRevoked`, unreachable by any configuration of their SIL. The earlier dead end was
one missing connection in the config, not a missing backend: only their two OCPP PnC configs join
`EvseManager`'s `token_provider` to `auth`, and without it the contract token is published and dropped
in silence ([`…-contract-validator`](docs/interop-runs/2026-08-13-everest-contract-validator/notes.md)).
Still `◐`: what nothing checks, here or anywhere, is whether the *contract* is good — their decider is
`DummyTokenValidator`, which returns a constant from its config and never reads the token, so standing in
as the backend proves their plumbing **carries** a verdict, not that anything **forms** one.
<br>**So: does `-2` Plug & Charge work against EVerest? Yes.** The flow runs and their station answers it
correctly in both arms. The `◐` marks a property of their SIL that no session of ours can reach, not a
failure of the session — and the transport it rides on is `✅` one row below, measured by this very
session.
