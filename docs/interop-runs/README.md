# Interop run artifacts

One directory per successful (or informative) interop run, named
`<yyyy-mm-dd>-<scenario>/` (e.g. `2026-08-01-iso2-ac-eim-notls/`; prefix the counterparty when it is
not Josev, e.g. `2026-08-01-tux-iso2-dc-notls/`, `2026-08-01-edf-iso20-dc-dynamic/`). See
[`tools/interop-josev/`](../../tools/interop-josev/README.md),
[`tools/interop-tux-evse/`](../../tools/interop-tux-evse/README.md),
[`tools/interop-evdriveflow/`](../../tools/interop-evdriveflow/README.md) and
[`tools/interop-everest/`](../../tools/interop-everest/README.md) for how to produce them.

Each directory should contain:

- `notes.md` — the counterparty's commit or image tag, our commit, the exact commands, the outcome,
  and any wire discrepancies found.
- `frames.log` — the full frame log for the session: per frame, hex + decoded message + timestamp
  + direction (EVCC→SECC / SECC→EVCC).

An **offline oracle run** — no session, no wire, just our recorded bytes put through somebody else's
codec — belongs here too and has no `frames.log`; it carries its verdict table instead. See
[`tools/interop-v2gdecoder/`](../../tools/interop-v2gdecoder/README.md) and the run it produced,
[`2026-08-07-v2gdecoder-oracle/`](2026-08-07-v2gdecoder-oracle/notes.md).

So does a **static sweep** — no session and no bytes either, just a counterparty's source held against
a rule. It carries the tool's output as its artifacts, and its `notes.md` says what the sweep could
*not* decide, since that is the whole risk with reading code instead of traffic. See
[`2026-08-11-libcbv2g-grammar-sweep/`](2026-08-11-libcbv2g-grammar-sweep/notes.md), and
[`2026-08-11-edf-pnc-source-audit/`](2026-08-11-edf-pnc-source-audit/notes.md) for the variant that
corroborates a source reading against frame logs **already** in this directory — the cheapest run there
is, and an argument for keeping the bytes rather than only the verdicts.

[`2026-08-11-everest-d20-rng-entropy/`](2026-08-11-everest-d20-rng-entropy/notes.md) is the strongest
form of that variant so far, and worth knowing about before you delete an old log: it measures a
counterparty's *random number generator* by recovering the 32-bit seed behind **49 SessionIDs their
station issued across twenty earlier runs**, none of which was recording them for that reason. The
artifacts kept for one question answered a different one nine days later —
[`2026-08-11-everest-iso2-metering-receipt/`](2026-08-11-everest-iso2-metering-receipt/notes.md) did it
again the same afternoon, out of a `frames.log` from 2026-08-02, and the reason it needed no rig is
that the finding is about a field that is **absent**: no live run can show you an omission you were not
already looking for, but a kept frame can be re-decoded by name.

A fifth shape is a run against a counterparty's **`main`, not its release tag**, to settle one claim.
[`2026-08-12-everest-main-ocsp-warning/`](2026-08-12-everest-main-ocsp-warning/notes.md) is the first:
it builds everest-core `main` in a **git worktree with its own install prefix** — the `2026.02.1` tree
every other EVerest measurement here rests on stays untouched, which is worth more than the twenty
minutes an in-place checkout would have saved — and answers one checklist item, *does the warning we
predicted actually appear*. It does, 2 of 2. Worth reading for the two corrections it forced **before**
the station was started: the claim said "at every start" and the code builds its TLS server per SDP
request, and even then a bare SDP request is refused until a car is plugged in. Tracing the call graph
found both; the run only confirmed the corrected version.

[`2026-08-12-everest-main-chain-selection/`](2026-08-12-everest-main-chain-selection/notes.md) is the
second, and it is the shape to copy when a finding is about *selection*: install two roots, give the
station a valid chain under each, and vary only what the client asks for. The EV asks for root A and
gets root B — with a no-extension control that gets the same chain, so the request demonstrably has no
influence. Its longest section is a **rig error**: the previous station was still running because the
kill pattern did not match a native build's argv, and the *check* used the same wrong pattern and
agreed. Two managers on one MQTT prefix crash-shut-down in a way that reads exactly like a defect in
whatever you changed last. An unexplained crash in a rig you just changed is your rig until proven
otherwise.

[`2026-08-16-everest-isomux-section4/`](2026-08-16-everest-isomux-section4/notes.md) is the same shape
against a different module, and it adds the piece the two above argue from configuration instead of
bytes: a **packet capture**, so *"our car asked for root B"* is the hex of the ClientHello extension and
*"they answered with the other chain"* is three certificates lifted off the wire and run through
`openssl verify` against each root. The first capture in this directory, and it took two attempts —
traffic to the host's own link-local address goes through `lo`, not through the interface that owns the
address, so `tcpdump -i eth0` recorded **0 packets** while the session completed. Use `-i any` with a
port filter.

