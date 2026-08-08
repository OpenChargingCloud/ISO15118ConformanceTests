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
