# Citing the standard

This repository does not contain the ISO 15118 requirement text and never will — it is ISO's copyright
and licensed per copy, the same reason `**/Schemas/` holds only a placeholder README and the build
fetches the schemas under ISO's Customer Licence Agreement.

Until 2026-08-08 that meant several findings ended in *"not decidable from here"*. Some of those are now
decided, because the documents exist **locally** in `onlyLocalDoNotCommit/` — ignored by
[`.gitignore`](../.gitignore), never staged, never pushed.

## The rule

**Clause identifiers and paraphrase go into `docs/`. ISO prose does not.**

A requirement ID such as `[V2G20-2545]` is an identifier — it is how the whole industry refers to these
obligations, and it carries no text. A one-line statement of what a requirement *obliges* is a fact about
the standard, and stating it is what every conformance document does. Copying the sentence is
redistribution. The line is not subtle; stay on the near side of it.

When a finding rests on a requirement, cite the ID and say what it obliges. A reader with the document
can check you in ten seconds; a reader without one still learns which obligation is at stake.

## What is available, and how much weight each carries

| Document | Status | Weight |
|---|---|---|
| ISO/FDIS 15118-20:2022 (E DIN EN ISO 15118-20:2022-03) | **Draft.** Final draft, comment period closed 2022-04-18 | High. FDIS→IS is normally editorial and requirement IDs are stable, but it **is** a draft and the published IS is ISO 15118-20:2022 |
| ISO/DIS 15118-2:2022 | **Draft** of a *revision*, ballot closed 2023-01-02 | **Read the caveat below** |
| EN ISO 15118-8:2020 | Published | Full, for what it covers — wireless physical and data link layers only |
| *ISO 15118 Manual*, Mültin, 2019 | Not a standard | Explanatory. Never a citation for a conformance claim |

### The -2 caveat, which matters more than it looks

**Our `-2` stack implements ISO 15118-2:2014.** The schemas the codec is generated from are the 2014
ones. The document available here is the **2022 DIS revision** — a different document, in ballot, whose
requirements may not have survived into anything published.

So a `[V2G2-xxx]` citation from it is evidence about *the revision*, not necessarily about the standard
our code targets. Most requirement IDs are carried over unchanged from 2014 and the risk is low, but it
is not zero, and it must be stated whenever a `-2` finding leans on it. `-20` has no equivalent problem:
FDIS and IS are the same generation as the schemas we build against.

### And a handling note

Both ISO PDFs are **personalised**. The -20 draft carries a Beuth download watermark with the licensee's
name and customer number **on every page**; the Mültin manual carries a per-transaction line. A leaked
copy is attributable. That is the concrete reason `onlyLocalDoNotCommit/` is ignored rather than merely
untracked — and the reason extracted plain text belongs in a scratchpad outside the working tree, not
next to the sources.

## What has been decided from these documents

Each of these was an open question in [`open-work.md`](open-work.md) or a run note before 2026-08-08.

### ISO 15118-20 session resumption

- **The SECC must verify that a resume comes from the same EVCC.** `[V2G20-2545]` — a *shall*; the
  methodology is explicitly left to the CSO or SECC manufacturer. The accompanying notes state the threat
  in as many words: a second EV that reuses another's SessionID would inherit that EV's authorization,
  PnC or EIM alike.
- **8.3.4.1.4.3 is the standard's own worked example** of such a methodology, and it is exactly what
  EVerest implements: `SHA-512(SessionID ‖ SHA-512(vehicle leaf certificate))`, the certificate taken
  from the verified TLS handshake (`[V2G20-2630]`, `[V2G20-2550]`, `[V2G20-2633]`, `[V2G20-2554]`). Its
  requirements are *should*, not *shall* — the check is mandatory, this particular way of doing it is not.
  Note that both cross-references to this section (in `[V2G20-2545]` NOTE 6 and `[V2G20-2539]` NOTE 8)
  point at "8.3.4.1.1", which does not exist. A numbering slip in the draft; worth knowing before you go
  looking for it.
- **On a failed check the SECC starts a new session** with a fresh SessionID and
  `OK_NewSessionEstablished` (`[V2G20-2626]`, `[V2G20-2627]`). EVerest's silent fall-through to a new
  session is conformant, not sloppiness.
- **The EVCC has the mirror-image obligation** — verify it is still talking to the same SECC
  (`[V2G20-2539]`, a *shall*, methodology left to the OEM), and on failure **purge everything** associated
  with the paused session and terminate it (`[V2G20-2613]`, `[V2G20-2614]`). Likewise if the answer comes
  back `OK_NewSessionEstablished`: purge all data *including authorizations* and start over
  (`[V2G20-2615]`–`[V2G20-2617]`).
