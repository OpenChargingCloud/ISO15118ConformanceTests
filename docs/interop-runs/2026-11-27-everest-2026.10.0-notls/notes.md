# 2026-11-27 — EVerest 2026.10.0: their EV asks for TLS, is answered `NoTLS`, and offers `-20` anyway

**The candidate from 2026-08-14 is now a finding, and it is their EV's.** Their EV here is `Ev15118`, the
C++ EVCC that replaced `PyEvJosev` in 2026.10.0. With `tls: prefer` it requests TLS in SDP. When its own
station answers `NoTLS`, it connects over plain TCP and puts `urn:iso:std:iso:15118:-20:DC` first in its
`SupportedAppProtocolReq`. `[V2G20-1805]` and `[V2G20-1237]` forbid exactly that offer. The same car, on
TLS 1.2, drops `-20` from its offer. Plain TCP is the weaker transport, and there it keeps `-20` and logs
nothing.

| | |
|---|---|
| Counterparty | everest-core **2026.10.0** (`74845c57`), `dist-2610`, in WSL; the EV code is unchanged on `main` `8abc3f2` (2026-11-27) |
| Peers | **both theirs**: `Ev15118` against `Evse15118D20`, from their own `config-sil-dc-d20-plaintext.yaml` and `config-sil-dc-d20-tls.yaml` |
| Changed in their configs | only the transport switches, plus `auto_exec: true` so the session starts at boot ([`make-notls.py`](scripts/make-notls.py)) |
| Evidence | per arm: `tcpdump -i any` capture, the SDP bytes, the decoded `SupportedAppProtocol` exchange, their log |
| Our stack | not on the wire. This run is their car against their station. |

## Why their station, not ours

On 2026-08-14 their `PyEvJosev` did the same against **our** reverse station. It was not filed, because
our fixture had configured their car and then offered it the downgrade
([`…-d20-ac-reverse-tls`](../2026-08-14-everest-d20-ac-reverse-tls/notes.md)). The arm that settles it is
a station of theirs answering `NoTLS`. `Evse15118D20` has a switch for exactly that,
`tls_negotiation_strategy: ENFORCE_NO_TLS`.

## The arms

One manager per arm, one session each, about 30 s of session.

| arm | station | EV | SDP request → response | transport | SAP offer (EV) | SAP answer | end |
|---|---|---|---|---|---|---|---|
| `notls-prefer` | `ENFORCE_NO_TLS` | `tls: prefer`, `-20` | TLS → **NoTLS** | **plain TCP** | **`-20:DC`** | `OK_SuccessfulNegotiation`, `-20` | `SessionStop` |
| `notls-prefer-both` | `ENFORCE_NO_TLS` | `tls: prefer`, `-20`,`-2` | TLS → **NoTLS** | **plain TCP** | **`-20:DC`**, then `-2` | `OK_SuccessfulNegotiation`, `-20` | `SessionStop` |
| `notls-require` | `ENFORCE_NO_TLS` | `tls: require` | TLS → NoTLS (×50) | none | — | — | the EV ignores every response |
| `tls-prefer` | `ENFORCE_TLS`, 1.3 | `tls: prefer`, version `auto` | TLS → **TLS** | TLS 1.3 | not readable: encrypted | `-20` (their log) | `SessionStop` |

**The SDP bytes** ([`*.sdp.txt`](sessions/)). The request payload is `01 fe 90 00 00 00 00 02 00 00`:
security `0x00` (TLS), transport `0x00` (TCP), the same in all four arms. The response ends `c3 50 10 00`,
port 50000 and security **`0x10`** (no TLS), in the three `ENFORCE_NO_TLS` arms. In `tls-prefer` it ends
`c3 50 00 00`, security `0x00`.

**The offer** ([`*.sap.txt`](sessions/), decoded from the capture by [`sap-offer.py`](scripts/sap-offer.py)):

```
notls-prefer       EV -> SECC  V2GTP 0x8001, 37 B   namespace: urn:iso:std:iso:15118:-20:DC
                   SECC -> EV  V2GTP 0x8001,  4 B   80400040
notls-prefer-both  EV -> SECC  V2GTP 0x8001, 71 B   namespace: urn:iso:std:iso:15118:-20:DC
                                                    namespace: urn:iso:15118:2:2013:MsgDef
                   SECC -> EV  V2GTP 0x8001,  4 B   80400040
tls-prefer         no plaintext V2GTP 0x8001 frame in this capture
```

`80400040` is `OK_SuccessfulNegotiation` for SchemaID 1, the `-20` entry. The EV log confirms it:
`Ev15118: selected protocol ISO15118-20`.

