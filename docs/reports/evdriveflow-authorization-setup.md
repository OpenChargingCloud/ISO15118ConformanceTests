# Draft report to EDF Lab — a station offering PnC *and* EIM ends their EV's session

Status: **draft, not sent.** Observed on the wire 2026-08-01 against `eVDriveFlow` at `60249c3`, in
the reverse direction (their EV, our SECC), and **re-observed on 2026-08-10 with their `stdin` bug out
of the way** — which is the run that makes this report stand on its own. Source re-read at the same
commit. Post it under your own name; see *Before sending* at the bottom.

Evidence in this repository:
[`2026-08-10-edf-pnc-eim-stdin-open`](../interop-runs/2026-08-10-edf-pnc-eim-stdin-open/notes.md) —
the isolating run, with their traceback and the three-way contrast; and
[`2026-08-01-edf-iso20-dc-dynamic-reverse`](../interop-runs/2026-08-01-edf-iso20-dc-dynamic-reverse/notes.md)
— finding 4, with [`finding4-workaround.py`](../interop-runs/2026-08-01-edf-iso20-dc-dynamic-reverse/finding4-workaround.py),
applied **inside a throwaway container**, to their copy, never to ours.

Two other reports for the same project are in
[`evdriveflow-headless-session.md`](evdriveflow-headless-session.md) (issues 1 and 2) and
[`evdriveflow-service-discovery-filter.md`](evdriveflow-service-discovery-filter.md) (issue 3).
**File them separately** — and issue 1 matters for reading this one, because it is what stood behind
this defect and hid how far it reaches.

---

**Title:** `evcc/states/wait_for_authorization_setup_response.py`: an `AuthorizationSetupRes` offering
any service the EV does not implement raises `NotImplementedError`, so a station offering PnC alongside
EIM — which `[V2G20-2566]` explicitly permits — ends the session

**Version:** `eVDriveFlow` `60249c3` (2023-04-17, `main`), Python EVCC, ISO 15118-20 DC.

## The defect

```python
# evcc/states/wait_for_authorization_setup_response.py:30-37
for _ in payload.authorization_services:
    if _ in self.controller.data_model.authorization_services:
        request.selected_authorization_service = AuthorizationType.EIM
        ...
    else:
        raise NotImplementedError
        # TODO other cases
```

Their EV declares `authorization_services = [AuthorizationType.EIM]`
(`evcc/ev_dummy_controller.py:111`). The loop has **no `break`**, so it visits every entry the station
offered and raises on the first one that is not EIM — **wherever it sits in the list**. `[PnC, EIM]`
raises on the first pass; `[EIM, PnC]` builds the request on the first pass and raises on the second.
Our station offered `PnC` then `EIM`, and the session ended there.

`[V2G20-2566]` is explicit that the offer is legal: the SECC indicates the service or services it
offers, and it may offer EIM, or PnC, or **both**. A charge point that supports Plug & Charge alongside
EIM is not an exotic configuration; it is what a public one is. So an EV whose own list is a strict
subset of what a conformant station offers cannot get past the fourth message — and "EIM only" is that
subset for most cars most of the time.

**Suggested direction.** Select from the intersection instead of policing the union:

```python
mine   = self.controller.data_model.authorization_services
usable = [s for s in payload.authorization_services if s in mine]
if not usable:
    ...   # a genuine "no common authorization service", which this is not
selected = usable[0]
```

The `# TODO other cases` marker suggests the `raise` was meant as *"PnC is not implemented yet"* rather
than *"this station is wrong"*. Turning it into a filter says the same thing without taking the session
with it. There are two or three reasonable shapes and which belongs in your tree is yours; we would
send a PR only if you want one.

## What stood behind it, and what that means for the fix

Worth setting out, because our own notes got this wrong for five days and a reader of them would too.

Patching the `raise` to a `continue` in a throwaway copy removed the crash — and their EV then
**terminated the session** rather than authorizing. We could not tell whether that was theirs or the
patch's doing, so we removed the variable: our SECC gained an option to offer EIM alone, and the run
was repeated against an **unpatched** tree. Same result — four exchanges, `SessionStopReq` straight
after `AuthorizationSetupRes`. At that point the notes recorded a second wall with *"root cause not
identified"*.

**It was not a second protocol defect.** On 2026-08-06 it turned out to be `stdin`: their EV arms a
"press Enter to stop the session" listener in `TCPClientProtocol.__init__` unconditionally and awaits
`sys.stdin.readline` in an executor, so at EOF it fires within a millisecond and `process_reaction`
replaces whatever the state machine built with a `SessionStopReq` in the first state that permits one —
`exitable_states = states[2:-3]`, which begins at `WaitForAuthorizationSetupResponse`. That is
**issue 1 of our other report**, and it is why the termination always looked like an authorization
problem. With stdin held open and nothing else changed, the same EV ran fifteen exchanges through
`ScheduleExchange`, `CableCheck`, `PreCharge`, `PowerDelivery` and into `DC_ChargeLoop`
([`2026-08-06-edf-stdin-wall`](../interop-runs/2026-08-06-edf-stdin-wall/notes.md)).

So the two are independent and both real, and **this one is the one still standing between their EV and
an ordinary charge point**: fix issue 1 and a car meeting a PnC-and-EIM station still raises here.