- **A resumed session opens at ChargeParameterDiscovery.** `[V2G20-1032]` names it as the allowed next
  request after the `SessionSetupRes`; `[V2G20-1843]` binds the EVCC to the sequence in `[V2G20-2097]`
  (AC), `[V2G20-2098]` (DC) and `[V2G20-5046]` (WPT), each of which requires the matching
  `*_ChargeParameterDiscoveryReq`. Authorization is not repeated because the previous session's
  authorization stays valid for the whole *service* session (`[V2G20-1844]`, `[V2G20-1847]`).
  Service discovery, detail and selection are not repeated either — `[V2G20-1032]` allows one next
  message, and it is not `ServiceDiscoveryReq`.

  A wart, since it will confuse the next reader: `[V2G20-2097]`/`[V2G20-2098]`/`[V2G20-5046]` are each
  worded as applying after a `SessionSetupRes` with ResponseCode `OK`, then qualified with "in case the
  EVCC resumes a previously paused session" — where the resume code is `OK_OldSessionJoined`. The
  qualifier and `[V2G20-1032]` leave no real ambiguity about which case is meant.

### ISO 15118-2 session resumption — deliberately different

Worth stating explicitly, because assuming otherwise is how our `-20` implementation went wrong.

- **No identity check.** `[V2G2-753]`/`[V2G2-754]` have the SECC compare the received SessionID against
  the stored one and nothing else. The binding obligation is **new in -20**.
- **The sequence is replayed, not skipped.** `[V2G2-740]` requires the EVCC to supply the same parameter
  values again in the resumed session, and `[V2G2-741]` requires the SECC to offer the previously selected
  payment option, the same `ChargeService`, and the previously selected `SAScheduleTuple` again — with its
  validity period reduced by the elapsed time. This is the opposite of `-20`'s jump to
  ChargeParameterDiscovery.
- **Two values must be adjusted on resume:** `DepartureTime` reduced by the elapsed time (`[V2G2-742]`)
  and `EAmount` reduced by the energy already delivered (`[V2G2-743]`).

### What a `FAILED_*` response does to the session — the two differ

Looked up on 2026-08-09 to settle whether our two stations should be made to behave alike. They should
not: each already follows its own document, and the asymmetry that looked accidental is the standards'.

- **`-20`: fatal, both sides terminate.** §8.6 *Message sequencing and error handling* states it in the
  ResponseCode description — a `FAILED`/`FAILED_*` value is a fatal error, and SECC and EVCC terminate
  the communication session after sending or receiving it. Worth noting for anyone going to check:
  **that sentence carries no requirement ID of its own**; the neighbouring `[V2G20-734]`/`[V2G20-735]`
  follow it and say something else. It is plain normative text in the type description.
- **`-2`: nothing of the kind.** §8.8.2 *Basic Definitions for Error Handling* has the parallel
  description **without** the fatal/terminate sentence, and its requirements stop well short of it:
  `[V2G2-734]` on OK the EVCC processes the other parameters, `[V2G2-735]` on `FAILED` the EVCC ignores
  them, `[V2G2-736]` the SECC fills the mandatory fields with schema-conformant values regardless.
  `[V2G2-457]`–`[V2G2-465]` say *when* each code is sent, not what happens to the session afterwards.
  Nothing obliges either side to end it.

So `Secc20Base` ending the session on any failure is conformant, and `Secc2` keeping the phase — so a car
that corrects its tuple choice still charges — is permitted. Unifying them would make one side worse: the
`-20` direction contradicts §8.6 outright, and the `-2` direction removes a permitted behaviour for no
requirement. Recorded in both state machines rather than tidied.

Carries the **`-2` caveat** above: the text to hand is the 2022 DIS revision and our `-2` stack targets
ISO 15118-2:2014. The risk here is lower than usual — this is basic error-handling wording rather than a
changed obligation — but an argument from *absence* in a revision is weaker than one from presence, and
this is an argument from absence.

### The ISO 15118-20 TLS profile

Our profile (`libs/EVSimulatorApp/docs/pki-model.md`: TLS 1.3, pinned suites, secp521r1) had never met a
counterparty that generates secp521r1 material, which left an open worry that we were simply stricter
than the field. The standard settles which side is deviating:

- **Cipher suites**, Table 6: `TLS_AES_256_GCM_SHA384` and `TLS_CHACHA20_POLY1305_SHA256` — both *shall*
  be supported by SECC (`[V2G20-2458]`) and EVCC (`[V2G20-2459]`), offered in table order
  (`[V2G20-1856]`, `[V2G20-1858]`). **`TLS_AES_128_GCM_SHA256` is absent** — TLS 1.3's own
  mandatory-to-implement suite is not in the -20 profile.
