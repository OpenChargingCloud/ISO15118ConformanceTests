# 2026-08-11 — EVerest's `-20` station sends no price schedule, and says so in a comment

**A source audit, corroborated by frames already in this directory. No rig was started, and none would
have helped** — which is the result rather than a shortcut.

everest-core **2026.02.1**, `lib/everest/iso15118/`.

## The question

`Signed tariffs (AbsolutePriceSchedule)` was `▢` for EVerest in the interop matrix. The run worth making
would have been `EV→`: their station offers a signed `AbsolutePriceSchedule` in `ScheduleExchangeRes`,
our EVCC verifies the signature against their PKI. That would have been the first external witness for
tariff signing in either direction — the Josev column's `◐` is the other way round and has no verifier
behind it.

## The answer, in their own words

`src/iso15118/d20/state/schedule_exchange.cpp:25-42`, building the **Scheduled** response:

```cpp
ScheduledResControlMode scheduled_mode{};

// Providing no price schedule!
// NOTE: Agreement on iso15118.elaad.io: [V2G20-2176] is not required and should be ignored.
scheduled_mode.schedule_tuple = {schedule};
```

and at the call site, twenty lines further down:

```cpp
res.control_mode.emplace<ScheduledResControlMode>(create_default_scheduled_control_mode(max_power));

// TODO(sl): Adding price schedule
// TODO(sl): Adding discharging schedule
```

The **Dynamic** branch is the same answer by a different route: it fills `DynamicResControlMode` through
`set_dynamic_parameters_in_res`, which sets `departure_time`, `target_soc`, `minimum_soc` and
`ack_max_delay` and nothing else.

So **neither control mode ever emits a price schedule**, absolute or level. There is nothing for our EV
to verify, and no configuration reaches it: the decision is in the response builder, not behind a flag.

`AbsolutePriceSchedule` does exist in their tree — `message/schedule_exchange.{hpp,cpp}` converts it in
both directions — so their **codec** handles it and their **station** never produces one. That
distinction is the same one the eVDriveFlow Plug & Charge audit turned on, and it is why grepping for
the type name is not an answer.

## The bytes agree, and they were already here

Every `ScheduleExchangeRes` this project has recorded from their `-20` station:

| run | bytes |
|---|---:|
| `2026-08-03-everest-iso20-dc-dynamic` | 28 |
| `2026-08-03-everest-ac` | 43 |
| `2026-08-03-everest-iso20-dc-full-charge` | 44 |
| `2026-08-03-everest-iso20-dc-tls13` | 44 |
| `2026-08-06-everest-isomux-tls` | 44 |
| `2026-08-06-everest-mcs-bpt-complete` | 44 |

28 to 44 bytes, including the V2GTP header and a schedule tuple. An `AbsolutePriceSchedule` carries a
currency, a price-rule stack and a header signature over the whole fragment; a `PriceLevelSchedule` is
smaller but still a list of entries. Neither fits. **The frames were kept for other questions and answer
this one without a session** — the third time this month.

## What this is *not*

**Not a filing, and this is the part to get right.** `[V2G20-2176]` is a *shall* — the SECC shall
provide either an `AbsolutePriceSchedule` or a `PriceLevelSchedule` as part of the
`ChargingScheduleType` — and they do not comply. But the deviation is **deliberate, documented in the
code, and attributed to an industry interoperability agreement** (`iso15118.elaad.io`). That is a
decision by people closer to the field than this project, recorded openly at the point of
non-compliance. Reporting it as a defect would be telling maintainers something they wrote down
themselves.

It goes in [`reports/README.md`](../../reports/README.md)'s *deliberately not here* category — design
properties, not defects — and the useful part is the data point rather than the verdict: **a `shall` in
the published text that at least one major implementation treats as withdrawn by agreement.** Worth
knowing before this project cites `[V2G20-2176]` against anybody.

## What moves

The matrix cell goes from `▢` to `—` for EVerest: not applicable, because their station implements
none of it. This is closing a cell by **answering its condition** rather than by testing — the same
move as the eVDriveFlow Plug & Charge cell on the same day, and the note is here so nobody re-opens it
as an obvious next run.

**The capability on our side is untouched by this.** Our SECC signs an `AbsolutePriceSchedule`
(`Secc20Base.TariffSignKey`) and our EVCC verifies one; the loopback covers both ends. What stays
missing is any **external** verifier, and after this audit the field of candidates is:
Josev consumes ours without checking (its EVCC-side tariff check is a literal `# TODO`), eVDriveFlow's
`-20` is `◐` at best and tux-evse has no `-20` at all. **No stack this project can reach verifies a
signed tariff.** That belongs in *Structural* rather than in the backlog.

## Reproduce

```
grep -n -A20 "create_default_scheduled_control_mode" \
    lib/everest/iso15118/src/iso15118/d20/state/schedule_exchange.cpp
grep -rn "set_dynamic_parameters_in_res" lib/everest/iso15118/src/
```

and, for the frame sizes, `grep -h ScheduleExchangeRes docs/interop-runs/*everest*/frames*.log`.
