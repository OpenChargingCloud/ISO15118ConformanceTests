# Draft report to EVerest — four findings in `IsoMux`

Status: **draft, not sent.** Four defects in one module, found between 2026-08-03 and 2026-08-10 across
everest-core **2025.10.0** and **2026.02.1**, all re-read in the built 2026.02.1 source (`b61bb12b8`)
and all still present. Post it under your own name; see *Before sending* at the bottom.

**This was four separate reports until 2026-08-10.** They were merged because they are one module, one
maintainer's afternoon, and — for three of the four — one shape: *a decision taken on information the
module does not have, or has and does not read.* Split them again if your tracker prefers it; each
section below stands alone, and each carries its own evidence and its own fix.

Three other reports go to everest-core and are **separate on purpose** — different modules, different
people would review them:
[`everest-loop-shutdown.md`](everest-loop-shutdown.md) (`Evse15118D20`),
[`everest-iso20-ac-contactor-latch.md`](everest-iso20-ac-contactor-latch.md) (libiso15118),
[`everest-evsev2g-session-log-responses.md`](everest-evsev2g-session-log-responses.md) and
[`everest-evse-security-ocsp-dropped.md`](everest-evse-security-ocsp-dropped.md) (`EvseV2G` /
libevse-security — the second of those also touches `IsoMux`, see §5). Plus
[`pyevjosev-manifest-services.md`](pyevjosev-manifest-services.md) and one to your fork of Josev's
certificate script ([`josev-iso20-pki-curve.md`](josev-iso20-pki-curve.md)).

The framing in `everest-loop-shutdown.md` — what everest-core has been worth to this project, and why a
report from us is not a bug filed by a stranger — applies here unchanged and is not repeated.

**Version for all four:** everest-core **2026.02.1** (`b61bb12b8`), native build, Debian 13,
OpenSSL 3.5.6. Module `IsoMux` in front of `EvseV2G` and `Evse15118D20`, `config-sil-dc-isomux`-shaped
configs. Findings 1 and 2 were also seen on **2025.10.0**.

---

## The four

| | Finding | What it costs | Requirement | Evidence |
|---|---|---|---|---|
| **1** | The backend is chosen on the *first* `-20` entry in the offer; `Priority` is logged and not read | A car that ranks `-2` above `-20` is put on `-20` anyway | `[V2G2-169]`, `[V2G20-169]` | three runs, two releases, two transports, byte-identical |
| **2** | The module serves TLS 1.2 and nothing higher, then routes `-20` onto it | The `-20` backend is reachable **only** by a non-conformant EV | `[V2G20-2356]`, `[V2G20-1237]`, `[V2G20-1805]` | four arms, one station process |
| **3** | A failed V2GTP header read is logged and not acted on | The backend choice is made from a buffer that was never filled | — (robustness) | A/B, two bytes apart |
| **4** | No trust anchor is fetched, so the TLS server boots with `trusted_ca_keys support disabled` | A station with two V2G roots cannot present the right chain | `[V2G2-651]`, `[V2G2-871]` | boot A/B, same process |

Findings 1, 2 and 3 all land in the same routing decision. If you touch `connection_handle()` and
`v2g_detect_iso20_support()` for one, the others are a few lines away.

---

## 1. `v2g_sniff_apphandshake()` routes on the first `-20` entry and never reads `Priority`

A `SupportedAppProtocolReq` ranking ISO 15118-2 **above** `-20` is a legal offer meaning *"I speak both
and would rather speak `-2`"*. The multiplexer supports both, and answers `-20`.

Your own log — both entries read, `Priority` printed two lines above the decision and not used in it:

```
iso_mux:IsoMux :: handshake_req: Namespace: urn:iso:15118:2:2013:MsgDef,  Version: 2.0, SchemaID: 1, Priority: 1
iso_mux:IsoMux :: handshake_req: Namespace: urn:iso:std:iso:15118:-20:DC, Version: 1.0, SchemaID: 2, Priority: 2
iso_mux:IsoMux :: Connected to proxy module for ISO-20
```