- **Named groups**, Table 7: `secp521r1` and `x448`, both *shall* (`[V2G20-1634]`, `[V2G20-1637]`).
  Neither `x25519` nor `secp256r1` appears.
- **Signature algorithms**, Table 8: `ecdsa_secp521r1_sha512` and `ed448`.
- **Certificate keys**: secp521r1 with ECDSA (`[V2G20-2674]`), and Ed448/EdDSA additionally
  (`[V2G20-2319]`), with a configurable mechanism to switch between them (`[V2G20-2320]`).

So the profile is the standard's, not ours. A counterparty whose `iso-20` certificate script emits
secp256r1 material is the one outside the profile — which reframes an open worry as a finding about
somebody else's test PKI. **Filed 2026-08-08:**
[`reports/josev-iso20-pki-curve.md`](reports/josev-iso20-pki-curve.md), and it is the clearest case this
section has produced of why the documents were worth having. The finding is not "we read Table 7
differently from you": their generator's `-20` branch carries its own
`# TODO Check correct version for ISO 15118-20` beside `EC_CURVE=prime256v1`, so the citation is not
what decides the point — it is what let the report say which value belongs there instead.

### Which TLS version may carry ISO 15118-20 — and which may not

Looked up on 2026-08-09 to settle a run from 2026-08-06 that the notes had closed as *"a layering
question"*: EVerest's `IsoMux` serves TLS 1.2 only and then routes on the `SupportedAppProtocolReq`, so
a dual-stack EV completed a whole `-20` session on TLS 1.2. It is decided, and the standard says it
three times.

- **The SECC must not select it.** `[V2G20-2356]` — a *shall not*: the station may not choose `-20` out
  of the `SupportedAppProtocolReq` when the connection carrying it is plain TCP, or TLS at 1.2 or below.
- **The EVCC must not offer it.** `[V2G20-1237]`, the mirror: over the same set of connections the car
  may not put `-20` into the offer at all.
- **And both at once, from the SDP direction.** `[V2G20-1805]`: where the TLS connection `7.7.3` calls
  for was not established, `-20` is neither offered by the EVCC nor chosen by the SECC.
- **Table 5** is what all three point at. It pairs TLS versions with the protocols they may carry, and
  `-20` appears in the 1.3 row only.
- **Serving TLS 1.2 is not itself a defect.** `[V2G20-2359]` permits it explicitly for backwards
  compatibility, and `[V2G20-2062]`–`[V2G20-2066]` describe the dual-version ClientHello a
  backward-compatible EVCC sends and how the SECC settles the version. What is forbidden is carrying
  `-20` on the result.
- **A conformant `-20` EVCC always offers 1.3.** `[V2G20-2365]` — include `0x0304` in
  `supported_versions` — with `[V2G20-1264]` requiring mutual TLS 1.3 of every `-20` entity.

**Filed 2026-08-09:**
[`reports/everest-isomux.md`](reports/everest-isomux.md) — and the
same lookup produced an item of **ours**, because the offer that reached their mux was our EVCC's and
`[V2G20-1237]` is the half addressed to us ([`open-work.md`](open-work.md)). Worth recording as a
pattern this section keeps producing: reading the requirement to decide whether a counterparty is wrong
is also the cheapest way to find out that we are.

### The SECC follows the EV's protocol ranking

A *shall*, and **both documents carry it under the same number in their own series**:

- **`[V2G2-169]`** and **`[V2G20-169]`** — from its own list of supported protocols, the SECC selects
  the one the EVCC ranked highest, and the response names that entry's SchemaID.
- **`[V2G2-167]`** and **`[V2G20-167]`** define the field: `1` highest, `20` lowest, at most 20 entries.

So the station's own capability is a filter and the EV's `Priority` is the ranking applied inside it.
A station that routes on *"does this EV mention `-20` at all"* and never reads `Priority` — which
EVerest's `IsoMux` does, confirmed on the wire three times across two releases — is not merely
surprising, it is on the wrong side of a requirement. The run notes had said *"whether that is a defect
depends on a requirement we have not checked"* since 2026-08-03; this is the check. **Filed 2026-08-09:**
[`reports/everest-isomux.md`](reports/everest-isomux.md).

Worth keeping for the method rather than the result. The `-2` half carries the **`-2` caveat** above,
and here it was worth doing something about rather than only declaring — three things, each
independent:

1. `[V2G20-169]` is in the `-20` FDIS, which needs no caveat and binds the same station.
2. The 2019 *ISO 15118 Manual*, written against ISO 15118-2:**2014**, describes the same rule in its
   walk-through of the handshake. The manual is never a citation for a conformance claim; **this** is
   what it is good for — deciding whether a requirement in the revision to hand predates it.
3. `-20`'s own worked example in `8.2.4` shows the SECC answering the SchemaID of the priority-1 entry
   where array order and priority order deliberately differ.