**And the two are now separated on the wire, not only in the reading.** That fifteen-exchange run
offered **EIM only**, the configuration that avoids this defect, so it left one cell unfilled: stdin
open *and* a PnC-and-EIM offer. Filled on 2026-08-10 — same rig, same commit, nothing of yours patched,
stdin held open by a fifo — and your EV raises at `wait_for_authorization_setup_response.py:36` after
receiving the offer quoted above.

| Run | stdin | Offer | Your EV |
|---|---|---|---|
| 2026-08-01 | EOF | PnC, EIM | `NotImplementedError` |
| 2026-08-01, control | EOF | EIM | 4 exchanges, clean `SessionStopReq` |
| 2026-08-06 | **open** | EIM | **15 exchanges**, into `DC_ChargeLoop` |
| **2026-08-10** | **open** | **PnC, EIM** | **`NotImplementedError`** |

The two failures even look different in the transcript. The stdin wall ends a session cleanly — the
state machine is intact and emits `SessionStopReq`, so the flow has four exchanges. This one kills the
connection inside the handler: three exchanges, no `SessionStopReq`, and none of the *"stop has been
requested"* lines every stdin-wall run carries.

## The offer that breaks your EV is one your own station can emit

Not a second issue, and not something we have observed — a note for whoever fixes the loop above, so
that both halves get looked at in one pass. **Your SECC will produce exactly the message your EVCC
raises on, if anyone sets the field that says so.**

```python
# secc/states/process_authorization_setup_request.py:28-31
response.authorization_services = self.controller.data_model.authorization_services
response.certificate_installation_service = self.controller.data_model.certificate_installation_service
# TODO: given the services in authorization services, the response shall be eim or pnc
response.eim_asres_authorization_mode = ""
```

`EVSEDataModel.authorization_services` (`secc/evse_controller.py:41`) is a declared
`List[AuthorizationType]`, and `AuthorizationType` has a `PnC` member. The **mode** is not configurable
and is always the EIM one. So a station configured `[EIM, PnC]` answers `OK`, advertises both, and sends
`EIM_ASResAuthorizationMode` — the offer that raises at
`wait_for_authorization_setup_response.py:36`, from your own tree.

The two elements are an `xs:choice`, so the effect is more than a wrong label: with the EIM branch
chosen there is **no `GenChallenge`**, and an EV could not build `PnC_AReqAuthorizationMode` even if it
wanted to. `[V2G20-1219]` and `[V2G20-2568]` each state the duty independently — offer "PnC" in
`AuthorizationServices` and the response shall carry `PnC_ASResAuthorizationMode`. Your line satisfies
the mirror, `[V2G20-1220]` / `[V2G20-2567]`, which holds only while PnC is absent.

**Two honest caveats.** The shipped default is `[EIM]` (`secc/evse_dummy_controller.py:104`), where the
line is correct, so nothing reachable out of the box is affected — and we have not run it, because
reaching it means us configuring your station rather than observing it. It is a reading of
`60249c3`, offered because the fix for the EVCC loop and the fix for this are one conversation:
whatever the tree decides an unimplemented authorization service means, both ends should say it the
same way.

---

## Before sending

- [x] **Do the missing run.** Done 2026-08-10: their EV with stdin held open, our station offering PnC
      and EIM, nothing of theirs patched — `NotImplementedError` at
      `wait_for_authorization_setup_response.py:36`, three exchanges, no `SessionStopReq`
      ([run notes](../interop-runs/2026-08-10-edf-pnc-eim-stdin-open/notes.md)). Every earlier
      observation was taken with their stdin bug also active and a maintainer would have been right to
      ask; that question is now closed on the wire rather than in the source.
- [x] **Observe it, do not only read it.** Their own EV log carries the traceback, twice over — from
      2026-08-01 and from the isolating run — and the loop is unchanged at `60249c3`.
- [x] **Re-check the source against the tree.** `wait_for_authorization_setup_response.py:27-43` and
      `ev_dummy_controller.py:111`, read on 2026-08-10; the loop still has no `break`.
- [x] **Do not blame the patch for what the patch may have caused.** The EIM-only control run exists
      for that, and the termination it showed turned out to be issue 1 rather than anything here.
- [ ] **Keep the stdin section.** It is the part that stops this being read as a duplicate of issue 1,
      and it is the part our own notes needed five days to get right.
- [x] **Check whether `main` has moved.** Checked 2026-08-11: `60249c3` **is** `origin/main`, and there
      has been no commit for two years and four months. The project is dormant on its default branch,
      which changes the pitch — expect a slow response, and a PR is more likely to be useful than an
      issue. Same caveat as issue 3.
- [x] **Say whether they claim the feature at all.** They do not: the README's *Supported features* list
      is DC BPT with dynamic control, mutual TLS 1.3 and SECC-set departure time, and `PnC` appears
      nowhere in `README.md` or `doc/source`. That is why the SECC note above is written as a note and
      not as a second issue — audited in
      [`2026-08-11-edf-pnc-source-audit`](../interop-runs/2026-08-11-edf-pnc-source-audit/notes.md).
- [ ] **File one issue, this one.** Issues 1, 2 and 3 are the other two reports.
- [ ] **Post under your own name, in your own words.**
