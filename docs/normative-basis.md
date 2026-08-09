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
[`reports/everest-isomux-iso20-over-tls12.md`](reports/everest-isomux-iso20-over-tls12.md) — and the
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
[`reports/everest-isomux-sap-priority.md`](reports/everest-isomux-sap-priority.md).

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
