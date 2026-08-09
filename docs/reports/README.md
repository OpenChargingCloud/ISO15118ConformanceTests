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
| [`evdriveflow-headless-session.md`](evdriveflow-headless-session.md) | EDF Lab (eVDriveFlow) | **1**, **2**, plus three secondary | The documented no-GUI path cannot complete a session, for two independent reasons: EOF on stdin reads as "Enter pressed", and `hasattr` on an xsdata `Optional` field nulls the EV's own target SOC |
| [`tux-evse-tls.md`](tux-evse-tls.md) | IoT.bzh (tux-evse) | **A**, **B** | Over TLS the EVCC signs every `AuthorizationReq` (so no shipped scenario runs over TLS at all), and the pinned cipher profile contains neither suite ISO 15118-2 prescribes |
| [`tux-evse-spin.md`](tux-evse-spin.md) | IoT.bzh (tux-evse) | **C**, **D** | One connection that pauses or closes sends the binder into a 200,000-line-per-second log loop — and SIGTERM stops the logging without ending the process |
| [`tux-evse-capture-fidelity.md`](tux-evse-capture-fidelity.md) | IoT.bzh (tux-evse) | **E**, **F** | A replayed capture never puts the car's real protocol offer on the wire — their converter parses it and drops it — and the closing SDP verb is hardcoded to the wrong API in DIN scenarios |
| [`everest-loop-shutdown.md`](everest-loop-shutdown.md) | EVerest | one | A failed TLS handshake ends `Evse15118D20`'s V2G accept loop, so one bad handshake takes the station down for the rest of its life — while the process stays healthy and nothing supervising it notices |
| [`everest-iso20-ac-contactor-latch.md`](everest-iso20-ac-contactor-latch.md) | EVerest (libiso15118) | one | The `-20` AC `PowerDelivery` state assigns a **pointer** to its `bool ac_connector_closed`, so a board-support module reporting the contactor **open** latches it closed, cancels the timeout that would have refused, and answers `PowerDeliveryRes(OK)` — the mechanism by [`tools/everest-contactor-probe/`](../../tools/everest-contactor-probe/README.md), the behaviour [against their running station](../interop-runs/2026-08-09-everest-ac-contactor-injection/notes.md), 2 of 2 with a control |
| [`everest-isomux-iso20-over-tls12.md`](everest-isomux-iso20-over-tls12.md) | EVerest (`IsoMux`) | one | The multiplexer serves TLS 1.2 and nothing higher, then routes the SAP offer to whichever backend it names — so a dual-stack EV gets a complete **ISO 15118-20 session over TLS 1.2**, which `[V2G20-2356]` forbids the station to select, while a `-20` EV that pins its own profile gets alert 70. Between the two, the `-20` backend is reachable **only** by an EV that breaks the mirror requirement — and ours did, which the report says before it says anything else |
| [`josev-iso20-pki-curve.md`](josev-iso20-pki-curve.md) | EVerest (`ext-switchev-iso15118`) **and** SwitchEV (iso15118) | one, filed twice | `create_certs.sh` branches on `-v iso-2\|iso-20` and the `-20` branch selects the same `prime256v1` as `-2`, under its own `TODO` — so `-20` contract provisioning cannot complete at all, the schema's key-wrap curve choice being secp521r1 or x448 and nothing else |
| [`josev-iso20-pause-resume.md`](josev-iso20-pause-resume.md) | SwitchEV (iso15118) | one | Pause/resume works in ISO 15118-2 and cannot work in -20: the `-20` `SessionSetup` compares the resumed session ID against the *live* connection instead of the preserved context, which its states never fill — so `OK_OldSessionJoined` is unreachable and every resume degrades to a new session |
| [`pyevjosev-manifest-services.md`](pyevjosev-manifest-services.md) | EVerest | one | `PyEvJosev`'s manifest under-documents `supported_d20_energy_services`, so a valid MCS configuration looks impossible — and an unrecognised entry is dropped in silence |
| [`v2gdecoder-fuzzy-grammar.md`](v2gdecoder-fuzzy-grammar.md) | FlUxIuS (V2Gdecoder) | **A**, **B** | A frame valid under two grammars is decoded by whichever sits first in the array, silently — and their DIN grammar rejects a real `ChargeParameterDiscoveryRes`, which the same fallback then answers for |
| [`libcbv2g-grammar-deviations.md`](libcbv2g-grammar-deviations.md) + [`libcbv2g/`](libcbv2g/) | EVerest (libcbv2g / cbexigen) | **A**, **B**, **C** | The document grammar groups global elements sharing a type, so two ACDP messages swap identity and one decodes cleanly as the other; the WPT mid-sequence particle grammar returns success while **silently dropping** a set field; and every `minOccurs="2"` repeating particle gets a loop state with no exit, so three WPT types cannot be encoded at all — reproduced by [`tools/cbv2g-defect-probe/`](../../tools/cbv2g-defect-probe/README.md) |

**Nineteen filings across six projects**, and two of them now have to be sent **twice**.
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

The letters and numbers are per counterparty and exist to
keep separate filings separate: IoT.bzh's A and B are the TLS pair, C and D the loop and the signal
handler, E and F what a converted capture loses — six issues, not one, and a fix for any of them does
not touch the others. EDF's 1 and 2 are likewise independent, and fixing 1 is what reveals 2.
V2Gdecoder's A and B are the same shape again: independent, and the first is what makes the second
expensive to find. libcbv2g's are three different grammars in the same generator, and its C is the one
finding in this directory that is not a difference of opinion at all — three types that no caller can
encode.

The libcbv2g report is the only one where **we changed our own stack because of what we found**: this
project reproduced both of those grammars deliberately, for byte-compatibility with the reference
encoder, and stopped on 2026-08-08. The report says so, which is why its A is written as a question
rather than a verdict — if there is a rationale for the grouping we cannot see, we would change back.

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