An argument that would have rested on one draft revision rests on three places instead, one of them
contemporaneous with the edition actually being implemented.

### OCSP stapling in the TLS handshake — required by both protocols

Chased on 2026-08-10, when a warning in EVerest's own boot log turned out to mean that no EVerest
station staples an OCSP response at all
([`2026-08-10-everest-ocsp-stapling`](interop-runs/2026-08-10-everest-ocsp-stapling/notes.md)).
The question the run had to settle was whether stapling is *required* or merely available. It is
required, in both protocols, and one of them makes the request mandatory too.

**ISO 15118-2**, with the `-2` caveat above — these identifiers are read from the 2022 DIS revision:

- **`[V2G2-871]`** — a SECC outside a private environment owes the EV its certificate and a chain up to
  a root, and — once the EV has asked for OCSP data — **one OCSP response per certificate it puts into
  the handshake**, in the IETF RFC 6961 form, i.e. the `status_request_v2` extension.
- **`[V2G2-872]`** — the carve-out: a SECC in a *private* environment shall not send one, and sends its
  private root instead.
- **`[V2G2-873]`** — what a conformant EVCC does when it asked and received none: for a chain linked to
  a **V2G root** it shall **close the connection**; for a single self-signed leaf that is already one
  of its own roots it ignores the omission.
- **`[V2G2-875]`** — the EVCC shall verify the chain *and the OCSP responses*, and abort the TLS setup
  if any of that verification fails.
- **`[V2G2-649]`**, **`[V2G2-876]`** — the SECC should refresh the cached response at least weekly, and
  a Sub-CA response's validity is bounded.

**ISO 15118-20**, which needs no caveat:

- **`[V2G20-2372]`** — the EVCC **shall** include the `status_request` extension in its `ClientHello`,
  with **`[V2G20-2373]`** a zero-length `responder_id_list`. So under `-20` the question is always
  asked; there is no configuration in which it is not.
- **`[V2G20-2388]`** — because the EV asked, a *public* SECC owes **one OCSP response per certificate**
  of the chain it presents in the `ServerHello`; **`[V2G20-2391]`** places the same duty on a *private*
  SECC that supports PnC.
- **`[V2G20-2389]`/`[V2G20-2390]`** and **`[V2G20-2392]`/`[V2G20-2393]`** — the OCSP signer chains
  follow the certificate profiles matching whichever curve set was negotiated.
- **`[V2G20-1021]`** — the validity of the response the SECC provides is limited to one week.
- **`[V2G20-2327]`**, **`[V2G20-2330]`**, **`[V2G20-2332]`**, **`[V2G20-2334]`** — the OCSP signer
  certificate profiles, per credential family.

The pair worth carrying away is `[V2G20-2372]` with `[V2G2-873]`: one makes the request unconditional,
the other makes silence a reason to hang up. A station that never staples is therefore not merely
missing an optional hardening step.

**Read further on 2026-08-10, and it cost part of that claim.** The sentence above used to end *"it is
unreachable over TLS for an EV that enforces either"*, and the `-20` half of that is wrong:

- **`[V2G20-2411]`** — where the public SECC did not staple, the EVCC **may** contact the OCSP responder
  listed in the certificate itself. A `may`, so a `-20` EV is *not* obliged to abandon the session the
  way `[V2G2-873]` obliges a `-2` EV to.
- **`[V2G20-1240]`** keeps the check itself mandatory — the EVCC **shall** verify revocation status via
  an OCSP response — and **`[V2G20-2409]`**, **`[V2G20-2413]`**–**`[V2G20-2415]`** say what a failed
  verification costs: the whole chain is treated as having failed validation.
- **`[V2G20-2398]`** is the exemption on the station side: a **private** SECC **not supporting PnC** may
  ignore `status_request` altogether. `[V2G20-2388]` (public) and `[V2G20-2391]` (private with PnC) are
  where the duty actually bites, so *which kind of station this is* has to be settled before a missing
  staple is called a defect.

So the honest `-20` severity is: the mandatory revocation check is pushed onto an EV that may have no
route to a responder — which is what stapling exists to avoid — rather than a handshake that fails.
`-2` keeps the stronger form. **Filed 2026-08-10:**
[`reports/everest-d20-ocsp-absent.md`](reports/everest-d20-ocsp-absent.md), for the `-20` module that
has no OCSP code at all, separately from
[`reports/everest-evse-security-ocsp-dropped.md`](reports/everest-evse-security-ocsp-dropped.md), whose
`-2` consequence is the hard one.

Worth keeping as method, and it is the second time this file has done it: reading one clause further
than the finding needed turned a confident sentence into a qualified one. The finding survived; the
claim about what it costs did not.

### The DC charge-loop limits — and who has to respect them

