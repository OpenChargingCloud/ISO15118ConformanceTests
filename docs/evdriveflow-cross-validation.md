# eVDriveFlow cross-validation, in detail

The long form of the eVDriveFlow column in the [interop matrix](../README.md#the-interop-matrix--who-we-test-against-and-what-happened):
every scenario that has run against **EDF-Lab/eVDriveFlow**, what each one caught, and what stays out of
reach.

It is the shortest of the four columns and the highest yield per exchange. Thirteen messages in one
direction and four in the other found **one defect in this project that every other oracle was
structurally blind to** — the one that started the [assumed-values sweep](assumed-values-sweep.md) — plus
three in theirs. Read alongside [Josev](josev-cross-validation.md) (the other independent codec) and
[EVerest](everest-cross-validation.md) (the independent charger).

Tooling: [`tools/interop-evdriveflow/`](../tools/interop-evdriveflow/README.md). Runs:
[`2026-08-01-edf-iso20-dc-notls`](interop-runs/2026-08-01-edf-iso20-dc-notls/notes.md) (forward) and
[`2026-08-01-edf-iso20-dc-dynamic-reverse`](interop-runs/2026-08-01-edf-iso20-dc-dynamic-reverse/notes.md)
(reverse).

---

## What eVDriveFlow is, and why it was picked

**EDF R&D's Python implementation of ISO 15118-20 Edition 1** (MIT, `60249c3`, 2023-04-17). It plays both
roles, and it encodes with **OpenEXI/Nagasena** — the jars sit in their `shared/lib/`, which is why the
install needs a JDK. That makes it the project's **second independent codec** after Josev's EXIficient,
and the first one to meet -20 at session level.

It was chosen for a combination nothing else here could witness: **-20 Ed. 1 + DC bidirectional + Dynamic
control mode + mutual TLS 1.3**. `docs/pki-model.md` pins -20 to TLS 1.3 with a mutual handshake, and
until this counterparty our own tests were the only thing that had ever checked we do it right — a second
implementation that *requires* it is an oracle rather than a second opinion from ourselves.

**None of those four has been reached.** The wall is below, and it is theirs. What arrived instead was the
codec cross-check and one finding worth the whole harness.

---

## What has run

- **Forward, -20 DC EIM over plain TCP — 13 exchanges, aborted in the charge loop.** SupportedAppProtocol
  through PowerDelivery all `OK`, then `DC_ChargeLoopReq` went unanswered. **Every frame we sent was
  decoded by an independent EXI implementation**: twelve of our -20 messages across CommonMessages and the
  DC set, read without a single decoding complaint, and their responses round-tripped through ours the
  same way. Not a byte diff — we never encoded the same content with both and compared octets — but a
  working independent decoder in both directions, at -20 session level, which nothing else had given.

- **Reverse, their EV against our SECC — 4 exchanges, and three firsts.** Their EV said hello, asked how to
  authorize, and left. What it produced anyway: the **first live SDP discovery** in this project against a
  non-Josev peer, the **first `trace.json` recorded from a real counterparty**, and finding 4.

  Their EV has no fixed-endpoint option at all — `start_new_session` connects strictly to whatever
  discovery returned — so the counterparty-agnostic SDP shim from the Josev harness answered it unchanged,
  and a station running **on the Mac** was discovered by an EV running **in a Linux container**. The
  reverse direction had been documented as needing SDP on a shared L2; it does, but the station itself
  does not have to be there.

---

## What it found in **us** — the response code that was never read

This is the finding that justifies the harness, and it is worth stating in full because it is the cleanest
example this project has of what a foreign stack is *for*.

`DC_CableCheckRes` came back with **`ResponseCode = FAILED`** — and our EVCC carried on. PreCharge,
PowerDelivery, into the charge loop. The cable-check loop looked only at `EVSEProcessing`, and `Expect<T>`
checked the message *set* and *type*:

```csharp
if (res.EVSEProcessing == Dc20.Processing.Finished) break;   // …and nothing else
```

There was **no `ResponseCode` check anywhere in the -20 EVCC path**. A station could have answered FAILED
to every message of a session and our car would have driven it to completion.

**Why nothing here could have found it.** Our own SECC never answers FAILED, so no recorded session
contains one and no replay can produce one. It took a station that says FAILED — for a reason of its own,
in virtual mode with no hardware — to make it visible.

Fixed the same day, in **both protocols and all three languages**. `Evcc20Base.RefuseOnFailure` sits in
the one place every -20 response passes through; `OK*` and `WARNING*` continue — a warning is explicitly
the code for "something is off and the session goes on" — and `FAILED*` ends the session with the message
and the code in the error. -2 needed a different shape: it has no common response base, so the code is
read by property name, and `Evcc2FailureHandlingTests.EveryResponseTypeIsCheckable` enumerates the
generated assembly to prove every response type carries one. A hand-written switch would have been
**fail-open** — the one forgotten, or the one added later, goes unchecked, which is the failure being
fixed.

**It has paid three times since**, all against EVerest: the AC energy-transfer-mode refusal, the MCS_BPT
`FAILED_WrongChargeParameter`, and the AC_BPT `FAILED_ContactorError` each arrived as a named code in the
message it happened in, rather than as a session that ran on and failed somewhere less obvious.

And it is the first of the three findings that made the roadmap ask for a **sweep** rather than a fourth
counterparty — *a value taken from our own assumption where the protocol supplies one*
([`assumed-values-sweep.md`](assumed-values-sweep.md)).

---

## What it found in **them**

Three, all reported in the run notes with the code that causes them:

1. **An optional element dereferenced.** `process_service_discovery_request.py` reads
   `payload.supported_service_ids.service_id` unconditionally; in -20 that element is `minOccurs="0"`, and
   omitting it means *"no filter, list everything"*. Our EVCC omits it — legally — and their session dies
   with `AttributeError: 'NoneType' object has no attribute 'service_id'`.
2. **The charge loop assumes Dynamic control mode.** `process_dc_charge_loop_request.py` reads
   `payload.dynamic_dc_clreq_control_mode` without checking. Ours sends the **Scheduled** variant — which
   their own `ScheduleExchange` had just answered `OK` — so the inconsistency is internal to their side.
3. **An EVSE offering PnC *and* EIM breaks their EV.** Their
   `wait_for_authorization_setup_response.py` walks the offered list and raises `NotImplementedError` on
   the first entry it does not support, even though EIM — which it does support — is the very next entry.
   An EVSE offering PnC alongside EIM is the ordinary case in the field.

Findings 1 and 3 were worked around **in their copy, inside a throwaway container**, to get past them.
Our stack was not changed for either. One note on method that is worth keeping: the finding-1 workaround
had to fall back to `[2]` (plain DC) rather than `[6, 2]` — choosing 6 made their charge-parameter handler
read BPT-only fields out of a plain-DC request and fail. **A workaround that manufactures the next error
is worse than none.**

---

## The wall, and the experiment that isolated it

Their EV terminates the session after `AuthorizationSetupRes`, and **the root cause inside their state
machine has not been identified.** That is recorded as an open question rather than guessed at.

What *was* settled, and how, is the part worth copying. The finding-3 workaround removed the crash and
their EV then terminated cleanly instead of selecting EIM — leaving it impossible to tell from the log
whether that termination was theirs or an artefact of the patch. So our SECC gained `OfferPlugAndCharge`
(CLI `--no-pnc`, fixture `V2G_INTEROP_NO_PNC=1`) to settle it **without patching anything of theirs
further**, mirroring `PreferDynamicControlMode`, which exists for exactly this class of reason.

Re-run with an EIM-only offer, their EV received exactly the one service it is configured for — and
**still sent `SessionStopReq`**. Two things follow: the workaround is exonerated, and the wall is not an
offered-services problem. Further diagnosis means reading their state machine rather than running interop.

> A verdict worth generalising, from that same run: the fixture's assertion **passed**. Their EV sent a
> well-formed `SessionStop(Terminate)`, our SECC reached its terminal state, `IsDone` was true, and the
> recorder even built a `SessionTrace`. Nothing was charged. *Four exchanges against a sixteen-message
> reference* is what `flow.md` showed at a glance — which is why the flow report, not `IsDone`, is the
> verdict that carries the information.

---

## What stays out of reach

All four reasons this counterparty was picked, and one that came with the setup:

- **Mutual TLS 1.3** — the reason it was chosen. Their `SECURITY_PROTOCOL` was set to `0x10` (TLS off,
  their own testing switch) to get a first session at all; the TLS run has not happened.
- **Dynamic control mode** — blocked behind the authorization wall, where `ScheduleExchange` (which is
  where the control mode is chosen) is never reached. Their charge-loop assumption (finding 2) is the
  *next* wall after that one, not this one.
- **DC bidirectional** — behind both.
- **The complete forward session** — behind finding 2.
- **SDP in the forward direction** — the relay path that makes this runnable from a Mac is exactly what
  bypasses discovery. The reverse direction did exercise it, which is the compensation.

Their container also needs an IPv6 network to start at all (`netifaces.ifaddresses(iface)[AF_INET6][0]`
raises `KeyError` on the default bridge), and their conda environment is linux-64 pinned, so the harness
ships a `Dockerfile` that reproduces what matters rather than what they specify. Both are setup facts, not
interop verdicts, and the run notes list all seven deviations from a clean-room run so nobody later
mistakes one for a result.

---

## Current state

**Two runs, one direction each, both stopped by their side** — and the column is worth more than that
sounds. It is one of two independent codecs here, the only one that has read our -20 at session level, and
the single richest source of defects-per-message this project has: one of ours, three of theirs, in
seventeen exchanges total.

Going back is cheap and the order is fixed by their own walls: the authorization termination gates
everything, and until somebody reads their EV's state machine, the four capabilities this counterparty was
chosen for stay unreachable.
