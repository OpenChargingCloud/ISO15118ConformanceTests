# 2026-08-11 — `EvseV2G` answers an out-of-order request properly. **No finding.**

A probe that went looking for a defect and did not find one, written up because a ruled-out class is
worth as much as a found one and costs the next reader nothing.

**The question.** ISO 15118-2 says a station that receives a request it is not waiting for must *answer*
before it hangs up: **`[V2G2-459]`** — the response shall carry `FAILED_SequenceError`; **`[V2G2-538]`** —
the SECC shall respond with **the corresponding response message** within
`V2G_SECC_Msg_Performance_Time`; **`[V2G2-539]`** — only then does it terminate, per `[V2G2-034]`.
Closing the socket without answering is the failure mode, and it is one **we had ourselves** until
2026-08-06 (`InteropSession.SeccOutcome.SequenceErrorAt` exists because of it). So it was worth asking
whether the station most likely to be on a real charger does it right.

**It does.**

| | |
|---|---|
| Counterparty | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12b8`), `EvseV2G`, `config-dc2-ours.yaml`, plain TCP |
| Ours | 40 lines of Python replaying our own recorded `-2` DC frames, with their live SessionID spliced in |
| Outcome | **Both arms answered with the right response message, their log named the error, and the connection closed after** — `[V2G2-459]`/`[V2G2-538]`/`[V2G2-539]` satisfied |
| Artifacts | [`probe.authorization.log`](probe.authorization.log) · [`probe.chargeparams.log`](probe.chargeparams.log) · [`their-charger.authorization.log`](their-charger.authorization.log) · [`their-charger.chargeparams.log`](their-charger.chargeparams.log) · [`seqprobe.py`](seqprobe.py) |

## The two arms

`SupportedAppProtocolReq` → `SessionSetupReq` → and then a message the station is **not** waiting for
(it wants `ServiceDiscoveryReq`), carrying the SessionID it had just issued:

| Arm | sent instead | their answer | their log |
|---|---|---|---|
| **A** | `AuthorizationReq` — four messages early | `01fe8001 0000000f 8098 02 <sid> 9010a100` | `Failed response code detected for message "Authorization", error: Sequence Error` |
| **B** | `ChargeParameterDiscoveryReq` — five early | `01fe8001 0000002a 8098 02 <sid> 90a0a030…` | `… for message "Charge Parameter Discovery", error: Sequence Error` |

Both times the response is the **corresponding** message type, which is the part `[V2G2-538]` is precise
about — not a generic error, not silence — and the connection closed afterwards, which is `[V2G2-539]`.

Their own log naming the response code is what makes this a measurement rather than an inference: we did
not have to decode the EXI to know what the code was, though the response length differing between the
arms (15 vs 42 bytes) already says the two answers are shaped like their own message types.

## Why it was worth the hour anyway

- **It rules out a class**, and the class is not hypothetical: our own station failed it four days ago,
  and the shape — *close the socket instead of answering* — is what a peer sees as "the charger
  vanished" rather than "the charger refused me".
- **It says something about the other direction.** Their EV is not what this tested; their station is.
  A future run that wants to know whether *their EVCC* handles `FAILED_SequenceError` gracefully has a
  different question and no answer here.
- **The probe is reusable.** [`seqprobe.py`](seqprobe.py) takes a host, a port and an arm name, and the
  frames come from `Vectors/Session.iso2-dc-eim.trace.json`. Pointing it at another `-2` station is a
  one-line change, and tux-evse's responder is the obvious next target.

## Not tested here

- The **timeout** half of the same clause pair: `[V2G2-537]` (stop at `V2G_SECC_Sequence_Timeout`) and
  the performance time in `[V2G2-538]`. We measured *that* an answer comes, not how fast.
- `FAILED_UnknownSession` (`[V2G2-460]`) — a wrong SessionID rather than a wrong message. Same probe,
  one more arm, not run.
- The `-2` document caveat applies to all three identifiers: the text to hand is the 2022 DIS revision
  and most deployed stacks target ISO 15118-2:2014. See [`normative-basis.md`](../../normative-basis.md).

## How it was run

```bash
~/everest/dist/bin/manager --config ~/everest/configs-ours/config-dc2-ours.yaml &   # fresh per arm
# EvseV2G binds and logs its port at startup — no SDP probe needed, unlike Evse15118D20
CP_AT_PLUGIN=1 bash ~/everest/sil-car.sh &
python3 seqprobe.py fe80::215:5dff:fe6b:3d4%2 61341 authorization
python3 seqprobe.py fe80::215:5dff:fe6b:3d4%2 61341 chargeparams
```
