# 2026-08-06 — **IsoMux over TLS**: a complete ISO 15118-**20** session on TLS **1.2**

**Their multiplexer terminates TLS at the -2 profile and then routes to whichever backend the SAP offer
names — including the -20 one.** So the combination it exists for is reachable over TLS only by speaking a
profile ISO 15118-20 does not allow, and a -20 EV that pins its own profile correctly cannot reach it at
all.

```
TLS: Tls12, TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA256, server DC=CPO, C=DE, O=EVerest, CN=SECCCert
SAP: offered -20 (priority 1), -2 (priority 2); the station picked -20.
✓ 60 exchanges to SessionStop, every code OK
```

| | |
|---|---|
| Counterparty | [EVerest](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12b8`), native build in WSL2 |
| Their station | `IsoMux` on `eth0`, `tls_security: force`; `EvseV2G` + `Evse15118D20` behind it on `lo` |
| Config | [`config-mux-tls-ours.yaml`](config-mux-tls-ours.yaml) — their `config-sil-dc-isomux-tls.yaml`, three lines moved to our topology |
| Ours | `EverestInteropTests.OurEvcc_…`, `V2G_INTEROP_TLS=1`, permissive server validation |

| Arm | Offer | TLS | Result |
|---|---|---|---|
| `iso2-tls12` | -2 only | 1.2 | ✅ 43 exchanges, `SessionStop` |
| `refused-tls13` | -20 only | 1.3 pinned | ⛔ **alert 70**, `tlsv1 alert protocol version` |
| `both-20first` | -20, then -2 | 1.2 (negotiated) | ✅ station picked **-20**, 60 exchanges |
| `both-2first` | -2, then -20 | 1.2 (negotiated) | ✅ station picked **-20** anyway, 57 exchanges |

## Finding 1 — `IsoMux` serves TLS 1.2 only, whichever protocol it routes

Confirmed on the wire rather than inferred:

```
$ openssl s_client -connect [fe80::…%eth0]:64110 -tls1_2
  Protocol: TLSv1.2, Cipher: ECDHE-ECDSA-AES128-SHA256, subject=CN=SECCCert, O=EVerest, C=DE, DC=CPO
$ openssl s_client -connect [fe80::…%eth0]:64110 -tls1_3
  ssl3_read_bytes:tlsv1 alert protocol version:SSL alert number 70
```

Where the 1.2 comes from, read in their source afterwards: `IsoMux/connection/tls_connection.cpp:278`
pins `cipher_list` to `ECDHE-ECDSA-AES128-SHA256` — the suite ISO 15118-2 prescribes — and sets
`ciphersuites = ""` under the comment *"disable TLS 1.3"*; `lib/everest/tls/src/tls.cpp:442` caps the
version at 1.2 on exactly that condition, also commented. Both lines are carried verbatim from `EvseV2G`.
So the mux does not *fail* to offer 1.3 — it serves the **-2 TLS profile by construction**, which is what
makes the rest of this a layering question rather than a missing feature.

Two consequences, and the second is the interesting one:

- **A conformant -20 EV cannot reach their -20 backend through the mux.** ISO 15118-20 mandates TLS 1.3;
  the mux refuses that hello before SAP exists. Our `-20`-only arm fails exactly there.
- **A dual-stack EV can — and gets a -20 session over TLS 1.2.** The mux answered our both-offer with
  SchemaID 2 (the -20 entry) on a TLS 1.2 connection, and 60 exchanges later the session reached
  `SessionStop` with every response `OK`. Nothing anywhere in that path objects.

That is a layering gap, not a bug in any single message: TLS is settled before `SupportedAppProtocol`
runs, the mux settles it at its own profile, and the routing decision that happens afterwards can land on a
backend whose profile the connection no longer satisfies. Their `Evse15118D20` addressed directly is strict
about this — under `enforce_tls_1_3` it refuses a hello that still allows 1.2, which cost us a run in
August. Behind the mux, that strictness is unreachable in either direction.

> **Decided and filed 2026-08-09.** *"A layering gap, not a bug in any single message"* was as far as this
> could be taken without the requirement text, which arrived two days later. It is `[V2G20-2356]`, a
> *shall not* addressed to the SECC — with the connection at TLS 1.2 or below it must not select `-20`
> from the `SupportedAppProtocolReq` — restated as `[V2G20-1237]` for the EVCC and as `[V2G20-1805]` for
> both, all three pointing at Table 5, where `-20` sits in the 1.3 row only.
> [`reports/everest-isomux-iso20-over-tls12.md`](../../reports/everest-isomux-iso20-over-tls12.md) is the
> nineteenth filing, and it leads with the consequence rather than the clause: a `-20`-only EV gets alert
> 70 and a backward-compatible one must drop `-20` from its offer on the 1.2 connection, so **their `-20`
> backend is unreachable by any conformant EVCC** and reachable only by one that is not.
>
> Which our EVCC was. The offer that produced both `-20` sessions above kept its `-20` entry on a TLS 1.2
> connection, and `[V2G20-1237]` forbids exactly that — our half of the same finding, now in
> [`open-work.md`](../../open-work.md). The ClientHello was right; the step after it was not.
>
> Finding 3 below went the same way: `[V2G2-169]` makes selecting by the EV's ranking a *shall*, so that
> one is decided too. It is a separate fix in the same function and is deliberately **not** folded into
> the filing.

## Finding 2 — the accept-loop defect is **not** in `IsoMux`

Worth recording because it narrows the report that is waiting to be filed
([`docs/reports/everest-loop-shutdown.md`](../../reports/everest-loop-shutdown.md)). `Evse15118D20`'s
event loop dies on any error on its accept path — a refused TLS handshake among them — leaving the sockets
bound and the station silently answering nothing.

`IsoMux` took a refused TLS 1.3 handshake from our EVCC and a second one from `openssl`, and **kept
accepting**: the TLS 1.2 probe after both completed normally, and the `both-*` sessions after that ran to
completion in the same process. The defect is `Evse15118D20`'s, not shared by the module in front of it.

## Finding 3 — priority is still ignored, now over TLS

The 2026-08-03 finding reproduces unchanged: offered `-2` at priority 1 and `-20` at priority 2, their mux
answered SchemaID 2 — the -20 entry. It walks the offer for the first namespace starting with `-20` and
never reads `Priority`. Third confirmation, second release, and the first over TLS.

## What it changed on our side

`DevTlsOrNull` pinned the TLS profile from the protocol — -2 to 1.2, -20 to 1.3 — including for
`V2G_INTEROP_PROTOCOL=both`, where `ProtocolAndMode` resolves "both" to -20 because that is the offer's
priority-1 entry. That resolution is right for naming a recording and **wrong for pinning a ClientHello**:
at the moment the profile is chosen, the handshake that decides the protocol has not run, because it runs
*inside* TLS. Pinned to 1.3, the both-offer never reached SAP against this station at all, and the failure
looked like an opaque handshake error rather than a profile disagreement.

A both-protocol offer now offers both TLS versions and the union of the suite lists, exactly as it offers
both application protocols one layer up, and lets the station settle it. Four offline tests hold the
pinning for single-protocol runs and the widening for `both`.

The fixture also prints what the handshake **actually** settled on, rather than what was offered:

```
TLS: Tls12, TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA256, server DC=CPO, C=DE, O=EVerest, CN=SECCCert
```

Without that line this run's whole finding is invisible — a complete -20 session looks like a success, and
the version it rode on is nowhere in the transcript.

## Running it

`IsoMux` binds its TCP port **at startup** (`[fe80::…%eth0]:64110` here) and needs no SDP probe, unlike
`Evse15118D20` addressed directly. The rest is the usual ritual: manager, `sil-car.sh` with
`CP_AT_PLUGIN=1`, wait for `SLAC MATCHED`, replug between sessions. `dotnet test` runs inside WSL, so there
is no relay.

Config deltas from their `config-sil-dc-isomux-tls.yaml`, all topology:

```diff
   iso_mux:            device: auto → eth0       # our EVCC's link
   iso15118_car:       device: auto → lo         # we are the car
   ev_manager:         auto_exec: false          # their EV must not drive
```

`tls_security: force` on the mux is theirs and stays; note their own config leaves the `EvseV2G` **backend**
at `allow` — only the front door forces TLS.

## Artifacts

`iso2-tls12.*`, `both-20first.*`, `both-2first.*` (flow, frames, trace), `refused-tls13.log` (the alert-70
transcript), `their-charger.log`, and the config. All three completed sessions are EIM and unsigned, so all
three became corpus traces.