| Run | Release | Transport | EV's ranking | Answered |
|---|---|---|---|---|
| [2026-08-03](../interop-runs/2026-08-03-everest-isomux-both/notes.md) | 2025.10.0 | TCP | `-2` p1, `-20` p2 | SchemaID **2** (`-20`) |
| [2026-08-05](../interop-runs/2026-08-05-everest-2026021-matrix/notes.md) | 2026.02.1 | TCP | `-2` p1, `-20` p2 | SchemaID **2** (`-20`) |
| [2026-08-06](../interop-runs/2026-08-06-everest-isomux-tls/notes.md) | 2026.02.1 | TLS 1.2 | `-2` p1, `-20` p2 | SchemaID **2** (`-20`) |

The same 79-byte request went out all three times and the same 12-byte response came back —
`01fe80010000000480400080`, `OK_SuccessfulNegotiation`, SchemaID 2. The control is the reversed ranking
(`-20` p1): also `-20`, correctly. It is the pair that separates *"follows the ranking"* from *"takes
`-20` if it is mentioned at all"*.

**The requirement, unusually, twice with the same number in each series.** `[V2G2-169]` and
`[V2G20-169]`: the SECC picks, from the protocols it supports itself, the one the EVCC ranked highest,
and names that entry's SchemaID. `[V2G2-167]`/`[V2G20-167]` define the field — `1` highest, `20`
lowest. So the station's capability is a *filter* and `Priority` is the *ranking* inside it; `IsoMux`
supports both, so the filter removes nothing and the ranking is all that is left to decide with. `-20`'s
worked example in `8.2.4` shows the response naming the priority-1 entry where array order and priority
order deliberately differ.

**Where it comes from** — `modules/EVSE/IsoMux/v2g_server.cpp:118-142`:

```cpp
const char* iso20_urn = "urn:iso:std:iso:15118:-20";
if (strncmp(iso20_urn, proto_ns, strlen(iso20_urn)) == 0) {
    iso20 = true;
    free(proto_ns);
    return true;              // <-- first -20 entry anywhere wins
}
```

and the caller acts on the flag alone (`connection/connection.cpp:436-443`). The early `return` also
explains a logging asymmetry that is otherwise puzzling: a `-20`-first offer logs **one**
`handshake_req` line, a `-2`-first offer **two**.

**Both of your backends already do this correctly**, which is what makes us think oversight rather than
policy. `EvseV2G` tracks the best rank and never returns early
(`EvseV2G/v2g_server.cpp:228-283`, `ev_app_priority >= app_proto->Priority`); `Evse15118D20` keys a map
by priority and cites the requirement on the line that takes the winner
(`lib/everest/iso15118/src/iso15118/d20/state/supported_app_protocol.cpp:26-42`,
`// [V2G20-167] Highest Prio: 1, Lowest Prio: 20`). `v2g_sniff_apphandshake()` is visibly a stripped
copy of `v2g_handle_apphandshake()` — same opening, same `Failed_NoNegotiation; // [V2G2-172]` line,
same doc comment citing `DIN [V2G-DC-436] ISO [V2G2-540]`. What the copy dropped is the two priority
comparisons. **And the right answer was one hop away**: routed to `EvseV2G` as the ranking called for,
that module would have found `-2` at Priority 1 and answered SchemaID 1.

**Fix.** Walk the whole array, keep the lowest `Priority` among the namespaces the mux can serve, route
on that — the shape `EvseV2G` has. Or, if preferring `-20` is deliberate, one log line at the decision
(*"selected ISO-20 (priority N of M offered)"*) and a comment saying so.

**Interop is unaffected**: all four offer shapes complete against your station. This is a conformance
point, not an outage, and we would rather say so than overstate it.

---

## 2. TLS is capped at 1.2, and `-20` is routed onto it anyway

`IsoMux` serves TLS **1.2 and nothing higher**, by construction, and then hands the connection to
whichever backend the offer names — including `Evse15118D20`. Four arms, one EVCC, one station process
([`2026-08-06-everest-isomux-tls`](../interop-runs/2026-08-06-everest-isomux-tls/notes.md)):