Chased on 2026-08-10 to settle a debt this file was carrying: an EVerest station lowered its stated
current limit from 200 A to 55.2 A mid-session while our `-2` EVCC went on asking for 120 A, and the
entry in [`open-work.md`](open-work.md) recorded the requirement side as *not cited yet*. It is cited
now, and it **changes the verdict** rather than confirming it.

**No requirement in either document obliges the EV to hold its target inside the station's stated
maximum.** The `-20` text says the opposite, in as many words as a standard uses:

- **`[V2G20-2188]`** — while trying to reach an `EVTargetVoltage` or `EVTargetCurrent` setpoint, **the
  SECC** shall not initiate any EVSE behaviour that would violate the communicated applicable limits.
  The duty is the station's.
- Its **NOTE 1** removes the remaining doubt: those two elements are to be read as *targets* and not as
  a replacement for upper limitation, and the limits live in other elements. A setpoint above the
  ceiling is a setpoint the station is required to serve *up to the ceiling*, not a violation by the car.
- **`[V2G20-2183]`–`[V2G20-2187]`** frame the same division: the EV names one of the two setpoints and
  keeps it logically consistent with what it reports; the SECC adjusts the output towards it as rapidly
  as it can.
- **`[V2G20-2654]`** — a station's DC loop limits shall be **less than or equal to** what it stated in
  the `DC_ChargeParameterDiscovery` pair (`[V2G20-2655]` is the minimum-value mirror). So an EVSE that
  drops from 200 A to 55.2 A mid-session is doing exactly what the standard provides for.

**ISO 15118-2 has no EV-side obligation either, and no `[V2G20-2188]` equivalent.** What it has is the
element semantics — `[V2G2-258]` and `[V2G2-260]` bind Tables 55 and 56 for `CurrentDemandReq`/`Res`,
`[V2G2-373]` binds Table 101 for `DC_EVSEChargeParameter` — where `EVTargetCurrent` is *the current the
EV requests* and `EVSEMaximumCurrentLimit` is *what the EVSE can deliver*. The physical side is
delegated to IEC 61851-23, which `-2` references for the EVSE's output and, in `[V2G2-644]`, for
overcurrent protection. The `-2` DC-specific requirements at 8.7.4.4 are about CP states and nothing
else. Carries the `-2` document caveat above.

**What this leaves standing, and what it retires.** It retires the description of our own EVCC as
having violated something: it had not. What stands is that our car ignored information a real one uses
— EVerest's `EvseManager` clamps such a request under a comment naming it a *broken EV implementation*
— and that the fix's **station** half is the one with a requirement behind it: a station that states a
ceiling and then serves past it is the case `[V2G20-2188]` forbids, and ours now clamps to what it
announces.

Worth keeping for the method. The citation was owed for one afternoon and paid the same day, and
paying it cost a claim rather than confirming one. That is the second time in three days that reading
the requirement text moved a finding — the other was `[V2G20-1618]`, withdrawn as unimplementable —
and both times the direction was *away* from a defect we had asserted.

### `trusted_ca_keys`, and the selection duty it carries

Read on 2026-08-10 to decide whether `IsoMux` booting with *"trusted_ca_keys support disabled"* is a
defect or a shrug. It is a defect, and the chain of `-2` requirements is short and unambiguous. Carries
the `-2` document caveat above.

- **`[V2G2-651]`** — the EVCC **shall** put a `trusted_ca_keys` extension (IETF RFC 6066) in its client
  hello, listing the V2G root certificates it holds. Unconditional: every conformant EV asks.
- **`[V2G2-871]`** — a station outside a private environment owes the EV a chain up to a root, and that
  root **shall be one the EV named**. This is the duty the extension exists to discharge.
- **`[V2G2-923]`** — the escape hatch, and it is narrow: only where the station *cannot* match may it
  present a chain to some other root.
- **`[V2G2-924]`** with **`[V2G2-875]`** — an EV handed a chain that does not trace to a root it trusts
  shall treat it as unvalidated unless it validates out of band, and abandon the TLS setup. So the
  station's failure to select is the EV's reason to leave.
- **`[V2G2-878]`** — up to ten concurrently valid V2G root certificates per root CA. The multi-root
  case the extension addresses is expressly provided for, not exotic.

The practical reading, which is what made the finding filable: with a single root a station that
ignores the extension is indistinguishable from one that honours it, because there is nothing to
choose. The requirement bites exactly where an operator holds two — mid-rotation, or serving two roots
— and that is the configuration nobody tests.

### `[V2G20-169]` has two halves, and they are usually implemented one at a time

Already cited on 2026-08-09 for the `IsoMux` routing finding; re-read on 2026-08-10 because a second
EVerest module failed the *other* half of it, and the pair is worth stating once.