A fourth shape has no counterparty artifact at all: an **audit of our own drafts** against the trees
they cite. [`2026-08-11-reports-upstream-audit/`](2026-08-11-reports-upstream-audit/notes.md) re-reads
every `file:line` in `docs/reports/` — 189 of them, all still correct — and then asks the question the
per-report checklists ask one at a time: *has anybody fixed it since we wrote it down?* Doing it across
all thirty-two at once — the file count **that day**; thirty-seven as of 2026-08-16 — is what made the
answer visible: **three findings are already fixed on
everest-core `main`**, two of which are therefore retired without ever being sent. It also records a
wrong turn taken on the way — the stale `EVerest/libiso15118` mirror still shows all three, and the
audit briefly concluded they should be filed there. That does not move a matrix cell — no session was
run — but it settles four filings, and it is cheaper than any run in this directory.

A third shape is the **control pair**: two sessions differing in exactly one input, run so that a single
observation can be attributed. [`2026-08-11-everest-iso2-cert-install/`](2026-08-11-everest-iso2-cert-install/notes.md)
is the clearest example here — the same provisioning request, answered `FAILED_SequenceError` in an EIM
session and plain `FAILED` in a Contract one, which is what turns a confusing response code into a
statement about the counterparty's state machine. The single session would have been written up as a
defect; the pair says which of two explanations is the live one, and leaves the requirement question
open on purpose. It also carries the counterparty's **own MQTT payload** as an artifact, so the claim
that a station forwarded our bytes unaltered is a hash comparison rather than a description.

The control pair has a variant where **the variable is *when*, not what**, and it is worth knowing
because a wall that survives six attempts starts to look like a property of the counterparty.
[`2026-08-13-everest-d20-ac-contactor-window/`](2026-08-13-everest-d20-ac-contactor-window/notes.md) is
seven `-20` AC sessions in eleven minutes against one station, one binary and one config, differing only
in the moment the simulated car raises its Control-Pilot line. Raised early: `FAILED_ContactorError` at
3,047 s, as on every attempt since 2026-08-03. Raised inside the counterparty's own three-second window:
`OK`, five times, through the charge loop to `SessionStop`. **Nothing was injected and nothing patched
in any of the seven** — the station reached its own conclusion each time.

The same variable bites from the other side when it is *not* the one under test.
[`2026-08-15-edf-session-id-460/`](2026-08-15-edf-session-id-460/notes.md) took its control first, and
that control stopped four messages earlier than the two probe arms — because eVDriveFlow's station fails
its virtual isolation test in the first session after a start, not because of anything the arms varied.
Read as it stood it says *"the wrong SessionID got further than the right one"*. **Against a stateful
peer, ordering is a variable until it is shown not to be**, and the cheap way to show it is to run the
control again at the end; the second one is identical to the probes, message for message.

A fourth shape is the arm that **checks the instrument rather than the peer**, and it is the one most
easily skipped because it is expected to be a formality.
[`2026-08-14-everest-iso2-ac-tls12/`](2026-08-14-everest-iso2-ac-tls12/notes.md) ran a `-2` AC session
over TLS with the trust anchor narrowed from *root + both Sub-CAs* to **the root alone** — which should
have made no difference, because `openssl s_client -showcerts` had just shown their station sending the
whole path. It failed, and what it had found was ours: the interop fixture's TLS callback discarded the
chain the peer sent, so every counterparty since the fixtures existed had been judged on its bare leaf.
**The wider bundle passes either way**, which is precisely why nothing had ever seen it.

The general form is worth stating, because this repository's usual instinct runs the other way: when a
run's setup contains a value that is *more generous than necessary* — a trust bundle with extra
certificates, a timeout longer than the standard's, a catalogue offering more than the peer needs — the
run cannot distinguish a peer that needed the generosity from one that did not. **Narrow it once, on
purpose.** The result is either a stronger claim about the counterparty or a finding about yourself.

There is a sharper version of that arm, and
[`2026-08-15-edf-session-id-460/`](2026-08-15-edf-session-id-460/notes.md) is the case for it: **when the
expected finding is *"the peer ignores this input"*, a probe that never sent the input produces exactly
the same session as the finding**. A complete, successful, textbook charge is the evidence *and* the
symptom of a broken instrument, and no amount of reading the peer's log tells them apart. The only way
out is to read the input back off the wire — there, the wrong SessionID recovered from our own recorded
request frames with a one-bit shift and no decoder, eleven of eleven — and to keep a peer that *does*
implement the rule as the reference arm, which refuses the identical bytes. Two fixtures in this suite
were passing four of eleven parameters when that run started; without the byte-level read-back, the
finding and the bug would have been written up the same way.