| Arm | Offer | TLS | Result |
|---|---|---|---|
| `iso2-tls12` | `-2` only | 1.2 | ✅ 43 exchanges to `SessionStop` — correct |
| `refused-tls13` | `-20` only, 1.3 pinned | — | ⛔ **alert 70**, `tlsv1 alert protocol version` |
| `both-20first` | `-20` p1, `-2` p2 | 1.2 | ✅ station selected **`-20`**, 60 exchanges, every code `OK` |
| `both-2first` | `-2` p1, `-20` p2 | 1.2 | ✅ station selected **`-20`** anyway, 57 exchanges, every code `OK` |

That the connection could not have been better than 1.2 is not inferred from our client:

```
$ openssl s_client -connect [fe80::…%eth0]:64110 -tls1_2
  Protocol: TLSv1.2, Cipher: ECDHE-ECDSA-AES128-SHA256, subject=CN=SECCCert, O=EVerest, C=DE, DC=CPO
$ openssl s_client -connect [fe80::…%eth0]:64110 -tls1_3
  ssl3_read_bytes:tlsv1 alert protocol version:SSL alert number 70
```

**The requirement**, three places, all in `-20` itself so no `-2` revision caveat applies:
`[V2G20-2356]` — a *shall not* on the station: it may not choose `-20` when the connection carrying the
offer is plain TCP or TLS ≤ 1.2. `[V2G20-1237]` — the mirror on the car: it may not put `-20` in the
offer over those connections. `[V2G20-1805]` — both halves in one clause. Both point at **Table 5**,
where `-20` appears in the 1.3 row only. Two more bound an acceptable fix: `[V2G20-2359]` explicitly
*permits* supporting TLS 1.2 for backward compatibility — serving 1.2 is not the defect, selecting `-20`
on it is — and `[V2G20-1235]` makes the `-20` TLS profile (Tables 6 to 8) apply once `-20` is chosen.

**The consequence worth your time is not the letter of it:** *through `IsoMux`, the `-20` backend is
unreachable by any conformant EVCC and reachable only by one that is not.* A `-20` EVCC must offer
TLS 1.3 (`[V2G20-2365]`, `[V2G20-1264]`). `-20`-only cars get alert 70; backward-compatible ones add
`0x0303` per `[V2G20-2062]`, land on 1.2, and are then obliged by `[V2G20-1237]` to drop `-20` from the
offer — so they get `EvseV2G`. The only route that reaches `Evse15118D20` requires *both* peers to break
the same pair of requirements.

**Our EV was wrong too, and we say so first**: ours put `-20` in an offer over TLS 1.2, which
`[V2G20-1237]` forbids. That is fixed on our side (2026-08-10) and it is why we could see this at all.

**Where it comes from** — `IsoMux/connection/tls_connection.cpp:278-280`:

```cpp
config.cipher_list  = "ECDHE-ECDSA-AES128-SHA256";
config.ciphersuites = "";     // disable TLS 1.3
config.verify_client = false; // contract certificate managed in-band in 15118-2
```

`lib/everest/tls/src/tls.cpp:442-449` turns the empty `ciphersuites` into the version cap and says so.
Those three lines are also in `EvseV2G/connection/tls_connection.cpp:282-284`, where they are **right** —
that module speaks `-2`, and the third comment names the profile out loud. It is the copy into the one
module that fronts both protocols that inherits a `-2`-shaped decision for a `-20`-capable listener.

**Fix.** Either gate the routing on the negotiated version — refuse `-20` from the offer when the
connection is not TLS 1.3, which is `[V2G20-2356]` literally — or give the mux a 1.3-capable profile
and choose per connection. The first is smaller and is what the requirement asks for.

**One thing we did not test and state only because it follows from your code:** `Multiplexer: Proxy
TLS->TCP` means the mux terminates TLS and forwards plaintext to the backend
(`tls_connection.cpp:328-334`). So `Evse15118D20` behind it has no TLS session of its own — its
`enforce_tls_1_3` cannot help, and whatever `-20` derives from the handshake (the vehicle certificate of
`[V2G20-1264]`/`[V2G20-2339]`, and the session-resumption binding of `8.3.4.1.4.3`) is not available to
it. Worth a look while you are in the module; we make no claim about it.

