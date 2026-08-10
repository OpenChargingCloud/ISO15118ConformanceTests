# Draft report to EDF Lab — a station offering PnC *and* EIM ends their EV's session

Status: **draft, not sent.** Observed on the wire 2026-08-01 against `eVDriveFlow` at `60249c3`, in
the reverse direction (their EV, our SECC), and the source re-read at that same commit on 2026-08-10.
Post it under your own name; see *Before sending* at the bottom — the first item is a run that has not
been done and that a maintainer will ask about.

Evidence in this repository:
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

**The gap we have not closed.** That fifteen-exchange run offered **EIM only**
(`V2G_INTEROP_NO_PNC=1`) — the configuration that avoids this defect. Nobody has yet run their EV with
stdin open **and** a PnC-and-EIM offer. From the source it must still raise: the loop sits on the
protocol path and has nothing to do with stdin. But that is a reading, not an observation, and the
report says which is which.

---

## Before sending

- [ ] **Do the missing run.** Their EV, stdin held open, our station offering PnC and EIM — the two
      knobs are `run.sh` from the stdin-wall notes and dropping `V2G_INTEROP_NO_PNC=1`. It should end
      in `NotImplementedError` at `AuthorizationSetupRes`. Every observation of this defect so far was
      taken with the stdin bug also active, and a maintainer is entitled to ask whether it was ever
      anything else.
- [x] **Observe it, do not only read it.** The `NotImplementedError` is in their own EV log from
      2026-08-01, and the loop is unchanged at `60249c3`.
- [x] **Re-check the source against the tree.** `wait_for_authorization_setup_response.py:27-43` and
      `ev_dummy_controller.py:111`, read on 2026-08-10; the loop still has no `break`.
- [x] **Do not blame the patch for what the patch may have caused.** The EIM-only control run exists
      for that, and the termination it showed turned out to be issue 1 rather than anything here.
- [ ] **Keep the stdin section.** It is the part that stops this being read as a duplicate of issue 1,
      and it is the part our own notes needed five days to get right.
- [ ] **Check whether `main` has moved.** `60249c3` is from 2023-04-17 — the project may be dormant,
      which changes the pitch and possibly whether an issue or a PR is the right vehicle. Same caveat
      as issue 3.
- [ ] **File one issue, this one.** Issues 1, 2 and 3 are the other two reports.
- [ ] **Post under your own name, in your own words.**