- **`[V2G20-169]`** (and its `-2` twin `[V2G2-169]`) — the SECC selects, **from the protocols it
  supports itself**, the one the EVCC ranked highest, and names that entry's SchemaID.
  - **The filter**: *from the protocols it supports itself.* A station may not answer with a protocol
    it cannot serve.
  - **The ranking**: *the one the EVCC ranked highest.* Among what survives the filter, `Priority`
    decides.
- **`[V2G20-167]`** / **`[V2G2-167]`** — define the field: `1` highest, `20` lowest, at most 20 entries.

Two EVerest modules, each implementing exactly one half, found a day apart:

| Module | Filter | Ranking |
|---|---|---|
| `IsoMux` | applied (it does serve both `-2` and `-20`) — but the routing reads only *"is `-20` mentioned"* | **not read** |
| `Evse15118D20` | **not applied** — `-20:AC` and `-20:DC` go into one map whatever the station serves | applied, and its comment cites `[V2G20-167]` |

Worth keeping because of what it says about reading a requirement: *"select the highest-ranked one you
support"* is one sentence and two obligations, and an implementation can satisfy the half it noticed
while failing the half it did not. Both halves have to be checked separately, and in this case each was
found by a different kind of evidence — the ranking one by a byte-identical response across three runs,
the filter one by an offer no conformant station could accept.

The `-20` AC and DC namespaces are separate `ProtocolNamespace` values because they select different
**message sets**, so the answer is a commitment to a schema rather than a preference — which is why the
filter half is not cosmetic.

### Who has to ask for the vehicle certificate, and what else the `CertificateRequest` carries

Looked up on 2026-08-10, when an EVerest `-20` station turned out to demand a client certificate from an
EV that offered TLS 1.3 and to demand nothing from an EV that offered only TLS 1.2
([`…-d20-client-auth`](interop-runs/2026-08-10-everest-d20-client-auth/notes.md)). The question to settle
was whether mutual authentication in `-20` is a property of TLS 1.3 — in which case a TLS 1.2 connection
simply is not a `-20` connection and the station is merely lax — or a duty on the SECC in its own right.
It is the latter, and it is unconditional.

- **`[V2G20-2400]`** — the SECC **shall** request the EVCC's certificate via the `CertificateRequest`
  message. No TLS-version qualifier, no public/private split, no PnC condition. NOTE 23 beside it says
  what it buys: a session in which each side verifies the other.
- **`[V2G20-1264]`** — mutual authentication with TLS 1.3 shall be supported by every V2G entity. This
  is the *support* obligation; `[V2G20-2400]` is the *behaviour* one, and only the second is violated by
  a station that quietly skips the request.
- **`[V2G20-2356]`** stays the reason the `-20` answer on such a connection is separately wrong, with
  **`[V2G20-2359]`** the reason the TLS 1.2 *listener* is not: serving 1.2 for backwards compatibility is
  explicitly permitted, carrying `-20` on the result is not.

And the request has required contents, which is the part that is easy to miss:

- **`[V2G20-2401]`** (public SECC) / **`[V2G20-2402]`** (private, not in CPM4PE) — send the V2G and/or
  OEM root CA certificates the SECC holds, in the `certificate_authorities` extension.
  **`[V2G20-2403]`** fixes which RDNs each `DistinguishedName` carries and in what order.
  **`[V2G20-2404]`** is the only exemption and it is narrow: an *empty* authorities element, when the
  SECC holds no roots at all. **`[V2G20-2405]`** is the CPM4PE case, **`[V2G20-2406]`**/**`[V2G20-2407]`**
  say what must *not* be in it (`oid_filters`, `status_request`).
- **`[V2G20-1667]`** — the SECC shall include the signature algorithms in **Table 8**'s order, and
  **`[V2G20-1668]`** makes Table 8 its preference when choosing among the EV's.
  **`[V2G20-2460]`** is the same rule for named groups against **Table 7**, and **`[V2G20-2461]`** ties
  the two together. Both sit beside **`[V2G20-2458]`**/**`[V2G20-1856]`**, the Table 6 cipher-suite pair
  already recorded above under *The ISO 15118-20 TLS profile*.

### Who asks for the meter reading, and who owes it

Looked up on 2026-08-10, when it turned out our own `-20` EVCC had hardcoded the field that asks. The
mechanism is small and the consequences are not: it is the entry point to the whole `-20` metering path.

- **`[V2G20-1081]`** — if the EVCC wants the `MeterInfo` element, it sets `MeterInfoRequested` to TRUE
  in `ChargeLoopReq`. The EV's only mechanism, in either message set.
- **`[V2G20-1082]`** — *if `[V2G20-1081]` applies*, the SECC **shall** respond with the `ChargeLoopRes`
  including `MeterInfo`. Conditional on the ask; unconditional once asked.