---

## 3. A failed V2GTP header read is logged, and then ignored

Two connections against the same station, six seconds apart, differing in how many bytes of the header
they sent ([`2026-08-10-everest-isomux-shortread`](../interop-runs/2026-08-10-everest-isomux-shortread/notes.md)).

**A — a complete 8-byte header, payload length 0.** No transport error; the EXI decode then fails
because there is no body, which is correct:

```
Incoming connection on eth0
Handling SupportedAppProtocolReq
decode_appHandExiDocument() failed
Connected to proxy module for ISO-2/DIN
```

**B — six bytes of the same header:**

```
Incoming connection on eth0
connection_read(header) too short: expected 8, got 6      ← the transport says no
v2g_incoming_v2gtp() failed                               ← and the caller agrees
Handling SupportedAppProtocolReq                          ← and then it carries on
decode_appHandExiDocument() failed
Connected to proxy module for ISO-2/DIN
```

Having announced that it could not read the message, the station decoded the buffer anyway, concluded
from that decode that the peer does not speak `-20`, and proxied to the `-2` backend — which met the
same bytes and closed.

**Where it comes from** — `IsoMux/v2g_server.cpp:145-179`, `v2g_detect_iso20_support()`:

```cpp
rv = v2g_incoming_v2gtp(conn);

if (rv != 0) {
    dlog(DLOG_LEVEL_ERROR, "v2g_incoming_v2gtp() failed");   // logged, and that is all
}
…
app_protocol_received = v2g_sniff_apphandshake(conn, iso20);  // :172, runs regardless
…
} while ((rv == 1) && not app_protocol_received);              // :178
```

Two problems, and the second is why the first is easy to miss:

1. **The error is logged, not acted on.** `rv != 0` covers a short read (`:48-51`), an invalid header
   (`:53-56`), an oversized payload, and the peer closing. All four continue to `:172`.
2. **The retry condition tests the wrong value.** `rv == 1` is what `v2g_incoming_v2gtp()` returns when
   the **peer closed the connection** (`:45-47`, under that comment). The loop retries only when there
   is nobody left to read from. Its doc comment at `:30` says the function returns *"0 … otherwise -1"*
   and does not mention 1, which may be how the condition came to be written.

`EvseV2G`, which this was forked from, does both correctly — `v2g_server.cpp:387-391` has the
`goto error_out`, and `:473-477` handles the peer-closed case by name.

**Why it matters.** `iso20` is the multiplexer's one decision, and after a failed read it is taken from
a buffer that was never filled — in this build the surrounding setup zeroes `payload_len` and sizes the
stream at 0, so the answer comes out `false`, but that is an accident of the setup code rather than a
property anyone stated. The visible consequence is that every unreadable first message becomes an
ISO 15118-2 session, and the `-2` backend is then blamed for bytes it never had a chance with.

**Fix.** `return false` (or break to where `is_connection_terminated` leads) when `rv != 0`, as
`EvseV2G` does; decide deliberately what the loop condition should be, since with the exit in place it
can only run once as written; and correct the doc comment at `:30`.

**Where the short read came from**: in our probe, `printf '\x01\xfe\x80\x01\x00\x00' | socat`, on
purpose. In the 2026-08-03 run where it first appeared, unknown — and it does not matter to the defect.
Your `connection_read()` loops until the count is satisfied or the sequence timeout expires
(`EvseV2G/connection/connection.cpp:265-300`), so a header split across TCP segments is reassembled;
*"got 6"* means the peer sent six bytes and stopped.

---

## 4. The TLS server boots with `trusted_ca_keys support disabled`

One station process, one PKI, two TLS servers, four milliseconds apart
([`2026-08-10-everest-isomux-trusted-ca-keys`](../interop-runs/2026-08-10-everest-isomux-trusted-ca-keys/notes.md)):

