# MCS_BPT against EVerest

**Matrix cell:** EVCC · ISO 15118-20 MCS · MCS_BPT · EVerest

Back to the [interop matrix](../../README.md).

---

Green on the second attempt: the first was refused with `FAILED_WrongChargeParameter`, correctly, and
that refusal is what proved the service/parameter coupling binds the EV too. Their `EvseManager` decoded
`dc_ev_maximum_power_limit: 3750000.0` at 3000 A / 1250 V. Megawatt **power** stays out of reach — their
MCS SIL is electrically a 22 kW charger.