**Their station warns, and their car does not.** Each `ENFORCE_NO_TLS` arm logs a startup warning and a
per-session warning from the station:

```
Evse15118D20: supported_ISO15118_20 is set together with tls_negotiation_strategy ENFORCE_NO_TLS.
  ISO 15118-20 mandates TLS [V2G20-2677], so any negotiated -20 session will not be standard-conformant. …
Negotiated ISO 15118-20 (urn:iso:std:iso:15118:-20:DC) on an unsecured connection.
  ISO 15118-20 mandates TLS [V2G20-2356], so this session is not standard-conformant
```

The `iso15118_car` lines in those two arms contain no warning at all.

## Finding — `tls: prefer` keeps `-20` in a plaintext offer

**The requirement.** `[V2G20-1804]`: when the SDP response offers TLS, the EVCC establishes TLS.
`[V2G20-1805]`: when that does not happen, the EVCC does not offer `-20` and the SECC does not select it.
`[V2G20-1237]` says the same about the established connection directly: over TLS 1.2 or plain TCP the
EVCC does not offer `-20`. Table 5 gives `-20` the TLS 1.3 row only.

**The mechanism** (`lib/everest/iso15118/src/iso15118/ev/session/sap_offer.cpp`, identical in
2026.10.0 and on `main`):

```cpp
// -20 over TLS 1.3, -2 over TLS 1.2; no negotiated version keeps every protocol.
bool runs_over(ProtocolId protocol, std::optional<config::TlsVersion> negotiated) {
    if (not negotiated.has_value()) {
        return true;
    }
    …
```

A plaintext connection has no negotiated version, so every protocol stays in the offer. The function
narrows the offer for TLS 1.2 and TLS 1.3 and not for plain TCP, the one transport `-20` is allowed on
least.

**What their documentation says.** `tls: off` is documented as a deviation, *"ISO 15118-20 over
plaintext deviates from [V2G20-2677] and is meant for testing"*. `prefer` is documented as *"requests
TLS in SDP and accepts a plaintext offer"*, with nothing about `-20`. So the deviation is declared for
the mode that never asks for TLS and undeclared for the mode that asks and is refused. In the second
case the car knows the station said no.

**Why it matters beyond conformance.** `prefer` is the mode a production car would choose to stay
compatible with `-2` stations that serve no TLS. In that mode a single SDP response with the security
byte set to `0x10` moves a `-20` session to plain TCP. Every `-20` message then travels unauthenticated.
It costs a downgrade only if something answers SDP. `allow_pnc_without_tls` still keeps the contract off
plain TCP by default, so this is not a credential leak.

**The fix is one line, and their station already has the model for it.** When the transport is plain and
`-20` is not explicitly allowed over it, `runs_over` should return `false` for `-20`. `-2` and DIN stay
offered, which is what `[V2G20-1805]`'s *"Refer to Table 5"* points at. `tls: off` can keep its
documented testing deviation. `Evse15118D20` already distinguishes the two cases with
`supported_ISO15118_20` and a warning.

**Not a station finding.** `Evse15118D20` selecting `-20` on plain TCP is a `[V2G20-2356]` and
`[V2G20-1238]` matter, but it is **declared**: the manifest documents it, the module warns at startup and
in every session, and `supported_ISO15118_20: false` turns it off. That is a configuration the operator
chose, not a defect.

## Controls

- **`notls-require`** shows their EV can refuse. `EV Controller: SECC offers no TLS but tls is require;
  ignoring the SDP response`, 50 times, and no TCP connection.
- **`tls-prefer`** shows the same EV mode does the right thing when TLS is on offer: TLS 1.3
  (`Client supports TLS1.3`), nothing readable in the capture, `-20` to `SessionStop`. The station warned
  that it holds no P-521 leaf and presents the P-256 one. The EV accepted it, which is beside this
  finding.

## Artifacts

- [`sessions/`](sessions/): per arm the capture (`*.pcap`), SDP bytes (`*.sdp.txt`), the decoded SAP
  exchange (`*.sap.txt`) and their log (`*.their-side.log`).
- [`configs/`](configs/): the four configs, each a release config plus the switches named in its header.
- [`scripts/`](scripts/): [`make-notls.py`](scripts/make-notls.py) writes the configs;
  [`notls-2610.sh`](scripts/notls-2610.sh) runs the arms as root for the capture;
  [`sap-offer.py`](scripts/sap-offer.py) reads the offer out of a Linux-cooked capture.

## Next

The report is drafted: [`everest-ev15118-plaintext-iso20-offer`](../../reports/everest-ev15118-plaintext-iso20-offer.md).
Nothing here touches the matrix, which measures our stack against theirs.