```
15:41:01.165  iso_mux:IsoMux   TLS server on eth0 is listening on port …:64110
15:41:01.165  iso15118_2:Evse  TLS server on lo   is listening on port …:64109
15:41:01.256  iso_mux:IsoMux   <n> certificates != <n> OCSP responses
15:41:01.257  iso_mux:IsoMux   No trust anchors for certificate: C:DE CN:SECCCert DC:CPO O:EVerest
15:41:01.257  iso_mux:IsoMux   trusted_ca_keys support disabled
15:41:01.261  iso15118_2:Evse  <n> certificates != <n> OCSP responses
                               ← and nothing more from EvseV2G
```

**Where it comes from** — two calls that differ in what they ask for:

```cpp
// IsoMux/connection/tls_connection.cpp:292
call_get_leaf_certificate_info(LeafCertificateType::V2G, EncodingFormat::PEM, false);

// EvseV2G/connection/tls_connection.cpp:298
call_get_all_valid_certificates_info(LeafCertificateType::V2G, EncodingFormat::PEM, true);
```

`get_leaf_certificate_info` runs with `include_root = false` (`evse_security.cpp:1371-1374`), so
`certificate_root` is not in the reply and `IsoMux` has nothing to put in `trust_anchor_pem` — it never
sets one. In `lib/everest/tls/src/tls.cpp` the rest is mechanical: `:1048` `if (!tas.empty())` is false,
so `:1057` `openssl::verify_chain()` is never reached and the chain is not registered; `:1062` logs
*"No trust anchors for certificate"*; and `:1077-1080` an empty `chains` produces *"trusted_ca_keys
support disabled"* with `m_server_trusted_ca_keys.update({})`.

`ServerTrustedCaKeys::handle_certificate_cb` (`extensions/trusted_ca_keys.cpp:368-407`, installed at
`:335`) is where the extension would be honoured: when the client sent one, `select()` runs over the
registered chains and the match is installed. With an empty list it returns `nullptr`, nothing is
installed, and the handshake continues on `cfg.chains[0]` (`tls.cpp:940-941`).

**Two things are therefore impossible here, not one.** No chain ever enters the selectable list; and
`get_leaf_certificate_info` returns the **newest single** chain rather than all valid ones, so even with
a trust anchor there would be one chain to choose between. `EvseV2G` gets both right with the one call
it makes.

**The requirement.** `[V2G2-651]` obliges **every** EVCC to send a `trusted_ca_keys` extension
(IETF RFC 6066) listing the V2G roots it holds — unconditionally. `[V2G2-871]` then obliges a station
outside a private environment to present a chain rooted at *one the EV named*, with `[V2G2-923]` as the
narrow escape where it cannot. An EV handed a chain that does not trace to a root it trusts must treat
it as unvalidated (`[V2G2-924]`) and abandon the TLS setup (`[V2G2-875]`). `[V2G2-878]` puts the ceiling
at ten concurrently valid V2G root certificates per root CA, so the multi-root case is provided for.

**With one V2G root nothing shows** — the station serves its only chain, which is what `[V2G2-923]`
would have it do anyway, and that is why these two lines sat in our logs from 2026-08-06 unread. With
two, `IsoMux` serves the first and an EV holding only the other walks away.

**Fix.** Have `IsoMux` make the call `EvseV2G` makes — `get_all_valid_certificates_info` — and set
`trust_anchor_pem` from `certificate_root`; that repairs the trust anchor and the single-chain
limitation together. Or, if single-chain is deliberate here, say so at the call site, because the
inherited machinery then warns on every boot for nothing.

**We have not run the failing case.** It needs a station with two V2G chains under different roots and
an EV that sends the extension; ours does not send it, and `openssl s_client` will not without
patching. This finding is your two log lines plus the source that produces them — no session.

---

## 5. Not part of this

The OCSP warning on the line above §4 is a **different report** and a different cause: a dropped
struct member in a type conversion, which hits `EvseV2G` as well —
[`everest-evse-security-ocsp-dropped.md`](everest-evse-security-ocsp-dropped.md). Fixing that does not
touch anything here, and fixing anything here does not touch it. Worth keeping apart even though the
two surface four milliseconds apart on the same boot.

Two cosmetic things noticed in the same connection path and not chased: `connection_read(header)
failed: Success` when the read returns `-1` without setting `errno`, and the peer address in *"Incoming
connection on eth0 from `[a00:deb2:0:0:fe80::]:57010`"*, which does not look like the link-local address
the connection came from.