- **`[V2G20-1833]`** — and without any ask: an EVSE *equipped with metering technology* and supporting
  the capability shall provide initial `MeterInfo` in the **very first** charge-loop response. Note the
  two antecedents — hardware and capability — either of which a station may honestly not meet.
- **`[V2G20-902]`** — what the element means: energy charged during the current service session.
- **`[V2G20-1083]`** — the SECC's own use for it: to trigger a `MeteringConfirmationReq/Res` it must
  include `MeterInfo` and set `EVSENotification` to `MeteringConfirmation`.
- **`[V2G20-1919]`** — a receipt based on kWh measurements carries the associated `MeterInfo`.

**The chain is what matters.** No `MeterInfo` means no `MeteringConfirmation` to trigger and no receipt
to base on kWh, so a station that never sets the element has not partially implemented `-20` metering —
it has made the path unreachable. That is the difference between an omitted field and a missing
capability, and it is worth checking for whenever a response element looks merely optional.

**Filed 2026-08-10:** [`reports/everest-d20-meter-info.md`](reports/everest-d20-meter-info.md) — and it
is the clearest case yet of the pattern this file keeps producing in the other direction. Reading
`[V2G20-1081]` to judge a counterparty is what showed that **our own EVCC could not perform it**: both
`-20` charge loops passed the literal `false`, so no run of ours had ever asked anyone, and no
counterparty's `[V2G20-1082]` had ever been tested. The car half went in the same day
([`open-work.md`](open-work.md)); the station was measured with it an hour later.

### Which credential the EV puts in the TLS handshake, and which root certifies it

Looked up on 2026-08-10, when an EVerest `-20` station refused a **vehicle** certificate from its own
test PKI and accepted a **contract** certificate from the same tree, logging the second as
*"Vehicle Cert is available"*
([`…-d20-trust-anchor`](interop-runs/2026-08-10-everest-d20-trust-anchor/notes.md)). The question was
whether the two are interchangeable at the TLS layer. They are not, and the document separates them in
its own overview before any requirement does.

- **`[V2G20-2339]`** — the EVCC shall hold a **vehicle certificate**, and it is what establishes the TLS
  session. **`[V2G20-2260]`** says the same thing for the 802.1X/RADIUS path.
- **`[V2G20-2331]`** — a vehicle certificate's chain is anchored at an **OEM root CA**, with a **V2G
  root CA** as the permitted alternative. Those two, and no others.
- **`[V2G20-2401]`/`[V2G20-2402]`** — accordingly, the anchors an SECC advertises in
  `certificate_authorities` are its **V2G root and/or OEM root** certificates. **MO appears in
  neither.**
- **Clause 7.3.1** draws the whole map in one paragraph: SECC certificates authenticate the *station* at
  the TLS layer; **contract** certificates authenticate at the **application** layer; V2G roots certify
  SECC and contract certificates; and **OEM roots with vehicle certificates are the pair the SECC uses
  at the TLS layer to authenticate the EVCC**. OEM *provisioning* certificates are named separately
  again, for installing and updating contract certificates.
- **`[V2G20-2677]`** is why a wrong anchor propagates: the pause/resume binding is computed over the
  **vehicle** certificate from the handshake, so whatever the station accepted there becomes the
  identity a resumed session is bound to.

The distinction to carry away: **a contract certificate says *who pays*, a vehicle certificate says
*which car*.** A station that accepts the first where the second belongs is not merely permissive — it
is answering a different question from the one the handshake asks. **Filed 2026-08-10:**
[`reports/everest-d20-trust-anchor.md`](reports/everest-d20-trust-anchor.md).

And a note on where such a defect can live: this one is not fixable in the `-20` stack alone.
everest-core's `CaCertificateType` has `V2G`, `MO`, `CSMS`, `MF` and no `OEM`, so the correct anchor
cannot be *requested*. When a requirement names a credential class an implementation's type system does
not have, the nearest available value is what gets used — and it will look deliberate in the code.

### Random numbers: one *shall* about the generator, and two numbers about the values

Read on 2026-08-11, when EVerest's `-20` library turned out to draw both its SessionID and its Plug
& Charge challenge from a Mersenne Twister seeded with 32 bits
([`…-d20-rng-entropy`](interop-runs/2026-08-11-everest-d20-rng-entropy/notes.md)). The question to
settle was whether `-20` says anything about *how* a random value is produced, or only about how wide
the field is. It says a great deal, in a *Random number generation* subclause of its own under §7.3,
and it is the most quantitative requirement text this file has had to record.