A fifth shape is the **re-take**: the same session against the same peer, run again because the harness
can now measure something it could not before. Two ran on 2026-08-15 and they had different causes worth
telling apart. The `-20` EVerest cell
([`…-d20-reverse-pnc-chain`](2026-08-15-everest-d20-reverse-pnc-chain/notes.md)) understated itself
because a value our own side held could not be printed — the sixth such instance in three days. Josev's
two cells ([`…-josev-reverse-pnc-chain`](2026-08-15-josev-reverse-pnc-chain/notes.md)) understated
themselves because they were **six weeks older than the flag that would have measured it**: nothing was
unreachable, the runs simply predated the capability and no one went back.

The second cause is the quieter one, and it has no code to fix. **A capability the harness gains does not
reach back through the matrix.** Every cell recorded before it keeps the wording it was written with,
and that wording now claims less — or, as here, reads as more — than a run today would support. The guard
is procedural rather than technical: **when a knob lands, the cells older than it are the ones to
re-take**, and the cheapest ones first. Both re-takes above cost one variable and one control apiece.

One more rule came out of those two notes, from an error in both. Each ended by naming the *next* cell to
re-take, and each named one at a counterparty that has no such cell — a stack audited four days earlier
and recorded, in the matrix and in `open-work.md`, as implementing no Plug & Charge at all. Nothing was
stale; the sentence was wrong when written, because three cells reads better than two and *"the last one
left"* is a satisfying way to end. **A run note's *Next* is a claim about the matrix, so read the matrix
before writing it** — including the cells that hold `—`, which are the ones nobody re-reads.

Two things generalise from the contactor-window run. The first is that the **positive control is the
load-bearing half** here:
five successes are what turn "it fails" into "it fails for this reason", and without them the run would
have been another failed attempt. The second is the reason it took four months: the wall had a *name* —
an explanation this repository stated confidently in four places and had already contradicted in a log
file of its own from 2026-08-09. **A named wall stops being measured.** When a cell has been blocked
long enough to have a story attached, re-read the artifacts before believing the story.

## Producing them

The `[Explicit] [Category("Interop")]` fixtures write these artifacts themselves when
`V2G_INTEROP_RECORD=<dir>` is set — see
[`ISO15118ConformanceTests.Simulation/Interop/InteropRecording.cs`](../../ISO15118ConformanceTests.Simulation/Interop/InteropRecording.cs).
Per run: the raw octets of each direction, a `frames.log` (named messages and response codes, not just
payload types), a `flow.md`, and a `*.trace.json` in the same format as `Vectors/Session.*.trace.json`
when the session was strictly alternating and untruncated. When it was not, a `trace-not-built.txt`
says why and the bytes are there anyway — the failed run is the interesting one, and it is precisely
the one a strict corpus builder refuses.

`flow.md` is the one to paste into `notes.md`: the paired message sequence, every response code that
was not OK, and — when `V2G_INTEROP_SCENARIO` points at a reference — how the order compares with it.
That comparison is the part of interop a vector corpus can never do, and it is where §1.3's
conformance fixes live.

The reference can be either kind of file, told apart by structure:

- **a counterparty's scenario** (tux-evse), which is a real session captured and replayed — so the
  expected column is another car's route;
- **one of our own `Vectors/Session.*.trace.json`**, for a counterparty that publishes no such file
  (eVDriveFlow and EVerest are stacks, not replayers). Then the comparison answers "did the live run
  take the same route as ours" — not a conformance claim, and against a Dynamic-mode -20 peer it has
  every reason to say no. The divergence is the result.

Both directions are compared when the reference declares both. For a counterparty that is a
**station** — EVerest's `EvseV2G` is the likeliest thing to be on a real charger — the *station → EV*
section is the one that carries the news: what our car sends is ours and already pinned by the corpus,
while what their charger answers is the thing no test here has ever seen.

A recorded `trace.json` can be adopted as a corpus entry, which is how a conformance fix earned in a
one-off run becomes something all four back ends are held to. That is a deliberate step, not
automatic: read it first (it carries a foreign station's identifiers and schedules).

## Record mode → vectors (the valuable part)

Frames captured from Josev are **independently generated** conformance vectors — Josev shares none of
our lineage (it uses EXIficient, not cbV2G). The adoption path into the regular suite:

1. Isolate a single message's EXI bytes from `frames.log`.
2. Decode it with our codec (`DecodeAny`) and confirm the decoded content is what the scenario expects.
3. If it round-trips (decode → re-encode) byte-for-byte, add it as a checked-in vector under
   `WWCP_ISO15118_EXI_Tests/Vectors/` with `source: "josev@<sha>"`, so it guards against regressions
   from a second, independent oracle.
4. If it does *not* round-trip, that is a real interop finding — analyse the byte diff, fix the root
   cause (codec or generator), and add the captured bytes as a regression vector.

Nothing is checked in here until there is a real run to record.
