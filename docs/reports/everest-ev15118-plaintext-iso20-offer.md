# Draft report to EVerest (`Ev15118` / libiso15118 EV) — with `tls: prefer`, a `NoTLS` answer leaves ISO 15118-20 in a plaintext offer

Status: **draft, not sent**, **measured on the wire** against their own station, and the mechanism read
in the release that ran and on `main`. Post it under your own name; see *Before sending* at the bottom.

Evidence in this repository:
[`2026-11-27-everest-2026.10.0-notls`](../interop-runs/2026-11-27-everest-2026.10.0-notls/notes.md),
with the captures. The candidate it settles was first seen on 2026-08-14 with `PyEvJosev`
([`…-d20-ac-reverse-tls`](../interop-runs/2026-08-14-everest-d20-ac-reverse-tls/notes.md)).

---

**Title:** `Ev15118` with `tls: prefer` offers ISO 15118-20 over plain TCP after the SECC answers SDP
with no TLS (`[V2G20-1805]`, `[V2G20-1237]`)

**Version:** everest-core **2026.10.0** (`74845c57`); the code below is unchanged on `main` (`8abc3f2`,
2026-11-27). `modules/EV/Ev15118`, `lib/everest/iso15118/src/iso15118/ev/session/sap_offer.cpp`.

## Observed

Your own SIL, both peers yours: `config-sil-dc-d20-plaintext.yaml` with two settings changed,
`Evse15118D20.tls_negotiation_strategy: ENFORCE_NO_TLS` and `Ev15118.tls: prefer`.

1. The EV requests TLS in SDP: request payload `00 00`, security `0x00` (TLS).
2. The SECC answers with no TLS: response ending `c3 50 10 00`, security `0x10`.
3. The EV connects over plain TCP and sends a `SupportedAppProtocolReq` whose first entry is
   `urn:iso:std:iso:15118:-20:DC`. With `supported_protocols: ISO15118_20,ISO15118_2` it is `-20:DC`
   first and `-2` second.
4. The SECC selects `-20` (`OK_SuccessfulNegotiation`, SchemaID 1), and the session runs to
   `SessionStop`.

The station logs that this session is not conformant. The EV logs nothing:

```
iso15118_charger :: Negotiated ISO 15118-20 (urn:iso:std:iso:15118:-20:DC) on an unsecured connection.
                    ISO 15118-20 mandates TLS [V2G20-2356], so this session is not standard-conformant
iso15118_car     :: Ev15118: selected protocol ISO15118-20
```

Two controls from the same build. With `tls: require` the EV ignores the `NoTLS` response, as it should.
Against `ENFORCE_TLS` the same `prefer` EV negotiates TLS 1.3 and `-20` normally.

## Where it comes from

`sap_offer.cpp`:

```cpp
// -20 over TLS 1.3, -2 over TLS 1.2; no negotiated version keeps every protocol.
bool runs_over(ProtocolId protocol, std::optional<config::TlsVersion> negotiated) {
    if (not negotiated.has_value()) {
        return true;
    }
    if (protocol == ProtocolId::ISO15118_20) {
        return negotiated == config::TlsVersion::Tls1_3;
    }
    return protocol == ProtocolId::ISO15118_2 and negotiated == config::TlsVersion::Tls1_2;
}
```

After a TLS handshake the offer is narrowed per version, and `-20` leaves it on TLS 1.2. A plaintext
connection has no negotiated version, so nothing is narrowed and `-20` stays.

## Why we think it is worth fixing

- **The standard says it twice.** `[V2G20-1804]`/`[V2G20-1805]`: when the TLS connection the SDP
  exchange asked for is not established, the EVCC does not offer `-20`. `[V2G20-1237]`: over TLS ≤ 1.2
  or plain TCP, the EVCC does not offer `-20`. Table 5 lists `-20` under TLS 1.3 only.
- **Your docs declare the deviation for `off` and not for `prefer`.** `off`: *"ISO 15118-20 over
  plaintext deviates from [V2G20-2677] and is meant for testing."* `prefer`: *"requests TLS in SDP and
  accepts a plaintext offer."* A reader would take `prefer` to mean `-2` without TLS where a station has
  none, not `-20` without TLS.
- **It is a downgrade path.** `prefer` is the mode a car would pick to stay compatible with plaintext
  `-2` stations. In that mode one SDP response with `0x10` moves a `-20` session to unauthenticated TCP.
  `allow_pnc_without_tls` still keeps the contract off it, which is why this is an issue and not an
  advisory.
- **Your station already handles the mirror case well.** `Evse15118D20` documents the plaintext `-20`
  case, warns at startup and per session, and offers `supported_ISO15118_20: false`.

## Suggested fix

Offered, not asserted. When TLS was requested (`tls != off`) and the connection is plain TCP, drop `-20`
from the offer. `-2` and DIN stay, which is where `[V2G20-1805]`'s *"refer to Table 5"* points. If no
protocol is left, stop with an error naming the transport. That is what you already do after a
handshake that leaves nothing. `tls: off` can keep its documented testing deviation. A warning when `-20`
does go out over plain TCP would match the station's.

A test: `build_sap_offer` with `tls = true`, no negotiated version and `{ISO15118_20, ISO15118_2}`
should yield `-2` alone.

---

## Before sending

- [x] **Reproduce it with their code only.** Both peers are theirs, two config switches, the release
      build. Captures and the decoded offer are in the run directory.
- [x] **Controls:** `tls: require` refuses; an `ENFORCE_TLS` station gets TLS 1.3. The finding is the
      plaintext branch alone.
- [x] **Check `main`.** `sap_offer.cpp` and `controller.cpp` are unchanged since `74845c57`; no open
      issue or PR in `EVerest/EVerest` matches *plaintext*, *V2G20-1237*, *V2G20-1805* or *tls prefer*
      (searched 2026-11-27).
- [ ] **Decide whether `prefer` is meant for production at all.** If they say it is a bring-up mode
      like `off`, the ask shrinks to documenting it and a warning. Ask that first.
- [ ] **Keep the station out of it.** `Evse15118D20` declares its half; mentioning it as a model helps,
      filing it as a defect would not.
- [ ] **Re-check `main` on the day you post.**
- [ ] **Post under your own name, in your own words.**
