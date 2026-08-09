# Draft report to EVerest — `IsoMux` routes ISO 15118-**20** onto a TLS **1.2** connection

Status: **draft, not sent.** Observed on the wire 2026-08-06 against everest-core **2026.02.1** built
from source — both of the two sessions that offered `-20` got it, on TLS 1.2 — and the source re-read
at the same commit on 2026-08-09. Post it under your own name; see *Before sending* at the bottom.

Evidence in this repository:
[`2026-08-06-everest-isomux-tls`](../interop-runs/2026-08-06-everest-isomux-tls/notes.md) — the run
notes, both frame logs (`both-20first.frames.log`, `both-2first.frames.log`), the flow reports, the
refused TLS 1.3 transcript (`refused-tls13.log`), your own `their-charger.log`, and the config. The
plain-TCP predecessor, which found the routing rule itself, is
[`2026-08-03-everest-isomux-both`](../interop-runs/2026-08-03-everest-isomux-both/notes.md).

Four other reports for the same project are in
[`everest-isomux-sap-priority.md`](everest-isomux-sap-priority.md) — **same module, same function,
different fix** —
[`everest-loop-shutdown.md`](everest-loop-shutdown.md),
[`everest-iso20-ac-contactor-latch.md`](everest-iso20-ac-contactor-latch.md) and
[`pyevjosev-manifest-services.md`](pyevjosev-manifest-services.md), and a fifth goes to your fork of
Josev's certificate script ([`josev-iso20-pki-curve.md`](josev-iso20-pki-curve.md)). **File them
separately.** The framing in the first of those — what everest-core has been worth to this project,
and why a report from us is not a bug filed by a stranger — applies here unchanged and is not repeated.

---

**Title:** `IsoMux`: the ISO 15118-20 backend is selected on connections the module has capped at
TLS 1.2, which `[V2G20-2356]` forbids — and no conformant `-20` EVCC can reach that backend at all

**Version:** everest-core **2026.02.1** (`b61bb12b8`), native build, Debian 13, OpenSSL 3.5.6.
Modules `IsoMux` + `EvseV2G` + `Evse15118D20`, your `config-sil-dc-isomux-tls.yaml` with three
topology lines moved. The selection code is unchanged at `main`.

## Summary

`IsoMux` serves TLS **1.2 and nothing higher**, by construction rather than by accident, and then hands
the connection to whichever backend the `SupportedAppProtocolReq` names — including `Evse15118D20`.
Four arms, one EVCC, one station process:

| Arm | Offer | TLS | Result |
|---|---|---|---|
| `iso2-tls12` | `-2` only | 1.2 | ✅ 43 exchanges to `SessionStop` — correct |
| `refused-tls13` | `-20` only, 1.3 pinned | — | ⛔ **alert 70**, `tlsv1 alert protocol version` |
| `both-20first` | `-20` p1, `-2` p2 | 1.2 | ✅ station selected **`-20`**, 60 exchanges, every code `OK` |
| `both-2first` | `-2` p1, `-20` p2 | 1.2 | ✅ station selected **`-20`** anyway, 57 exchanges, every code `OK` |

Your own log, from the last of those, four lines apart:

```
14:47:13 iso_mux:IsoMux   :: Incoming TLS connection
14:47:13 iso_mux:IsoMux   :: handshake_req: Namespace: urn:iso:15118:2:2013:MsgDef,  … SchemaID: 1, Priority: 1
14:47:13 iso_mux:IsoMux   :: handshake_req: Namespace: urn:iso:std:iso:15118:-20:DC, … SchemaID: 2, Priority: 2
14:47:13 iso_mux:IsoMux   :: Connected to proxy module for ISO-20
14:47:13 iso_mux:IsoMux   :: Multiplexer: Proxy TLS->TCP
14:47:13 iso15118_20:Evs  :: Incoming connection from [::1]:33796
```

The answered `SupportedAppProtocolRes` was `01fe80010000000480400080` — SchemaID 2, the `-20` entry —
and a complete DC session followed: `AuthorizationSetup`, `ServiceDiscovery`, `ScheduleExchange`,
`DC_CableCheck`, `DC_PreCharge`, three `DC_ChargeLoop`s, `DC_WeldingDetection`, `SessionStop`.

That the connection could not have been better than TLS 1.2 is not inferred from our client:

```
$ openssl s_client -connect [fe80::…%eth0]:64110 -tls1_2
  Protocol: TLSv1.2, Cipher: ECDHE-ECDSA-AES128-SHA256, subject=CN=SECCCert, O=EVerest, C=DE, DC=CPO
$ openssl s_client -connect [fe80::…%eth0]:64110 -tls1_3
  ssl3_read_bytes:tlsv1 alert protocol version:SSL alert number 70
```

