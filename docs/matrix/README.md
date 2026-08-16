# Matrix cells

One document per cell of the [interop matrix](../../README.md) that has more behind it than a
status: what the run showed, what it did not, and which report it produced. The matrix says the
state; these say why it is that state.

| Cell | Background |
|---|---|
| EVCC and SECC · ISO 15118-20 · Plug & Charge · eVDriveFlow | [Plug & Charge at eVDriveFlow: none implemented](edf-iso20-pnc.md) |
| EVCC · ISO 15118-2 · AC, EIM *and* TLS 1.2 (unilateral) · EVerest | [ISO 15118-2 AC over TLS 1.2, against EVerest](everest-iso2-ac-tls12.md) |
| EVCC · ISO 15118-2 · Contract provisioning · EVerest | [ISO 15118-2 contract provisioning against EVerest](everest-iso2-cert-update.md) |
| EVCC · ISO 15118-2 · DC, EIM · EVerest | [ISO 15118-2 DC EIM, against EVerest](everest-iso2-dc-eim.md) |
| EVCC · ISO 15118-2 · DC, EIM · tux-evse | [ISO 15118-2 DC against tux-evse, driving their responder](tux-iso2-dc-forward.md) |
| EVCC · ISO 15118-2 · Plug & Charge · EVerest | [ISO 15118-2 Plug & Charge against EVerest, and the charge](everest-iso2-pnc-charge.md) |
| EVCC · ISO 15118-2 · Renegotiation · EVerest | [ISO 15118-2 renegotiation against EVerest](everest-iso2-renegotiation.md) |
| EVCC · ISO 15118-2 · TLS 1.2 (unilateral) · Ours | [`trusted_ca_keys` on the wire, `[V2G2-651]`](ours-iso2-trusted-ca-keys.md) |
| EVCC · ISO 15118-20 MCS · MCS_BPT · EVerest | [MCS_BPT against EVerest](everest-mcs-bpt.md) |
| EVCC · ISO 15118-20 · AC · EVerest | [ISO 15118-20 AC against EVerest](everest-iso20-ac-forward.md) |
| EVCC · ISO 15118-20 · BPT · EVerest | [ISO 15118-20 BPT against EVerest](everest-iso20-bpt-forward.md) |
| EVCC · ISO 15118-20 · DC, Scheduled, EIM · eVDriveFlow | [ISO 15118-20 DC Scheduled against eVDriveFlow](edf-iso20-dc-scheduled.md) |
| EVCC · ISO 15118-20 · Multi-protocol SAP offer · EVerest | [The multi-protocol SAP offer against EVerest's IsoMux](everest-isomux-sap.md) |
| EVCC · ISO 15118-20 · Mutual TLS 1.3 · EVerest | [Mutual TLS 1.3 against EVerest, from Windows](everest-iso20-mtls-forward.md) |
| EVCC · ISO 15118-20 · Pause / Resume · EVerest | [ISO 15118-20 pause and resume against EVerest](everest-iso20-pause-resume.md) |
| EVCC · ISO 15118-20 · Pause / Resume · Josev — and SECC · Renegotiation · Josev | [Josev's empty -20 session context](josev-iso20-session-context.md) |
| EVCC · ISO 15118-20 · Signed tariffs · EVerest | [EVerest sends no price schedule, deliberately](everest-iso20-tariffs.md) |
| EVCC · ISO 15118-20 · WPT · ACDP · Ours | [WPT and ACDP: codec only, and independently judged](ours-wpt-acdp.md) |
| SECC · ISO 15118-2 and -20 · Plug & Charge, Mutual TLS 1.3 · Josev | [Josev's inbound Plug & Charge chains, anchored](josev-pnc-chains.md) |
| SECC · ISO 15118-2 · AC, EIM *and* Plug & Charge · EVerest | [The first ISO 15118-2 reverse session against EVerest](everest-iso2-reverse.md) |
| SECC · ISO 15118-2 · AC, EIM · tux-evse | [tux-evse's captured AC routes against our SECC](tux-iso2-ac-reverse.md) |
| SECC · ISO 15118-2 · DC, EIM · tux-evse | [tux-evse's captured Audi session against our SECC](tux-iso2-dc-reverse.md) |
| SECC · ISO 15118-2 · TLS 1.2 (unilateral) · tux-evse | [tux-evse's TLS configs offer neither prescribed suite](tux-iso2-tls.md) |
| SECC · ISO 15118-20 · AC *and* Mutual TLS 1.3 · EVerest | [ISO 15118-20 AC in reverse against EVerest](everest-iso20-ac-reverse.md) |
| SECC · ISO 15118-20 · BPT · EVerest | [EVerest's car picks our bidirectional services](everest-iso20-bpt-reverse.md) |
| SECC · ISO 15118-20 · BPT · eVDriveFlow | [eVDriveFlow's DC_BPT against our SECC](edf-iso20-bpt-reverse.md) |
| SECC · ISO 15118-20 · CertificateInstallation · EVerest | [EVerest's real OEM chain, and the wall behind it](everest-iso20-certinstall.md) |
| SECC · ISO 15118-20 · DC, Dynamic · EVerest | [ISO 15118-20 Dynamic in reverse against EVerest](everest-iso20-dynamic-reverse.md) |
| SECC · ISO 15118-20 · DC, Dynamic · Josev | [Josev's EV adopts the control mode we offer](josev-iso20-dynamic-reverse.md) |
| SECC · ISO 15118-20 · DC, Dynamic · eVDriveFlow | [eVDriveFlow's EV in our Dynamic charge loop](edf-iso20-dynamic-reverse.md) |
| SECC · ISO 15118-20 · Mutual TLS 1.3 · Josev | [Josev's EVCC presents its OEM chain as a TLS credential](josev-iso20-vehicle-cert.md) |
| SECC · ISO 15118-20 · Mutual TLS 1.3 · eVDriveFlow | [secp521r1 both ways, against eVDriveFlow](edf-iso20-mtls.md) |
| SECC · ISO 15118-20 · Plug & Charge · EVerest | [EVerest's signed AuthorizationReq, verified by us](everest-iso20-pnc-reverse.md) |
| SECC · ISO 15118-20 · Renegotiation · EVerest | [Renegotiation in reverse against EVerest's fork](everest-iso20-renegotiation.md) |
| SECC · ISO 15118-20 · SDP discovery *and* MCS · EVerest | [EVerest's EV discovers us, and picks MCS out of our catalogue](everest-sdp-and-mcs-reverse.md) |
| SECC · ISO 15118-20 · Signed tariffs · Josev | [A signed schedule consumed, and nothing that verifies it](josev-iso20-tariffs-reverse.md) |
| The counterparty columns, read together | [EVerest's EV is Josev, and what that costs a column](josev-is-everests-ev.md) |
