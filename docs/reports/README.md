# Findings written up for the counterparty they belong to

Everything this suite found *in somebody else's stack*, drafted as a report they could act on. All of
them are **drafts, not sent**: they are written to be posted by a person, under their own name, in
their own words — every one ends with a *Before sending* checklist whose unticked items are the parts
only a human can do.

Each report follows the same shape, and the shape is the point: what was run and against which commit,
the observed behaviour with their own logs rather than our summary of them, where in *their* source it
appears to come from, why we think it is worth fixing, and a suggested fix offered rather than
asserted. Where we worked around something to keep going, the workaround is named — including when it
was our own configuration we bent.

## What is here

| Report | To | Issues | One line |
|---|---|---|---|
| [`evdriveflow-headless-session.md`](evdriveflow-headless-session.md) | EDF Lab (eVDriveFlow) | **1**, **2**, plus two secondary | The documented no-GUI path cannot complete a session, for two independent reasons: EOF on stdin reads as "Enter pressed", and `hasattr` on an xsdata `Optional` field nulls the EV's own target SOC |
| [`evdriveflow-authorization-setup.md`](evdriveflow-authorization-setup.md) | EDF Lab (eVDriveFlow) | **4** | A station offering PnC alongside EIM — which `[V2G20-2566]` explicitly permits — raises `NotImplementedError` in their EV: the loop has no `break`, so any offered service it does not implement ends the session wherever it sits in the list. What looked like a second defect behind it was issue 1 (the `stdin` wall), and saying so is half the report |
| [`evdriveflow-session-id.md`](evdriveflow-session-id.md) | EDF Lab (eVDriveFlow) | **5** | Their `-20` SECC **never reads the incoming SessionID**: fifteen `process_*_request.py` handlers write their own id into the response header and none compares, so `[V2G20-460]` is unimplemented and any request at all is served as the session owner's. Source only — but the probe was run against EVerest's `-20` station first, which **refuses** the same zero id, so the instrument and the requirement are both demonstrated. Two of the four `-20`/`-2` stacks audited get it right, one has a narrow defect, this one has none of it |
| [`evdriveflow-session-id-entropy.md`](evdriveflow-session-id-entropy.md) | EDF Lab (eVDriveFlow) | **6** | `generate_random_session_id()` draws `secrets.randbelow(10⁸)` and zero-pads it to eight ASCII digits, so the 64-bit `SessionID` never leaves a 10⁸-wide corner of its field: **26,6 bits of entropy where `[V2G20-2621]` requires 58**, and a repeat expected after ~10 000 sessions. The generator is right and the range is not, which is the whole report. Their own docstring cites the requirement and says it *might have security issues* — so the news is the number and the one-line fix (`secrets.token_bytes(8)`). Says out loud that it buys nothing until issue **5** is fixed: a value nobody compares does not have to be hard to guess |
| [`evdriveflow-service-discovery-filter.md`](evdriveflow-service-discovery-filter.md) | EDF Lab (eVDriveFlow) | **3** | `SupportedServiceIDs` is optional — omitting it means *"list everything"* — and their station dereferences it unconditionally, so **every EV that does not pre-filter** dies at the fifth message. In the same three lines, a filter naming neither of their two services leaves the mandatory `EnergyTransferServiceList` unset behind an `OK`. Both are the `Optional`-is-`None` family the report counts: seven `hasattr` sites in four files, on both sides |
| [`tux-evse-tls.md`](tux-evse-tls.md) | IoT.bzh (tux-evse) | **A**, **B** | Over TLS the EVCC signs every `AuthorizationReq` (so no shipped scenario runs over TLS at all), and the pinned cipher profile contains neither suite ISO 15118-2 prescribes |
| [`tux-evse-spin.md`](tux-evse-spin.md) | IoT.bzh (tux-evse) | **C**, **D** | One connection that pauses or closes sends the binder into a 200,000-line-per-second log loop — and SIGTERM stops the logging without ending the process |
| [`tux-evse-capture-fidelity.md`](tux-evse-capture-fidelity.md) | IoT.bzh (tux-evse) | **E**, **F** | A replayed capture never puts the car's real protocol offer on the wire — their converter parses it and drops it — and the closing SDP verb is hardcoded to the wrong API in DIN scenarios |
| [`everest-evsev2g-session-log-responses.md`](everest-evsev2g-session-log-responses.md) | EVerest | one | `session_logging` publishes every **response** sized by `conn->payload_len`, which still holds the preceding *request's* length — the record is taken before `v2g_outgoing_v2gtp()` writes the response header — so a logged response is truncated, or padded with stale buffer, under the correct message name. Measured against their own published stream on **2026.02.1**: a complete DC charge, 43 requests byte-exact and 43 of 43 responses carrying the request's length — and 42 of them the version byte `0x00`, so the record is not a V2GTP frame at all |
| [`everest-evse-security-ocsp-dropped.md`](everest-evse-security-ocsp-dropped.md) | EVerest | one | `to_everest(CertificateInfo)` copies six of seven members and forgets `ocsp`, so the OCSP data libevse-security collected for the chain never reaches the TLS server: it caches nothing, and **no EVerest station ever staples an OCSP response**. `[V2G2-871]` and `[V2G20-2388]` require it, `[V2G20-2372]` makes the EV always ask, and `[V2G2-873]` makes a conformant EV close the connection when nothing comes back. Measured off their own MQTT reply — no EV, no session |
| [`everest-isomux.md`](everest-isomux.md) | EVerest (`IsoMux`) | **four**, one per section | Four defects in the one module that fronts both protocols, merged into one report on 2026-08-10 because they are one module and — for three of them — one shape, *a decision taken on information the module does not have or does not read*: the backend is chosen on the **first** `-20` entry and never on `Priority` (`[V2G2-169]`); TLS is capped at 1.2 and `-20` is routed onto it anyway, so the `-20` backend is reachable **only** by a non-conformant EV (`[V2G20-2356]`); a failed V2GTP header read is logged and then ignored, so the routing decision is taken from a buffer that was never filled; and the TLS server boots with **`trusted_ca_keys support disabled`**, the extension `[V2G2-651]` obliges every EV to send. Both backends behind the mux implement the first correctly |
| [`everest-d20-ac-namespace.md`](everest-d20-ac-namespace.md) | EVerest (libiso15118) | one | A DC-only `Evse15118D20` answers **`OK_SuccessfulNegotiation`** to an offer containing nothing but `urn:iso:std:iso:15118:-20:**AC**` — both namespaces go into the same priority map with no reference to what the station serves. `[V2G20-169]` makes the station's own capability a filter *before* the ranking; this implements the ranking and skips the filter. The session then spends session setup, an authorization and a token before dying at `ServiceDiscovery` on services 2 and 6. **The sibling of `everest-isomux.md` §1, from the other side** |
| [`everest-d20-meter-info.md`](everest-d20-meter-info.md) | EVerest (libiso15118) | one | Their `-20` charge loop reads `MeterInfoRequested` off the request and forwards it as a feedback signal, then never sets `meter_info` on the response — one `TODO` between the two. Measured over a complete 70-exchange DC session with a control: **our request changed by one bit (`0x81`→`0xa1`), their responses were byte-identical between the two runs**, so the answer does not depend on the question. `[V2G20-1082]` makes it a *shall* once asked; with the element never set, `[V2G20-1083]`'s `MeteringConfirmation` and `[V2G20-1919]`'s kWh receipt have no way to start, so the `-20` signed-metering path is unreachable rather than partial. **The first of these that needed a capability of ours first**: our EVCC hardcoded the ask to `false` until the same morning |
| [`everest-d20-trust-anchor.md`](everest-d20-trust-anchor.md) | EVerest (libiso15118 · libevse-security) | one | The `-20` station loads the **MO** root as a trust anchor for the EV's TLS client certificate and never an **OEM** root, so with their own unmodified PKI a **vehicle** certificate gets `certificate verify failed` and a **contract** certificate gets `Verify certificate result is okay` followed by their own line **`Vehicle Cert is available`** — which then becomes the `[V2G20-2677]` resume binding. `[V2G20-2331]` anchors vehicle certificates at an OEM (or V2G) root; `[V2G20-2401]` names exactly those two in `certificate_authorities`; clause 7.3.1 puts contract certificates at the application layer. **Not fixable in the `-20` module alone**: `CaCertificateType` has no `OEM` value to ask with |
| [`everest-d20-ocsp-absent.md`](everest-d20-ocsp-absent.md) | EVerest (libiso15118) | one | `openssl s_client -status` against their `-20` station gets **`OCSP response: no response sent`** on both TLS versions, because `libiso15118` contains no OCSP handling at all — not requested (`include_ocsp = false`), nowhere to carry it (`SSLConfig` has no member), nothing to send it (no `SSL_CTX_set_tlsext_status_cb`). `[V2G20-2372]` makes every `-20` EV ask and `[V2G20-2388]` obliges a public SECC to answer. **Separate from the `to_everest` one on purpose**: neither fix alone produces a staple. Controlled against `IsoMux`, whose own `OcspCache::lookup` ran on the same request, so the extension was demonstrably on the wire |
| [`everest-d20-client-auth.md`](everest-d20-client-auth.md) | EVerest (libiso15118) | **§1**, **§2** | Their `-20` TLS server switches client-certificate verification on **from the `supported_versions` list in the `ClientHello`**: offer TLS 1.3 and it demands a vehicle certificate and refuses without one; offer TLS 1.2 alone and it sends no `CertificateRequest` at all — and then answers `supportedAppProtocolReq(-20:DC)` with `OK_SuccessfulNegotiation` and mints a session id, so an anonymous peer reaches `AuthorizationSetup`. `[V2G20-2400]` is unconditional. Two `openssl s_client` calls reproduce it — no EV, no client PKI. §2 is the same function's other three omissions: no `certificate_authorities` (`[V2G20-2401]`), OpenSSL's signature-algorithm defaults rather than Table 8 (`[V2G20-1667]`), a named group outside Table 7 (`[V2G20-2460]`) — while the Table 6 cipher suites are set exactly right |
| [`everest-loop-shutdown.md`](everest-loop-shutdown.md) | EVerest | one | A failed TLS handshake ends `Evse15118D20`'s V2G accept loop, so one bad handshake takes the station down for the rest of its life — while the process stays healthy and nothing supervising it notices |
| [`everest-d20-sequence-timeout.md`](everest-d20-sequence-timeout.md) | EVerest (`libiso15118`) | one | `V2G_SECC_Sequence_Timeout` is a single 60 s constant armed from one call site, so a silent EV holds the **charge loop** — contactor closed — for a minute where Tables 216/217 give the SECC **0,5 s** (`[V2G20-1500]`, `[V2G20-1502]`). Measured against a normal-charge control: **60,0025 s** between their own `DcChargeLoopRes` and their own timeout verdict, from their log. The arming, disarming and session-stop around it are all correct — one value, not a broken design. Their log line names a third number again (40 s, the EV's row of Table 215) |
| [`everest-evsev2g-paymentdetails-crash.md`](everest-evsev2g-paymentdetails-crash.md) | EVerest (`EvseV2G`) | one | **A malformed contract certificate in `PaymentDetailsReq` crashes the V2G module.** `handle_iso_payment_details` parses the cert at `iso_server.cpp:982`, **uses** it at `:990` (`getEmaidFromContractCert`), and only checks the parse result at `:1006`. On unparseable DER the parse returns a **null** `certificate_ptr`, so line 990 runs `certificate_subject(nullptr)` — which opens `assert(cert != nullptr)` and then `X509_get_subject_name(cert)`: **SIGABRT** in a debug build (their CMake default), **SIGSEGV** in release. Reachable **pre-authentication** — `-2` TLS is unilateral and the crash is during parsing, before any signature check. Demonstrated in isolation (an OpenSSL null-deref repro), not yet against a running station. **Not** a controllable write — availability, not code execution — and **no ISO clause**, it stands on the crash. Josev (whole body in `try:`) and ours (`X509CertificateLoader` in try/catch) both answer `FAILED` on the same bytes. One reordered check fixes it |
| [`everest-evsev2g-certificate-update.md`](everest-evsev2g-certificate-update.md) | EVerest (`EvseV2G`) | one | `handle_iso_certificate_update` is `// TODO: implement CertificateUpdate handling` and `return V2G_EVENT_NO_EVENT` — the ordinary carry-on value, not `IGNORE_MSG` — so the dispatch **sends the response anyway**. `iso2_BodyType`'s bodies are a **union**, none of the three inits touches its members, and every `*ResType` starts with `ResponseCode`: the answer to a contract-renewal request is therefore the **previous message's** response code, `OK` in any session that got that far, with all five other mandatory elements stale bytes of another type. `[V2G2-556]` makes acting on the request a *shall*, `[V2G2-558]` makes `FAILED` the owed answer, `[V2G2-736]` the schema-conformant fill. Explicitly **not** a memory disclosure — the encoder bounds-checks lengths — and explicitly not measured. Bounded by a sweep of **all seventeen** handlers: sixteen assign `ResponseCode` between 2 and 13 times, this one never. Josev does not implement the feature either and answers `FAILED` correctly in nine lines |
| [`everest-evsev2g-metering-chain.md`](everest-evsev2g-metering-chain.md) | EVerest (`EvseV2G`) | **two** | The ISO 15118-2 signed-metering path is open at both ends. **Out:** `handle_update_meter_info` reads `powermeter.energy_Wh_import.total` and never the `energy_Wh_import_signed` sibling on the same argument — there is nowhere to put it, and `SigMeterReading` occurs nowhere in the module — so `MeterInfo` reaches the wire with **two of its five elements**, against `[V2G2-902]`'s *shall* that it be the meter's own output and nothing else. **Back:** the EV's signed `MeteringReceiptReq` (`[V2G2-903]`, a *shall* on every PnC car) hits `publish_iso_metering_receipt_req`, an empty `// TODO`, and `ResponseCode = OK` unconditionally; `check_iso2_signature` has one call site in the module and it is the `AuthorizationReq`. Not verifying is permitted — `[V2G2-904]` is a **may** — but its NOTE 1's secondary actor is the alternative that `may` presupposes, and nothing is forwarded either. Measured off their own recorded frames with an independent codec |
| [`everest-d20-rng-entropy.md`](everest-d20-rng-entropy.md) | EVerest (libiso15118) | one | Four sites fill a security-relevant array from `std::mt19937` seeded with **one 32-bit** `random_device` draw, so the 64-bit `SessionID` and the 128-bit PnC `GenChallenge` both carry at most **32 bits** — against `[V2G20-2621]`'s 58 and `[V2G20-698]`'s 120, with `[V2G20-835]` requiring a cryptographically secure generator in the first place (and `[V2G2-835]`/`[V2G2-698]` saying the same under the same numbers). Measured twice: **49 of 49 SessionIDs their station had issued across twenty earlier interop runs were recovered from the 2³² seed space**, and a 16-byte challenge repeats 8 times in 262 144 draws where a `/dev/urandom` control repeats 0 — predicted 8, observed 8. Their own `EvseV2G` reads `/dev/urandom` for the same two values in `-2` and DIN |
| [`everest-evsev2g-session-id-zero.md`](everest-evsev2g-session-id-zero.md) | EVerest (`EvseV2G`) | one | Their `-2` station's `[V2G2-460]` check carries a `received_session_id != 0` conjunct, so a mid-session request with a SessionID of **eight zero bytes is served as the session owner** — and zero is the value ISO reserves for *"I have no session"*, and the value their own `v2g_session_id_from_exi` leaves behind when a peer echoes nothing. Three arms with a control on both sides: a flipped bit is refused, zero is not, and the responses to zero and to the correct id differ in no byte. Their DIN twin, their `-20` implementation and their own DIN test all get it right — the test with a **non-zero** id, which is why it survived |
| [`everest-iso20-ac-contactor-latch.md`](everest-iso20-ac-contactor-latch.md) | EVerest (libiso15118) | one | The `-20` AC `PowerDelivery` state assigns a **pointer** to its `bool ac_connector_closed`, so a board-support module reporting the contactor **open** latches it closed, cancels the timeout that would have refused, and answers `PowerDeliveryRes(OK)` — the mechanism by [`tools/everest-contactor-probe/`](../../tools/everest-contactor-probe/README.md), the behaviour [against their running station](../interop-runs/2026-08-09-everest-ac-contactor-injection/notes.md), 2 of 2 with a control |
| [`josev-iso20-pki-curve.md`](josev-iso20-pki-curve.md) | EVerest (`ext-switchev-iso15118`) **and** SwitchEV (iso15118) | one, filed twice | `create_certs.sh` branches on `-v iso-2\|iso-20` and the `-20` branch selects the same `prime256v1` as `-2`, under its own `TODO` — so `-20` contract provisioning cannot complete at all, the schema's key-wrap curve choice being secp521r1 or x448 and nothing else |
| [`josev-iso20-renegotiation.md`](josev-iso20-renegotiation.md) | SwitchEV (iso15118) **and** EVerest (`ext-switchev-iso15118`) | **§1**, **§2** | Their `-20` EVCC sends a correct `SessionStopReq(ServiceRenegotiation)`, our station answers `OK` without ending the session — and their EVCC then kills the link, because the renegotiation branch of `SessionStop.process_message` sets `next_state = ServiceDiscovery` and is **the only one of 28 transitions in the file that never calls `create_next_message(...)`**. Their own framework says so: *"Field `next_v2gtp_msg` is None but must be set because next state is not Terminate"*. §2 is upstream-only — `DCWeldingDetection` hardcodes `ChargingSession.TERMINATE`, so a DC/MCS EV cannot even ask — and **EVerest's fork has already fixed it**, which is worth telling upstream |
| [`josev-iso20-charge-loop-timeout.md`](josev-iso20-charge-loop-timeout.md) | SwitchEV (iso15118) | one | `V2G_SECC_SEQUENCE_TIMEOUT_{AC,DC,WPT}_CL = 0.5` are transcribed from Tables 216/217 into their own timeouts file and **referenced nowhere**; both charge-loop states hand on the 60 s baseline instead, so a silent EV holds the loop — contactor closed — for a minute where `[V2G20-1500]`/`[V2G20-1502]` give 0,5 s. Two lines to fix, and the constants already exist. **Source only, and the checklist says so first**: the identical behaviour was *measured* against EVerest's implementation the same day, so two independent `-20` stacks flatten the same override |
| [`josev-iso20-pause-resume.md`](josev-iso20-pause-resume.md) | SwitchEV (iso15118) | one | Pause/resume works in ISO 15118-2 and cannot work in -20: the `-20` `SessionSetup` compares the resumed session ID against the *live* connection instead of the preserved context, which its states never fill — so `OK_OldSessionJoined` is unreachable and every resume degrades to a new session |
| [`pyevjosev-manifest-services.md`](pyevjosev-manifest-services.md) | EVerest | one | `PyEvJosev`'s manifest under-documents `supported_d20_energy_services`, so a valid MCS configuration looks impossible — and an unrecognised entry is dropped in silence |
| [`v2gdecoder-fuzzy-grammar.md`](v2gdecoder-fuzzy-grammar.md) | FlUxIuS (V2Gdecoder) | **A**, **B** | A frame valid under two grammars is decoded by whichever sits first in the array, silently — and their DIN grammar rejects a real `ChargeParameterDiscoveryRes`, which the same fallback then answers for |
| [`libcbv2g-grammar-deviations.md`](libcbv2g-grammar-deviations.md) + [`libcbv2g/`](libcbv2g/) | EVerest (libcbv2g / cbexigen) | **A**, **B**, **C** | The document element codes are ordered by **type name** rather than element qname — five of the eight generated document grammars deviate from EXI §8.5.1, and in ACDP two messages swap identity so one decodes cleanly as the other; the WPT mid-sequence particle grammar returns success while **silently dropping** a set field; and every `minOccurs="2"` repeating particle gets a loop state with no exit, so three WPT types cannot be encoded at all — reproduced by [`tools/cbv2g-defect-probe/`](../../tools/cbv2g-defect-probe/README.md), and bounded by [`tools/cbv2g-grammar-sweep/`](../../tools/cbv2g-grammar-sweep/README.md) over all 4 792 generated states |

**Thirty-nine filings across six projects**, and three of them now have to be sent **twice**.
`create_certs.sh` lives in `SwitchEV/iso15118` and in EVerest's fork of it, byte-identical in the
relevant block and 100 lines apart everywhere else, so one merge will not reach the other tree. The
contactor one is the same trap in a tidier form: `power_delivery.cpp` is byte-identical — 5663 bytes,
same SHA-256 — in `EVerest/everest-core` at `lib/everest/iso15118/` and in standalone
`EVerest/libiso15118`, and which of the two is generated from the other could not be told from outside.

The **eighteenth** went out of that pattern and came back into it within a day. It was written from a
source reading plus a probe, with *"this was not observed on the wire"* as the first item on its checklist;
the reproduction landed the same afternoon — 2 of 2 against their stock SIL, with a control that fails
the way it should — and the item is ticked. Worth recording because the intermediate state was the
right one to be in: the defect was not a matter of opinion even then, but a report that had blurred
"we read your source" into "we observed your station" would have been the fastest way to have a real
finding dismissed.

The **nineteenth** is the first one in this directory that has to say *our own peer was wrong too*. The
run that produced it needed an EV to offer ISO 15118-20 on a TLS 1.2 connection, which `[V2G20-1237]`
forbids — so the session that shows their station selecting `-20` there is a session our EVCC should
never have offered. The report says so before it says anything else, and then gives the two reasons it
does not think that weakens the finding: the SECC's obligation is written separately from the EVCC's
precisely for the case where the car gets it wrong, and the argument that actually matters — that no
*conformant* `-20` car can reach the multiplexer's `-20` backend at all — does not use that session.
It is also the first filing that exists **because the documents arrived**. The run is from 2026-08-06
and the notes closed it as *"a layering question, and a bigger conversation than this"* — deliberately
unfiled, on the grounds that we did not hold the requirement text. That changed on 2026-08-08
([`normative-basis.md`](../normative-basis.md)), and two observations parked as questions turned out to
be a `shall not` and, next door in the same function, a `shall`.

The **twentieth** is that second one: `IsoMux` never reads SAP `Priority`. It had been written down
three times and filed none, under a note that said the requirement had not been checked — the check now
comes back `[V2G2-169]`, and `[V2G20-169]` beside it, which happens to be the one case in this
directory where the `-2` document caveat could be answered rather than merely declared: three
independent places say it, one of them written against the 2014 edition.

The two are separate **filings** on purpose even though the same `if` decides both — sections of one
report since the merge, but two issues to post — and that pair is the clearest illustration of why this
directory splits things. They are different defects — one is *"do not
route `-20` here"*, the other *"route the entry the car ranked first"* — with different fixes, different
severities (one makes a backend unreachable, the other is a conformance point that costs no interop),
and different answers available to a maintainer: the first might reasonably be *"the mux is not meant
for that"*, the second cannot be, because the two modules behind the mux both implement the rule
already. Filed together, one reply would have covered both and the weaker answer would have decided it.

[`everest-d20-client-auth.md`](everest-d20-client-auth.md) applies that rule to a report written as one
piece from the start: two numbered sections, and the checklist says to file two issues. §1 — the station
decides whether to authenticate the EV from what the EV *offered* — has a reasonable answer available
(*"the TLS 1.2 path exists so the same process can serve ISO 15118-2"*), and if §2 were part of the same
issue that answer would close it too. §2 has none: the `certificate_authorities` extension, the Table 8
signature algorithms and the Table 7 named groups are missing whatever the verify mode ends up being.
One reading of one function, one patch a maintainer would plausibly write — and still two issues,
because that is where a reply lands.

The letters and numbers are per counterparty and exist to
keep separate filings separate: IoT.bzh's A and B are the TLS pair, C and D the loop and the signal
handler, E and F what a converted capture loses — six issues, not one, and a fix for any of them does
not touch the others. EDF's 1 and 2 are likewise independent, and fixing 1 is what reveals 2; their 3
is in a different file again and is the one that needs no misbehaviour from the other side at all.
Their 4 is the one that had to be *un*-split: it was drafted as two, because the session also died
behind it, and the second half turned out to be issue 1 — the `stdin` wall — seen from the far side.
Half of that report is now the paragraph saying so, which is the only thing that stops it reading as a
duplicate.
V2Gdecoder's A and B are the same shape again: independent, and the first is what makes the second
expensive to find. libcbv2g's are three different grammars in the same generator, and its C is the one
finding in this directory that is not a difference of opinion at all — three types that no caller can
encode.

The libcbv2g report is the only one where **we changed our own stack because of what we found**: this
project reproduced both of those grammars deliberately, for byte-compatibility with the reference
encoder, and stopped on 2026-08-08. The report says so, which is why its A is written as a question
rather than a verdict — if there is a rationale for the sort key we cannot see, we would change back.

It is also the only one that has since been **bounded**. All three came out of round-tripping our own
corpus, which limits them to the grammars our vectors walk; on 2026-08-11 the generated C was read
instead, all 4 792 states of it
([run notes](../interop-runs/2026-08-11-libcbv2g-grammar-sweep/notes.md)). That found no fourth defect —
261 content models hold — and it corrected A: the ordering is a sort by type name reaching five document
grammars, not a grouping of shared types confined to ACDP. A filing that says how far the search went is
worth more than one that does not, and this is the first here that can.

The **two entropy reports** are the first pair here that no message could have produced. Every other
finding in this directory is visible in something a peer sent or failed to send; `[V2G20-2621]` and
`[V2G20-698]` are requirements about *how many bits are inside* a field whose width, position and
schema validity are all correct. Four `-20` stacks were audited, all four emit a legal SessionID every
session, and two of them carry a third of the entropy the standard asks for. No corpus, no reference
codec and no loopback test can see that — which is worth saying out loud in a repository whose usual
oracle is bytes.

They are also the pair that best shows what keeping old artifacts buys. The EVerest one is a
*measurement* rather than a source reading only because twenty earlier runs had recorded 49 SessionIDs
their station issued, for entirely unrelated reasons; recovering all 49 from a 32-bit seed space made
a claim about their binary out of what would otherwise have been a claim about their code
([run notes](../interop-runs/2026-08-11-everest-d20-rng-entropy/notes.md)). The eVDriveFlow one had no
such luck and says so on its first line.

And they are split for the usual reason, sharpened: same requirement, two projects, two entirely
different causes — a correct generator used over a 10⁸ range, and a full range filled by a generator
that is not cryptographic. A single report saying *"use a CSPRNG"* would have been **wrong about
eVDriveFlow's code**, which already does.

## What is deliberately not here

Observations we could not raise honestly. Three kinds recur:

- **Design properties, not defects.** tux-evse's responder answers only the car in its recording; that
  is what a replayer is. Those are written as questions in an *Also seen* section, or left out.
- **Things we caused.** Where a run needed us to deviate — unpinning our cipher list, relaxing an
  `expect` block, editing a value inside a throwaway container — the deviation is recorded in the run
  notes and the finding is only reported if it survives without it.
- **Our own faults.** Those go the other way: into the app, as a fix and a regression test. Three of
  them came out of these runs (a sequence guard that closed the socket instead of answering, a
  hardcoded 2020 timestamp, an AC schedule rounded 40 W below what a real charge point offers). The
  interop matrix in the top-level [`README.md`](../../README.md) marks which counterparty found each.

## Before any of these goes out

The checklists are not decoration. The recurring items:

- **Reproduce it yourself** — every report has this ticked, and each says with what: their binder and
  their scenario, their EV and their PKI, their manifest and their config. A finding that needs our
  stack on the other end to appear is a weaker finding, and where that is the case it says so.
- **Re-read the citations before sending**, against the tree rather than against the draft. Every
  `file.cpp:line` in these reports was checked again on 2026-08-07; a line number that has drifted is
  the fastest way to have a real finding dismissed.
- **File separately what will be fixed separately.** Filing two issues together invites one answer.
- **Ask before asserting** where the thing may be a decision rather than an oversight.
- **Offer patches only if they want them.** Every suggested fix here has at least two reasonable
  shapes, and which one belongs in their tree is theirs to choose.
- **Post under your own name.** These are drafts for a person to send, not messages from a test suite.