And one in a neighbouring module, mentioned only because it is in a file you would open for §1:
`Evse15118D20`'s SAP handler treats `-20:AC` and `-20:DC` as interchangeable when ranking — both land in
the same priority map, so a DC-configured station could answer the SchemaID of an `-20:AC` entry. **Not
run, not reported**, and it may well be deliberate.

---

## On citing the standard

We hold the ISO documents under licence and quote none of them. The identifiers above are how the
industry refers to these obligations, and each sentence states what a requirement *obliges* rather than
how it is worded; our rule for that is [`docs/normative-basis.md`](../normative-basis.md).

**One caveat, stated rather than left to be found.** Our ISO 15118-2 document is the **2022 DIS
revision**, not the 2014 edition most `-2` stacks target, so a `[V2G2-…]` citation from us is strictly
evidence about the revision. Where it matters we have corroborated: for §1 the same obligation is
`[V2G20-169]` in the `-20` FDIS, which needs no caveat, and the 2019 *ISO 15118 Manual* — written
against ISO 15118-2:2014 — describes the same rule. §2 rests on `-20` alone. §4's citations carry the
caveat unrelieved, and the material there is old and stable but we have not corroborated it a second
way.

---

## Before sending

Per finding, what is established and what is not:

- [x] **§1 reproduced three times** — 2025.10.0 (2026-08-03), 2026.02.1 (2026-08-05), TLS (2026-08-06),
      each with the discriminating offer *and* the reversed-ranking control; byte-identical request and
      response every time.
- [x] **§2 reproduced across four arms** in one station process, with `openssl s_client` establishing
      the version cap independently of our client.
- [x] **§3 reproduced deliberately, with a control** — two `socat` connections six seconds apart
      differing by two bytes, no EV and no car simulation. A maintainer can run it in a minute.
- [ ] **§4's failing case has not been run.** Two roots and an EV that sends `trusted_ca_keys`. **Say
      this in the issue** rather than letting a reader infer a session that never happened.
- [x] **Every line reference re-read against the built 2026.02.1 tree**, on 2026-08-09 (§1, §2) and
      2026-08-10 (§3, §4): `IsoMux/v2g_server.cpp:30`, `:45-47`, `:48-51`, `:53-56`, `:118-142`,
      `:145-179`, `:172`, `:178`; `IsoMux/connection/connection.cpp:436-443`;
      `IsoMux/connection/tls_connection.cpp:278-280`, `:292`, `:328-334`;
      `EvseV2G/v2g_server.cpp:228-283`, `:387-391`, `:473-477`;
      `EvseV2G/connection/connection.cpp:265-300`; `EvseV2G/connection/tls_connection.cpp:282-284`,
      `:298`, `:316`; `lib/everest/tls/src/tls.cpp:442-449`, `:940-941`, `:1048`, `:1057`, `:1062`,
      `:1077-1080`; `lib/everest/tls/extensions/trusted_ca_keys.cpp:335`, `:368-407`;
      `evse_security.cpp:1371-1374`;
      `lib/everest/iso15118/src/iso15118/d20/state/supported_app_protocol.cpp:26-42`.
- [ ] **Check they are still there on the day.** These are small functions that could be touched at any
      time; all four were unchanged at `main` when this was written.
- [ ] **Lead each section with its consequence, not its clause.** §1: *both of your backends implement
      this and the router in front of them does not.* §2: *the `-20` backend is reachable only by a
      non-conformant EV.* §3: *"failed to read the message"* followed by *"handling the message"*.
      §4: *every EV sends that extension.*
- [ ] **Ask whether §1 and §2 are deliberate** before calling either a defect. If preferring `-20` is
      policy, that answer is interesting on its own and the fix is the log line.
- [ ] **Say that §1 costs no interop.** Overstating a small finding is how the next one gets ignored.
- [ ] **Decide the shape.** One issue with four headings, or four issues — this document works either
      way, and a maintainer's tracker should decide it, not us.
- [ ] **Post under your own name, in your own words.**
