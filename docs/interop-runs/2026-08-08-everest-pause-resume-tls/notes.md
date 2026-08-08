# 2026-08-08 — EVerest resumes a paused -20 session, and our EVCC cannot follow

**Their side works. Ours does not.** `Evse15118D20` answered our resumed `SessionSetupReq` with
`OK_OldSessionJoined` — *"Old session resumed with session_id: 0xC6, 0xCC, …"* — and our EVCC then sent
`AuthorizationSetupReq` and was told `FAILED_SequenceError`. A resumed `-20` session does not
re-authorize, and our EVCC does not know that.

| | |
|---|---|
| Counterparty | [EVerest](https://github.com/EVerest/everest-core) `everest-core` @ **`b61bb12`**, native build |
| Their config | `config-d20-tls-ours.yaml` — `ENFORCE_TLS`, `enforce_tls_1_3: true` (from [2026-08-06](../2026-08-06-everest-iso20-tls13-windows/notes.md)) |
| Ours | conformance suite @ `65f3c66`, CLI EVCC in WSL, mutual TLS with their minted vehicle credential |
| Direction | `EV→` — our EVCC against their station, -20 DC, EIM |
| Outcome | session 1: **60 exchanges**, paused, `OK_NewSessionEstablished`. Session 2: **`OK_OldSessionJoined`**, then our `AuthorizationSetupReq` → `FAILED_SequenceError` |

This closes the `▢` in the matrix, and not the way it was expected to close.

## Why it needed mutual TLS

Established by reading their source before the run, which is what made the run worth doing:
`d20/state/session_setup.cpp` rejoins only when `SHA-512(session_id ‖ vehicle_cert_hash)` matches, and
the hash comes from the verified TLS peer certificate — `ConnectionSSL` fills it after a handshake that
verified one, `ConnectionPlain::get_vehicle_cert_hash()` returns `std::nullopt` unconditionally. **A
plain-TCP pause/resume attempt would have answered `OK_NewSessionEstablished` and proved nothing**,
which is why no earlier EIM run could have found any of this.

The credential is theirs and had to be minted, exactly as the 2026-08-06 run did it: their vendored
Josev `create_certs.sh -v iso-20`, installed wholesale, their PKI backed up first and restored after.
Our EVCC presented `DC=OEM, C=DE, O=Pionix, CN=WMIV1234567890ABCDEX (+2 intermediates)`; their side
logged `Handshake complete!` and `Verify certificate result is okay` for both halves.

## What happened

```
session 1   Handshake complete! / Verify certificate result is okay
            Received session setup with evccid: EVCC01
            New session created with session_id: 0xC6, 0xCC, 0x08, 0xCD, 0x07, 0xFC, 0x88, 0x3B
            … 60 exchanges … Paused session id: C6CC08CD07FC883B   ✓ complete in 6186 ms

session 2   Handshake complete! / Verify certificate result is okay
            Received session setup with evccid: EVCC01
            Old session resumed with session_id: 0xC6, 0xCC, 0x08, 0xCD, 0x07, 0xFC, 0x88, 0x3B   ← accepted
            CAR ISO V2G AuthorizationSetupReq
            EVSE ISO V2G AuthorizationSetupRes
✗ our EVCC: the station answered AuthorizationSetupRes with FAILED_SequenceError
```

Their own state machine says why, in the branch right after the one that accepted the resume:

```cpp
if (not new_session) {
    …
    if (m_ctx.session.is_dc_charger()) {
        return m_ctx.create_state<DC_ChargeParameterDiscovery>();
    }
}
return m_ctx.create_state<AuthorizationSetup>();
```

A resumed session goes **straight to `DC_ChargeParameterDiscovery`**. `AuthorizationSetup` is only
reached on a new session. Our EVCC replays its full opening sequence regardless of the response code it
just received, so it sends `AuthorizationSetupReq` into a station that has already moved past it.

## What this is, and what it is not

**It is ours.** Our EVCC treats `OK_OldSessionJoined` as a label on an otherwise normal session. The
whole point of joining an old session is that the parts already agreed are not renegotiated, so a
resume that re-authorizes has not resumed anything.

**It is not proof about the standard.** Whether ISO 15118-20 *requires* skipping authorization on a
resumed session, or EVerest chose it, cannot be decided from here — the `-20` text is not in this
repository. What the run does establish is behavioural and sufficient for our purposes: **an EV that
re-sends `AuthorizationSetupReq` after `OK_OldSessionJoined` cannot resume against EVerest.** Ours does,
so ours cannot.

It sits beside the other half of the same question, found the same day: our *SECC* accepts a resume on
the session ID alone, with no vehicle-certificate binding, where EVerest binds one
([`docs/open-work.md`](../../open-work.md)). Both halves of our -20 resume were built by analogy to -2,
and -20 differs in at least two ways.

## Two rig facts, because they cost an hour

- **`Evse15118D20` answers SDP only when no session is running** — *"Ignoring sdp request message
  because a session is already created and running"*. A probe that is **not** followed by a TCP connect
  leaves a session in that state with nothing to time it out, and every later probe is refused. The
  first attempt at this run wedged itself exactly that way, on a probe fired to check the port.
- **Both halves must share one station process.** `pause_ctx` lives in it. Restarting the manager
  between the halves clears the paused context, and the resume then fails for a reason that has nothing
  to do with what is being tested.

The replug ritual (`unplug` → `execute_charging_session`) is per half, and comes *before* the probe.

## Reproducing

```bash
bash mint-and-install-pki.sh        # their create_certs.sh -v iso-20, installed wholesale
bash run-pause-resume-tls.sh        # restarts the station, then both halves against it
```

Artifacts: `our-evcc.s1.log`, `our-evcc.s2.log`, `their-charger.log`.

## Next

- **Fix our EVCC**: on `OK_OldSessionJoined`, skip `AuthorizationSetup` and open at
  `{AC,DC}_ChargeParameterDiscovery`. That is app-side work in `Evcc20Base`, and it wants a loopback
  regression test — our own SECC currently accepts the wrong sequence, so the loopback E2E cannot catch
  it as it stands.
- **Then re-run this**, which becomes the first live `-20` pause/resume that completes end to end.
- **The SECC half** — the vehicle-certificate binding — stays an open question in `docs/open-work.md`
  until somebody reads the standard.
