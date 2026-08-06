# 2026-08-06 — the reverse direction, **recorded**: the fixture advertises itself

**A session their EV discovered by SDP and our fixture wrote down.** Same counterparty and same result as
[`2026-08-06-everest-mcs-reverse`](../2026-08-06-everest-mcs-reverse/notes.md) — their `PyEvJosev` picking
service **8** out of our `Secc20Mcs` catalogue — but driven by `EverestInteropTests` instead of the CLI, so
it leaves frames, a flow report and a corpus trace behind instead of a console log.

```
SDP: advertising [fe80::215:5dff:fe79:69ca%2]:55000 (NoTLS) on eth0 — their EV should discover this
     station rather than be pointed at it.
Waiting for their PyEvJosev on [::]:55000 ...
SDP: request from [fe80::215:5dff:fe79:69ca%2]:49221 — NoTLS, TCP
SDP: answered with [fe80::215:5dff:fe79:69ca%2]:55000 (NoTLS, TCP), 28 byte(s).

Energy transfer service: 8 (MCS) — their EV's pick out of our catalogue.
Plug & Charge (inbound): contract DC=MO, C=DE, O=EVerest, CN=UKSWI123456789A;
                         challenge OK, digest OK, signature OK (ecdsa-sha256, grammar=xmldsig-standalone).

Passed TheirPyEvJosev_AgainstOurSecc_RunsToCompletion [53 s]
```

| | |
|---|---|
| Counterparty | [EVerest](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12b8`), native build in WSL2 |
| Their EV | `PyEvJosev`, `supported_d20_energy_services: MCS`, plain TCP |
| Ours | `EverestInteropTests.TheirPyEvJosev_AgainstOurSecc_RunsToCompletion`, `Secc20Mcs` |
| Config | [`config-mcs-reverse-ours.yaml`](../2026-08-06-everest-mcs-reverse/config-mcs-reverse-ours.yaml), unchanged from the CLI run |
| Command | `V2G_INTEROP_LISTEN=55000 V2G_INTEROP_SDP=eth0 V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=mcs V2G_INTEROP_RECORD=… dotnet test --filter …TheirPyEvJosev…` |
| Sessions | **PnC** 53 exchanges · **EIM** 52 exchanges, both to `SessionStop` |

## What was missing, and what closes it

The reverse fixture bound a TCP socket and waited. Their EV takes a `device` and finds a station by SDP
multicast on it — there is no endpoint to point it at — so the fixture was waiting for a car with no way to
arrive, and every reverse run against a discovering peer had to go through the CLI, which could advertise
but could not record. The previous run's closing line named that as the next piece of harness work.

`Interop/InteropSdp.cs` is it: `V2G_INTEROP_SDP=<iface>` starts a `SECC_SDPServer` beside the listener, on
the port the listener **actually bound** rather than the one the environment asked for. It reuses the CLI's
`Program.BuildSeccSdpOptions` rather than repeating it — the subtlety worth not re-deriving is
`RejectNoTlsRequests`, whose TLS-oriented default (`true`) makes a plaintext station drop a plaintext EV's
request without a word, and the fixture and `secc --sdp` now advertise identically by construction.

## Finding 1 — the reverse fixture could not have failed an MCS run

The previous run's finding 2 was that the *app* could not report which service had been selected
(`Secc20Base.SelectedEnergyServiceId` was `protected`). Fixed then. What the first recorded session showed
is that the *fixture* still had nowhere to put it: `RunSeccAsync` returned a bare `Boolean`, so a reverse
run reported whether it finished and never what finished.

That is not only unreadable, it is unsound. Our `Secc20Mcs` offers `{ 8, 9 }` on a state machine that runs
an ordinary DC session just as happily, so an EV that ignored the MCS entries would complete, the test
would pass, and the run would be filed as the one where somebody else's car chose our MCS catalogue. The
forward fixture has asserted its negotiated service since the first MCS run; the reverse one asserted
nothing.

`RunSeccAsync` now returns a `SeccOutcome` — terminal state, selected service, and the inbound Plug & Charge
verdict — mirroring the `EvccOutcome` that already existed on the other side, and the Everest reverse test
asserts service 8-or-9 for an MCS run. Both lines in the transcript above come from that, and both are
facts only our station holds: the session is ours, so their charger module never sees it and their logs say
nothing about it.

## Finding 2 — the more valuable half of the run cannot become a corpus entry, for a good reason

Their EV authorizes with a contract certificate, and our SECC verifies it — the -20 Plug & Charge result
this counterparty has in one direction only. That session's recording is **refused** by `SessionTrace.Build`:

> trace 'everest-iso15118-20-mcs-reverse' has signed requests but no signing key — the signature-aware
> comparison would substitute the recorded value and verify nothing.

Right, and worth stating rather than working around: the signature is their EV's, made with a private key
that is theirs and not in this repository, so a corpus entry built from it could only ever re-check the
bytes against themselves. The 2199-byte `AuthorizationReq` is 52 % of everything their car sent in that
session, and none of it is reproducible by us.

So the run was done **twice**, and both halves are kept:

| | `mcs-reverse.pnc.*` | `mcs-reverse.eim.*` |
|---|---|---|
| Authorization | contract, verified inbound | EIM (`V2G_INTEROP_NO_PNC=1`) |
| Service | 8 (MCS) | 8 (MCS) |
| Exchanges | 53 | 52 |
| Artifacts | frames, flow, `trace-not-built.txt` | frames, flow, **`trace.json`** |

The EIM session carries no signature, so it builds — that is the corpus entry, and the first one this
project has from the reverse direction against EVerest. The PnC session keeps the evidence a trace could
not have held anyway.

## Not a finding — the poll rounds differ between the two

The EIM session shows six short `PowerDeliveryReq` before the one that carries a power profile; the PnC
session shows none. That is the case our own `Secc20Base` already documents at the `Phase20.PowerOn`
transition — *"a real EV repeats PowerDeliveryReq(Start) (EVProcessing=Ongoing) until it begins the charge
loop; answer each and stay"* — so their EV was waiting for the DC supply, our station answered each and
held the phase, and the count is how many rounds that took. The recording has no timestamps, so how much of
the difference is the 2.2 kB signature verification buying the supply extra time is not something these
artifacts can settle. Both sessions are otherwise identical message for message.

## Running it

`dotnet test` inside WSL, for the same reason the CLI ran there: SDP is multicast on the EV's link and our
Windows side is not on it. .NET 10 is present; `--artifacts-path ~/wsl-artifacts` keeps the Linux build out
of the `bin/`/`obj/` the Windows build owns, which matters when the same working tree is built from both.

Order matters — **fixture first, station second**. Their EV probes once, shortly after the manager boots;
if nothing answers that probe the session never starts and the fixture times out looking like the peer
never came.

```bash
mosquitto -p 1883 &                                        # /usr/sbin, not on PATH
dotnet test … --filter …TheirPyEvJosev… &                  # advertises, then waits
sleep 8 && ./bin/manager --config …/config-mcs-reverse-ours.yaml
```

## Artifacts

`mcs-reverse.pnc.{flow.md,frames.log,trace-not-built.txt}`, `mcs-reverse.eim.{flow.md,frames.log,trace.json}`,
`our-fixture.pnc.log` (the transcript above in full), `their-manager.pnc.log`. The raw octet streams are
written beside them by the recorder and not committed, as with every other run here.
