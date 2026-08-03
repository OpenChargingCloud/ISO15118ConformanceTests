# The sweep for the fourth (2026-08-03)

Three live findings in three days had the same shape — **a value taken from our own assumption where the
protocol supplies one** — so the roadmap asked for a sweep rather than a fourth counterparty:

| | found by | what was assumed |
|---|---|---|
| [Response-code handling](roadmap.md#-response-code-handling-2026-08-01) | eVDriveFlow | that a response meant success |
| [The ongoing-poll deadline](roadmap.md#-the-ongoing-poll-deadline-2026-08-02) | EVerest | that `Ongoing` would end |
| [The energy transfer mode](roadmap.md#-reading-the-energy-transfer-mode-instead-of-assuming-it-2026-08-03) | EVerest | that the station wanted three-phase AC |

All three were invisible to every oracle here for one reason: **our own SECC supplies exactly what our
own EVCC assumes.** A constant and a field agree until a foreign station disagrees, and loopback,
corpus and replay are all made of our own parts.

This is what a deliberate search for the fourth found. It is a code audit, not a run: the point was to
stop paying a counterparty to find these one at a time.

## Method

Every place either EVCC puts a value into a request, checked against the question *did the station tell
us this already?* — with the answer written down either way, because a sweep that records only its hits
cannot be repeated or trusted.

## Found — four, all fixed

### 1. ISO 15118-2: the ChargeService id  ⚠️ real

`PaymentServiceSelectionReq` selected `ServiceID: 1`, a literal, while `ServiceDiscoveryRes` had just
carried `ChargeService.ServiceID`. Ours is 1 and every counterparty's has been 1 — which is precisely
why it was still a literal. A station numbering its charge service anything else would have been sent a
selection for a service it never advertised.

### 2. ISO 15118-20: the energy-transfer service, fallback across message sets  ⚠️ real

`SelectEnergyTransferService` fell back to `offered[0]` when nothing matched. For a DC car at an AC-only
station that selects the **AC** service and then sends `DC_ChargeParameterDiscoveryReq` — a message-set
mismatch refused two exchanges later, by which point the error no longer names the cause.

The fix is narrower than "never fall back", and that matters: the fallback exists on purpose, for
**MCS**, where service ids 8/9 ride the *DC* message set. `Secc20McsTests` documents a megawatt truck at
an ordinary DC charger taking service 2 — correct, because the wire shape is identical. So the rule is
now *fall back within the message set you speak*: `DrivableEnergyServiceIds` (DC: 2/6/8/9, AC: 1/5)
alongside `PreferredEnergyServiceIds`, and a refusal naming the catalogue when neither has anything.

An earlier attempt at this removed the fallback entirely and broke that MCS test. The test was right.

### 3. ISO 15118-20: EIM assumed available  ⚠️ real, minor

`BuildAuthorizationReqEncoder` checked `AuthorizationServices` for **PnC** and then fell through to EIM
without checking it was offered. A station advertising PnC only would get an EIM request and answer
`FAILED`. It now refuses at `AuthorizationSetup`, one message earlier, naming what was on offer and
whether we hold a contract certificate.

### 4. SupportedAppProtocol: the accepted SchemaID  ⏳ latent

The handshake checked the response code and ignored `SchemaID` — *which* of the offered protocols the
station accepted. Harmless while the offer is a single entry, which it is today, and a silent protocol
mismatch the moment [it is not](roadmap.md#what-remains). Checked now rather than on the day a second
entry is added, because that is exactly the day nobody re-reads this function.

## Checked and correct — the other half of the sweep

| Place | Reads the station's value? |
|---|---|
| -2 `PaymentOption` (Contract vs ExternalPayment) | ✅ `discovery.PaymentOptionList` |
| -2 `SAScheduleTupleID` in PowerDelivery | ✅ chosen from the offered tuples, by lowest average price |
| -2 `ChargingProfile` | ✅ shaped to the chosen tuple's PMaxSchedule, entry for entry |
| -2 `MeteringReceiptReq` | ✅ sent only when the station set `ReceiptRequired`, echoing its `MeterInfo` |
| -2 signed `SalesTariff` verification | ✅ digest over the station's own re-encoded fragment |
| -20 `AuthorizationServices` → PnC | ✅ (EIM was the gap, above) |
| -20 `CertificateInstallationService` | ✅ certificate installation runs only when offered |
| -20 `ParameterSetID` (ControlMode) | ✅ since the Dynamic work, with a named refusal |
| -20 `ScheduleTupleID` in the EVPowerProfile | ✅ from `ScheduleExchangeRes`, falling back to 1 only when the station sent no Scheduled control mode |
| -20 `MaximumSupportingPoints: 12` | ✅ not the station's to say — schema-bounded to [12, 1024], and 12 is the floor |
| -2/-20 EV limits (voltage, current, energy, SOC) | ✅ ours by definition: they describe the car |

## What did not come out of it

**No fifth.** That is worth stating, because a sweep with an open-ended target is otherwise never
finished. The remaining request fields either describe the vehicle, or are schema constants, or are
already read from the peer.

**The ports are untouched.** Kotlin and Swift carry none of these four, as they carry neither Dynamic nor
the transfer-mode selection — see [What remains](roadmap.md#what-remains). Their EVCCs are validated
against the trace corpus, and no corpus entry contains a station that answers differently from ours,
which is the same blind spot one layer along.

**Two of the four could still only be found this way.** Numbers 1 and 4 produce no failure against any
station that behaves like ours — no live run would have caught them, and no test built from our own
parts either. That is the argument for repeating this sweep after the next protocol feature lands rather
than waiting for a counterparty to embarrass us.
