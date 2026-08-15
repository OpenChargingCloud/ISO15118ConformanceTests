# Draft report to EDF Lab — an omitted `SupportedServiceIDs` ends the session

Status: **draft, not sent.** Found on the wire 2026-08-01 against `eVDriveFlow` at `60249c3`, and the
source re-read at that same commit on 2026-08-10 — it is unchanged, and it is their `main`. Post it
under your own name; see *Before sending* at the bottom.

Evidence in this repository:
[`2026-08-01-edf-iso20-dc-notls`](../interop-runs/2026-08-01-edf-iso20-dc-notls/notes.md) — finding 1,
with [`finding1-workaround.py`](../interop-runs/2026-08-01-edf-iso20-dc-notls/finding1-workaround.py),
the two-line patch we applied **inside a throwaway container** to get past it. Our own stack was not
changed for this and did not need to be.

The other report for the same project is
[`evdriveflow-headless-session.md`](evdriveflow-headless-session.md), whose issues 1 and 2 are the
no-GUI path. **File them separately** — and read the note at the end of this one first, because this
finding and its issue 2 are the same mistake in two places, which is the most useful thing here.

---

**Title:** `secc/states/process_service_discovery_request.py`: `SupportedServiceIDs` is optional and is
dereferenced unconditionally, so an EV that does not pre-filter takes the station down — and when it
*is* present, a filter naming neither service leaves the mandatory `EnergyTransferServiceList` unset

**Version:** `eVDriveFlow` `60249c3` (2023-04-17, `main`), Python SECC, ISO 15118-20 DC.

## Summary

`ServiceDiscoveryReq` carries `SupportedServiceIDs` with `minOccurs="0"`
(`V2G_CI_CommonMessages.xsd`, `ServiceDiscoveryReqType`). The standard's own semantics table for that
message says what leaving it out means: the EV wants **every** service the SECC provides. It is a
pre-filter, and using it is the EV's option, not its duty.

Your station reads it without checking (`process_service_discovery_request.py:31`):

```python
if 6 in payload.supported_service_ids.service_id:
    response.energy_transfer_service_list = self.controller.data_model.energy_transfer_service_list["6"]
elif 2 in payload.supported_service_ids.service_id:
    response.energy_transfer_service_list = self.controller.data_model.energy_transfer_service_list["2"]
    self.session_parameters.dc_bpt_selected = False
```

Our EVCC omits the element, which is the ordinary case, and the session ends:

```
AttributeError: 'NoneType' object has no attribute 'service_id'
```

**Every conformant EV that does not pre-filter hits this.** It is the fifth message of the session, so
nothing beyond `SessionSetup`/`AuthorizationSetup` is reachable from such a car at all.

## The second half, in the same three lines

Even with the element present, those lines are not a filter but a two-branch lookup, and the branches
do not cover the input space:

- **A filter naming neither 2 nor 6** — say an AC car listing service 1, or an EV filtering for a VAS —
  falls through both branches, so `response.energy_transfer_service_list` is never assigned.
  `EnergyTransferServiceList` is **mandatory** in `ServiceDiscoveryRes`, and the response still carries
  `ResponseCode = OK`. The EV is told everything is fine and handed a message missing the element the
  whole exchange exists to deliver.
- **A filter naming both** always answers with 6 (DC_BPT), because 6 is tested first. That one is
  defensible — it is a filter, the EV said it supports both, and a station may offer its BPT entry —
  but it is worth knowing that the list cannot express "both" even though `ServiceIDListType` is a list
  and your data model holds an entry per service.

We did not put the neither-branch on the wire; it is read from the source, and it is the reason the
suggested fix below is a loop rather than a `None` check bolted onto the front.

## Suggested direction

The shape that fixes both halves at once is to treat absence as "no filter" and to build the answer
from the intersection rather than from a chain:

```python
wanted = payload.supported_service_ids.service_id if payload.supported_service_ids else None
offered = [sid for sid in ("6", "2") if wanted is None or int(sid) in wanted]
response.energy_transfer_service_list = merge(self.controller.data_model.energy_transfer_service_list[s]
                                              for s in offered)
```

— with whatever `merge` your `ServiceListType` wants, and with the empty case answered as an empty
list rather than left unset. The `dc_bpt_selected` assignment on line 35 can go: it is decided
correctly one message later in `process_service_selection_request.py`, from what the EV actually
selected, and the default in `evse_session.py:151` covers the rest.

We are not attached to that shape and would send a PR only if you want one.

## Why this is worth more than the one crash

**It is the same mistake as issue 2 of our other report, and that is the finding.** There,
`evcc/states/wait_for_dc_charge_loop_response.py:30,32` uses `hasattr` on an xsdata `Optional` field,
so a legally omitted `TargetSOC` silently overwrites the EV's own with `None`. Here, an optional
element is dereferenced without a check. Both come from the same place: **xsdata gives every optional
element the value `None`, and `None` neither answers `hasattr` the way a missing attribute would nor
survives being dereferenced.**

We stopped guessing and counted. `hasattr` on a message field appears **seven times, in four files,
on both sides**:

| | |
|---|---|
| `evcc/states/wait_for_schedule_exchange_response.py` | 31 |
| `evcc/states/wait_for_dc_charge_loop_response.py` | 30, 32 — this is issue 2 |
| `secc/states/process_schedule_exchange_request.py` | 31, 33 |
| `secc/states/process_dc_charge_parameter_discovery_request.py` | 40, 42 |

Every one of those is asking "did the peer send this?" of an xsdata dataclass, where the attribute is
always there and the *value* is what carries the answer. They are not all equally harmful — several
guard a value the code then defaults sensibly — but none of them tests what it looks like it tests, and
the two we watched on the wire both went wrong. Sweeping `hasattr(x, "y")` to `x.y is not None` across
those four files is a smaller job than the individual fixes and closes the class, which is why this
report leads with it rather than with the traceback.

## The same mistake one message later, and this one *was* observed

**2026-08-15, on the wire, from a conformant car.** `process_dc_charge_loop_request.py:114` reads the
optional `DisplayParameters` the same way line 31 reads `SupportedServiceIDs`:

```python
display_parameters = payload.display_parameters
self.controller.data_model.update_charging_status(display_parameters.present_soc,
                                                  display_parameters.battery_energy_capacity, power)
```

`DisplayParameters` is `minOccurs="0"` in `ChargeLoopReqType`, our EVCC omits it, and the station ends
the same way as at line 31:

```
Received DcChargeLoopReq.
AttributeError: 'NoneType' object has no attribute 'present_soc'
```

The same read is at `:176` for the unidirectional branch, so both DC paths carry it. This one is
**worth more than the crash at line 31 in one respect and less in another**: more, because the EV that
hits it has done everything right — selected a service your catalogue offers, negotiated Dynamic
control, driven `CableCheck`, `PreCharge` and `PowerDelivery` to `OK`, and then sent a legal charge-loop
request; less, because by then the session has already had to get past line 31, so nobody has ever seen
it. It is written up in
[`2026-08-15-edf-session-id-460`](../interop-runs/2026-08-15-edf-session-id-460/notes.md), a run that
had a reason to send the filter.

**Not split into its own filing**, deliberately: it is the same one-line pattern in the same tree, and
the suggested direction below closes it in the same sweep. If it is split when this is posted, the
count changes and the argument does not.

## Also seen, and deliberately **not** reported

Our 2026-08-01 notes list a third observation, which does not survive checking and is corrected here
rather than left standing: `process_dc_charge_loop_request.py:128` reads
`payload.dynamic_dc_clreq_control_mode` unconditionally, so a **Scheduled** DC charge loop raises
`AttributeError`. True, but your station advertises `ControlMode = 2` (Dynamic) in the only parameter
set it offers for either service (`evse_dummy_controller.py:109-114`), so a conformant EV never selects
Scheduled and never sends one. Reaching that line takes a car that ignored the catalogue it was given
— which ours did in August, before it was taught to read parameter sets. What is left is that
malformed input crashes rather than being refused, and that is not a thing we would file against you.

**The `display_parameters` read fourteen lines earlier is the opposite case**, which is why it is in the
report proper and this one is not: there the input is legal and the omission is the standard's own
default. Two reads in one file, one reportable and one not, is the distinction this section exists for.

---

## Before sending

- [x] **Observe it, do not only read it.** Hit on the wire on 2026-08-01 by an EVCC that omits the
      element; the workaround we applied to get past it is checked in, and it was applied to a
      throwaway copy of *their* code, never to ours.
- [x] **Re-check the source against the tree.** `process_service_discovery_request.py:31-35`,
      `process_service_selection_request.process_payload`, `evse_dummy_controller.py:109-114`,
      `evse_session.py:151` and all seven `hasattr` sites read from `60249c3` on 2026-08-10.
- [ ] **Say which half was observed and which was read.** The crash was observed, and so is the
      `display_parameters` one since 2026-08-15. The unset `EnergyTransferServiceList` was not — it
      comes from reading the branches — and the report says so; keep that distinction when it is
      rewritten in your own words.
- [x] **Check that the fix is reachable from outside.** With `SupportedServiceIDs` present their station
      runs the whole DC sequence — `ServiceDetail`, `ServiceSelection`, `ChargeParameterDiscovery`,
      `ScheduleExchange`, `CableCheck`, `PreCharge`, `PowerDelivery`, all `OK`
      ([2026-08-15](../interop-runs/2026-08-15-edf-session-id-460/notes.md)). So line 31 is a wall in
      front of a station that otherwise works, which is the strongest version of this report and was
      not known when it was written.
- [ ] **Lead with the family, not the traceback.** The optional-element pattern is the part worth their
      attention; a maintainer who fixes only line 31 has fixed one of an unknown number.
- [x] **Check whether `main` has moved — checked 2026-08-11: it has not.** `EDF-Lab/eVDriveFlow` `main`
      is still `60249c3` (2023-04-17), **three years and four months** without a commit. So the project
      is dormant rather than merely slow, and the pitch follows from that: a PR with the fix in it, not
      an issue asking for one, and no expectation of a reply.
- [ ] **File one issue, this one.** The no-GUI path is the other report, and it is separate.
- [ ] **Post under your own name, in your own words.**