## What the standard says

Three separate places, all in ISO 15118-20 itself — no `-2` revision caveat applies:

- **`[V2G20-2356]`** — a *shall not*, addressed to the station: it may not choose `-20` out of the offer
  when the connection carrying that offer is plain TCP, or TLS at 1.2 or below. This is the requirement
  the run above puts the station on the wrong side of.
- **`[V2G20-1237]`** — the mirror, addressed to the car: over the same set of connections it may not put
  `-20` into the offer at all. Ours did; see *Our EV was wrong too*, below.
- **`[V2G20-1805]`** — both halves in one clause, reached from the SDP side: where the TLS connection
  `7.7.3` calls for was not established, `-20` is neither offered nor chosen.

Both `[V2G20-2356]` and `[V2G20-1805]` point at **Table 5**, which pairs TLS versions with the
protocols they may carry. `-20` appears in the 1.3 row only.

Two more that bound the shape of an acceptable fix:

- **`[V2G20-2359]`** — supporting TLS 1.2 is explicitly *permitted*, for backward compatibility. Serving
  1.2 is not the defect. Selecting `-20` on it is.
- **`[V2G20-1235]`** — where the chosen `ProtocolNamespace` is `urn:iso:std:iso:15118:-20`, the `-20`
  TLS profile applies to both sides. The profile itself is Tables 6 to 8: `TLS_AES_256_GCM_SHA384` and
  `TLS_CHACHA20_POLY1305_SHA256` (`[V2G20-2458]`, `[V2G20-2459]`), `secp521r1` and `x448`
  (`[V2G20-1634]`, `[V2G20-1637]`). `ECDHE-ECDSA-AES128-SHA256` is not a TLS 1.3 suite at all.

We hold the ISO documents under licence and quote none of them; the identifiers above are what the
industry refers to these obligations by, and each sentence here states what the requirement obliges
rather than how it is worded. Our own rule for this is
[`docs/normative-basis.md`](../normative-basis.md).

## The consequence that makes it worth your time

Not the letter of the requirement — this:

**Through `IsoMux`, the `-20` backend is unreachable by any conformant EVCC, and reachable only by one
that is not.**

A `-20` EVCC must offer TLS 1.3 in `supported_versions` (`[V2G20-2365]`, and `[V2G20-1264]` requires
mutual TLS 1.3 of every `-20` entity). There are two kinds:

- **`-20`-only.** Offers 1.3 alone; your mux answers alert 70. That is the `refused-tls13` arm above.
- **Backward-compatible.** Adds `0x0303` as `[V2G20-2062]` requires, lands on TLS 1.2 — and then
  `[V2G20-1237]` obliges it to drop `-20` from the offer, so it gets `EvseV2G` and ISO 15118-2.

Either way it never reaches `Evse15118D20`. The only path that does is an EV that offers `-20` on a
TLS 1.2 connection, which is exactly the thing `[V2G20-1237]` forbids — and reaching it then requires
the station to do the thing `[V2G20-2356]` forbids. A route that works only when both peers break the
same pair of requirements is a route that will not work in the field.

There is a second consequence in that same log excerpt, which we did **not** test and state only
because it follows from your own code. `Multiplexer: Proxy TLS->TCP` means the mux terminates TLS and
forwards plaintext to the backend over loopback (`tls_connection.cpp:328-334` — the buffered
`SupportedAppProtocolReq` written out with `write()`, then a byte pump between the two descriptors). So
behind the mux, `Evse15118D20` has no TLS session of its own: its `enforce_tls_1_3` option cannot help,
and anything `-20` derives from the handshake — the vehicle certificate that `-20`'s mutual
authentication puts on the wire (`[V2G20-1264]`, `[V2G20-2339]`), and with it the session-resumption
binding of `8.3.4.1.4.3` — is not available to it. Worth a look while you are in the module; we have not
measured it and make no claim about it.

## Where it comes from in your source

Two independent decisions, in two files.

**1. The TLS profile is `EvseV2G`'s, copied into a module that also fronts `-20`.**

```cpp
// modules/EVSE/IsoMux/connection/tls_connection.cpp:278-280, in build_config()
config.cipher_list = "ECDHE-ECDSA-AES128-SHA256";
config.ciphersuites = "";     // disable TLS 1.3
config.verify_client = false; // contract certificate managed in-band in 15118-2
```

`lib/everest/tls/src/tls.cpp:442-449` turns the empty `ciphersuites` into the version cap, and says so:

```cpp
if ((ciphersuites != nullptr) && (ciphersuites[0] == '\0')) {
    // no cipher suites configured - don't use TLS 1.3
    // nullptr means use the defaults
    if (SSL_CTX_set_max_proto_version(ctx, TLS1_2_VERSION) == 0) {
```

Those three lines are also in `modules/EVSE/EvseV2G/connection/tls_connection.cpp:282-284`, where they
are **right**: `EvseV2G` speaks ISO 15118-2, `ECDHE-ECDSA-AES128-SHA256` is the suite `-2` prescribes,
and in-band contract certificates are `-2`'s design. The third comment names the profile out loud. It
is the copy into `IsoMux` — the one module in the tree that fronts both protocols — that inherits a
`-2`-shaped decision for a `-20`-capable listener. (The two files are *not* otherwise identical: 14 005
and 11 618 bytes. This is a copied decision, not a shared file.)

There is no configuration that lifts it. `IsoMux`'s manifest offers `device`, `tls_security`
(`prohibit`/`allow`/`force`), `tls_key_logging`, `tls_timeout`, `proxy_port_iso2`, `proxy_port_iso20`
and `proxy_device` — no TLS version or cipher option of any kind, and `build_config()` sets those two
fields unconditionally.

**2. The routing decision never looks at the connection.**

`v2g_sniff_apphandshake()` decodes the offer and returns on the first entry whose namespace begins
`urn:iso:std:iso:15118:-20` (`modules/EVSE/IsoMux/v2g_server.cpp:118-142`). Its caller then routes:

```cpp
// modules/EVSE/IsoMux/connection/connection.cpp:429-443, connection_handle()
if (conn->ctx->state == 0) {
    iso20 = v2g_detect_iso20_support(conn);
}
…
uint16_t port = conn->ctx->proxy_port_iso2;
conn->ctx->selected_iso20 = false;
if (iso20) {
    conn->ctx->selected_iso20 = true;
    port = conn->ctx->proxy_port_iso20;
}
```

`conn->is_tls_connection` (`v2g.hpp:165`) is in scope at that `if` and is not consulted. Nothing else
between the handshake and the proxy connect looks at the transport either.

## Your `-20` module already agrees with the requirement

`libiso15118` implements the strict side and `Evse15118D20` exposes it:

```cpp
// lib/everest/iso15118/src/iso15118/io/connection_ssl.cpp:233-235
const int result_set_min_proto_version = (ssl_config.enforce_tls_1_3)
                                             ? SSL_CTX_set_min_proto_version(ctx, TLS1_3_VERSION)
                                             : SSL_CTX_set_min_proto_version(ctx, TLS1_2_VERSION);
```

with `enforce_tls_1_3` in `modules/EVSE/Evse15118D20/manifest.yaml:22`. Addressed directly, that module
refuses a hello that still permits 1.2 — it cost us a run in August, correctly. Put the mux in front of
it and the option is not merely off, it is unreachable: the module is no longer the TLS endpoint.

So this is not a disagreement about what `-20` requires. Your tree contains the right behaviour; the
multiplexer is where it stops being reachable.

## Our EV was wrong too, in the mirror image

Stated plainly because the run above needed it. The offer that produced the two `-20`-over-1.2 sessions
came from our EVCC, and it violated `[V2G20-1237]`: it advertised both TLS versions in the ClientHello
(right — that is what `[V2G20-2062]` and `[V2G20-2365]` ask of a backward-compatible EVCC), let the
station settle on 1.2 (right, `[V2G20-2064]`), and then offered `-20` in the `SupportedAppProtocolReq`
anyway (wrong). It is ours to fix and it is recorded as such in our
[`open-work.md`](../open-work.md).

We do not think it weakens the report, and we would rather you judge that than discover it. Two reasons:

- `[V2G20-2356]` binds the SECC on its own terms. It is written as a check on what the *station* does
  with the offer it received, which is what makes it useful — an EV that gets this wrong is precisely
  the case it exists for.
- The reachability argument above does not depend on it at all. It is about what a *correct* EVCC
  experiences, and both kinds of correct EVCC lose.

What our fault does mean is that we cannot show you a conformant EV completing a `-20` session over
TLS 1.2 — no such thing exists, by construction. The `refused-tls13` arm is the half we can show:
a `-20` EVCC that pins 1.3, as required, never gets a connection.

## Suggested direction

Two changes; the first is small and, alone, removes a path that currently works.

