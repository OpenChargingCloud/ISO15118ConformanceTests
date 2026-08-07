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
| [`everest-loop-shutdown.md`](everest-loop-shutdown.md) | EVerest | one | A failed TLS handshake ends `Evse15118D20`'s V2G accept loop, so one bad handshake takes the station down for the rest of its life — while the process stays healthy and nothing supervising it notices |
| [`pyevjosev-manifest-services.md`](pyevjosev-manifest-services.md) | EVerest | one | `PyEvJosev`'s manifest under-documents `supported_d20_energy_services`, so a valid MCS configuration looks impossible — and an unrecognised entry is dropped in silence |

**Eight filings across three projects.** The letters and numbers are per counterparty and exist to keep
separate filings separate: IoT.bzh's A and B are the TLS pair, C and D the loop and the signal handler —
four issues, not one, and a fix for any of them does not touch the others. EDF's 1 and 2 are likewise
independent, and fixing 1 is what reveals 2.

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