- **`[V2G20-835]`** — a *shall*, and unconditional: wherever this document requires a random number, a
  **state-of-the-art cryptographically secure** RNG is to be used. Its NOTE declines to prescribe one
  (the choice does not affect interoperability) and names NIST SP 800-90 A and BSI AIS 20/31 as
  examples of established standards.
- **`[V2G20-2607]`** — the value used to re-seed or initialise a DRBG shall itself carry the minimum
  entropy the particular function requires, and both the entropy input and the seed shall be kept
  secret.
- **`[V2G20-2608]`** — a value generated under `[V2G20-835]` shall be kept secret and used **only
  once**. This is the clause a birthday collision violates, and it is what makes "how many values can
  this generator produce" a conformance question rather than a hygiene one.
- The surrounding non-normative text spells out the failure mode explicitly: a DRBG's output is only
  as good as its seed, not every DRBG is cryptographically secure, and where a function states no
  minimum entropy of its own, **116 bits** should be assumed for any random number it uses.

And the two values this project has had reason to look at:

| value | width | minimum entropy | where |
|---|---|---|---|
| `SessionID` | 64 bits | **58 bits** | `[V2G20-2621]`, beside `[V2G20-2106]` (generate a new one, non-zero) |
| PnC `GenChallenge` | 128 bits — `[V2G20-697]`, `[V2G20-2110]` | **120 bits** | `[V2G20-698]`, with `[V2G20-2108]` pointing at 7.3.7 |

**Why the pair is worth carrying together.** The width is in the schema and any codec gets it right;
the entropy is in a requirement and nothing on the wire reveals it. Two of the four `-20` stacks
audited that day put the right number of bytes in the right field and far too few bits inside them —
EVerest's `-20` library at ≤ 32 bits from a non-cryptographic generator, eVDriveFlow's station at
26,6 bits from a correct one used over a 10⁸ range. **A conformance suite that only reads messages
cannot see either.** That is the general lesson: an entropy requirement is invisible to every oracle
that works from bytes, and is only reachable by reading the generator or by collecting enough values
to count collisions.

`[V2G20-2545]` — the resume binding already recorded above — is the reason a weak SessionID is not
automatically a hijack: a `-20` station that verifies the resume against the vehicle certificate does
not rely on the identifier being unguessable. It is also the reason the requirement exists, since a
station that skips that check does.

Filed 2026-08-11: [`reports/everest-d20-rng-entropy.md`](reports/everest-d20-rng-entropy.md) and
[`reports/evdriveflow-session-id-entropy.md`](reports/evdriveflow-session-id-entropy.md).

**ISO 15118-2 carries most of this already, under the same numbers** — which the first draft of this
section got wrong, and the correction is worth keeping because it changes what may be said to whom.
Checked in the 2022 DIS revision to hand, §7.9.2.4.4:

- **`[V2G2-835]`** — the *same* clause, same identifier in the `-2` series, same unconditional *shall*
  on a state-of-the-art cryptographically secure RNG, with the same NIST SP 800-90 A / BSI AIS 20/31
  NOTE. Like `[V2G2-169]`/`[V2G20-169]`, this is one rule wearing one number in two documents.
- **`[V2G2-697]`/`[V2G2-698]`** — `GenChallenge` exactly 128 bits, entropy at least 120 bits. Again
  the same identifiers as the `-20` pair, and **`[V2G2-825]`/`[V2G2-826]`** state it a second time for
  the `PaymentDetailsRes` challenge.

What `-20` **adds** is the rest of the table: `[V2G20-2621]`'s 58-bit floor for the SessionID — `-2`
has only `[V2G2-750]`, generate a new one different from zero, with no entropy requirement at all —
together with `[V2G20-2607]`, `[V2G20-2608]` and the 116-bit default for anything unspecified.

So a `-2` or DIN module **is** bound by the generator rule and by the challenge's 120 bits, and is
**not** bound by any SessionID entropy minimum. Worth having straight before pointing an entropy
finding at one. Carries the `-2` document caveat above, though the risk is low here: these
identifiers sit in the 600–800 range that predates the revision, and the `-20` document carries
`[V2G20-835]` and `[V2G20-698]` verbatim.

### The ISO 15118-20 TLS profile is four lists, not one

Suites (Table 6), named groups (Table 7), signature algorithms (Table 8), and the trust anchors named in
the `CertificateRequest` (`[V2G20-2401]`). The station measured on 2026-08-10 implements the first
exactly and leaves the other three at its TLS library's defaults — which is worth recording as a
**shape** rather than a one-off. A profile expressed as several tables in several subclauses gets
implemented at the table someone read; the tables are not adjacent in the document either. When judging
an implementation against Table 6, check for the other three before concluding the profile is applied.

All `-20` identifiers, so no document caveat. **Filed 2026-08-10:**
[`reports/everest-d20-client-auth.md`](reports/everest-d20-client-auth.md), as two issues.
