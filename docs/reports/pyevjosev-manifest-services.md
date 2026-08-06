# Draft report to EVerest — `PyEvJosev`'s manifest under-documents `supported_d20_energy_services`

Status: **draft, not sent.** Found 2026-08-06 while configuring their own `config-sil-mcs.yaml` for a
reverse interop run. Post it under your own name; see *Before sending* at the bottom.

Evidence in this repository: finding 3 of
[`2026-08-06-everest-mcs-reverse`](../interop-runs/2026-08-06-everest-mcs-reverse/notes.md), and the
session it came out of — their `PyEvJosev` selecting **MCS** against our SECC, recorded in
[`2026-08-06-everest-mcs-reverse-recorded`](../interop-runs/2026-08-06-everest-mcs-reverse-recorded/notes.md).

This one is documentation only. It is worth filing anyway because the value it omits is used by a config
in the same repository, and because the code silently ignores a value it does not recognise.

---

**Title:** `PyEvJosev`: `supported_d20_energy_services` documents 4 of the 12 accepted values — including
`MCS`, which your own `config-sil-mcs.yaml` uses

**Version:** everest-core **2026.02.1** (`b61bb12b8`); vendored Josev
`EVerest/ext-switchev-iso15118` at `26f7988`.

## Summary

`modules/EV/PyEvJosev/manifest.yaml` (lines 55–61) documents four accepted values:

```yaml
supported_d20_energy_services:
  description: >-
    The supported ISO15118-20 energy services (DC, DC_BPT, AC, AC_BPT) the EV supports,
    provided as a prioritized list. The first entry in the list has the highest priority
    and is the most likely to be selected. The services should be separated only with commas.
  type: string
  default: "DC,DC_BPT"
```

The vendored Josev accepts twelve — `iso15118/shared/utils.py`, `load_requested_energy_services`:

```python
supported_services = [
    "AC", "DC", "WPT", "DC_ACDP", "AC_BPT", "DC_BPT", "DC_ACDP_BPT",
    "MCS", "MCS_BPT", "AC_DER", "INTERNET", "PARKING_STATUS",
]
```

And `config/config-sil-mcs.yaml:19` sets one of the eight that are missing:

```yaml
supported_d20_energy_services: MCS
```

So the shipped MCS configuration uses a value the module's own manifest does not list.

## Why this is more than a typo in a description

`type: string` is free-form, so nothing validates the value at config time — which is why
`config-sil-mcs.yaml` works, and why the gap is invisible from the outside. But an unrecognised entry is
then **silently dropped**, not reported:

```python
valid_services = [service for service in services if service in supported_services]
if not valid_services:
    raise NoSupportedEnergyServices(...)
```

A list with at least one recognised entry passes, and the unrecognised ones vanish without a log line.
`"DC,MSC"` quietly becomes `DC`. That makes the manifest description the only place a user can learn the
correct spelling — which is exactly the thing that is incomplete.

Practically, the failure mode is the milder one but still costs time: a value absent from the manifest
reads as unsupported, so the natural conclusion is that `PyEvJosev` cannot request an MCS session. It can;
we ran one against a third-party SECC on 2026-08-06, and their EV selected service 8 and drove a complete
ISO 15118-20 session on it.

## The station side already avoids this, which suggests the fix

`modules/EVSE/EvseManager/manifest.yaml:9-12` documents its enum by **reference** rather than by copying
the values:

```yaml
connector_type:
  description: The connector type of this evse manager (/evse_manager#/ConnectorTypeEnum)
  type: string
  default: "Unknown"
```

That cannot drift. The EV-side entry writes its list inline, and it has drifted by eight values.

## Suggested direction

Smallest fix — complete the list:

```diff
-      The supported ISO15118-20 energy services (DC, DC_BPT, AC, AC_BPT) the EV supports,
+      The supported ISO15118-20 energy services (AC, DC, WPT, DC_ACDP, AC_BPT, DC_BPT,
+      DC_ACDP_BPT, MCS, MCS_BPT, AC_DER, INTERNET, PARKING_STATUS) the EV supports,
```

Better, if you would rather not maintain a copy: point at Josev's `supported_services` the way
`connector_type` points at its schema enum, so the manifest cannot fall behind again.

Optional and separate: have `load_requested_energy_services` log the entries it discards. It is a
one-line `logger.warning`, and it turns a silent typo into a visible one. Your call whether that belongs
in `ext-switchev-iso15118` rather than here.

## Before sending

- [x] **Verify it against the shipped tree**, not a memory: manifest lines 55–61, `utils.py`
      `load_requested_energy_services`, `config-sil-mcs.yaml:19`, all read from
      everest-core 2026.02.1 as built.
- [x] **Confirm the config actually works**, so the report cannot be read as "MCS is broken" — it is not,
      the documentation is incomplete. A complete MCS session on that config is recorded in the run notes
      linked above.
- [ ] **File it as its own issue.** It is unrelated to the accept-loop shutdown
      ([`everest-loop-shutdown.md`](everest-loop-shutdown.md)); do not bundle them, they belong to
      different modules and have different severities.
- [ ] **Post under your own name, in your own words.** Worth keeping the sentence about how it was hit:
      somebody configuring their EV module from the manifest would conclude MCS is unsupported, while
      their own MCS config proves otherwise.
- [ ] **Offer the one-line PR only if they want it** — and ask which of the two shapes they prefer, the
      completed list or the reference, since that is a maintenance preference and theirs to make.