1. **Gate the route on the transport.** In `connection_handle()`, at the `if (iso20)` on
   `connection.cpp:439`, decline `-20` unless the connection is TLS 1.3 — today, with the cap in place,
   that means declining it on every TLS connection, so a `-20`-mentioning offer would fall through to
   `EvseV2G` and the EV would get ISO 15118-2. That is the conformant outcome and it is also a
   capability regression until (2) lands, which is why we would not send this on its own.

   The same gate covers plain TCP, which `[V2G20-2356]` treats identically — and that is how everyone
   including us runs `-20` on the bench, so a guard with no way round it would delete a lot of working
   test setups. An explicit `tls_security: prohibit` is arguably already the way to say *"this
   deployment does not do TLS, proceed anyway"*, which would leave `allow` as the only case that has to
   refuse. But that is a judgement about your users and it is yours, not ours.

2. **Serve TLS 1.3 as well, so the `-20` route becomes real.** `tls::Server::config_t` has both
   `cipher_list` (the 1.2 suites) and `ciphersuites` (the 1.3 ones), and `server_init()` applies each to
   its own version — so one listener can carry both profiles. Setting `ciphersuites` to the two suites
   `-20` prescribes lifts the max-version cap by the very condition quoted above, and the mux would then
   serve 1.3 to `-20` EVs and 1.2 to `-2` ones, which is what a multiplexer in front of both stacks
   ought to do.

   The part we would not want to specify for you is client authentication. `verify_client = false` is
   right for `-2` and `[V2G20-2067]` forbids requesting the EVCC certificate on the legacy path, while
   `-20` wants mutual authentication — and OpenSSL's verify mode is set on the `SSL_CTX`, not per
   negotiated version. A `SSL_CTX_set_client_hello_cb` that sets `SSL_set_verify()` once the version is
   known is one way; you will know better ones.

3. **Or decide the other way, and say so.** If `IsoMux` is meant only as a `-2`-era front door and the
   `-20` route through it is not intended to be used over TLS, then (1) alone is the whole fix and the
   manifest should say that TLS on the mux implies ISO 15118-2. That is a legitimate answer and it is
   still a change — the current behaviour neither serves `-20` properly nor refuses it.

## Also in the same function — filed separately, and please read it separately

`v2g_sniff_apphandshake()` never reads `Priority`. It walks the offer in the order received and returns
on the first `-20` entry, so an EV that ranks `-2` above `-20` gets `-20` anyway — visible in the
`both-2first` log excerpt at the top, where both entries are logged and the `-2` entry is the
priority-1 one. `[V2G2-169]` and `[V2G20-169]` make selecting by the EV's ranking a *shall*, and both
modules behind your mux already implement it. Reproduced three times, across 2025.10.0 and 2026.02.1,
over plain TCP and over TLS: [`everest-isomux-sap-priority.md`](everest-isomux-sap-priority.md).

Different defect, different fix, and a different severity — that one costs no interop at all. The
routing decision they land in is the same one, so you will meet both in the same `if`; that is the only
reason it is mentioned here.

---

## Before sending

- [x] **Reproduce it yourself, on their stack.** Their modules, their `config-sil-dc-isomux-tls.yaml`
      shape, their PKI, their SIL car. Two sessions of two selected `-20` on TLS 1.2, and the control
      (`-2` only) behaved correctly in the same process.
- [x] **Re-check every line reference against the tree.** All of them re-read from the built 2026.02.1
      source on 2026-08-09, three days after the run: `IsoMux/connection/tls_connection.cpp:278-280`,
      `IsoMux/v2g_server.cpp:118-142`, `IsoMux/connection/connection.cpp:429-443`,
      `IsoMux/connection/tls_connection.cpp:328-334`, `IsoMux/v2g.hpp:165`, `IsoMux/manifest.yaml`,
      `EvseV2G/connection/tls_connection.cpp:282-284`,
      `lib/everest/tls/src/tls.cpp:442-449`, `lib/everest/iso15118/src/iso15118/io/connection_ssl.cpp:233-235`,
      `Evse15118D20/manifest.yaml:22`.
- [ ] **Lead with reachability, not with the clause.** The requirement makes it decidable; the reason to
      spend time on it is that the `-20` half of the multiplexer cannot be used by a conformant car.
      A report that opens with a `shall not` invites a debate about test benches.
- [ ] **Say our EV was wrong first, not when asked.** The section above is in the draft on purpose;
      keep it, and keep it before the suggested fixes.
- [ ] **Ask whether the `-20` route through the mux is meant to be used at all.** If the answer is no,
      suggestion 3 is the whole issue and the rest is noise. This is the question that decides the
      shape of the fix and it is theirs to answer.
- [ ] **Do not attach the plaintext-backend paragraph as a finding.** It is inferred from their log line
      and their proxy code, and nothing here measured it. Either drop it or mark it as a question.
- [ ] **File one issue, this one.** The `Priority` behaviour is separate; the loop-shutdown, contactor
      and manifest reports are separate again.
- [ ] **Post under your own name, in your own words.**
