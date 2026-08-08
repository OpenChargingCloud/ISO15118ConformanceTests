# The OEM provisioning chain, against a foreign car — 2026-08-08

The one validator path still judged only by material this project minted itself. The chain-validation
run earlier the same day closed the other two — TLS, and the contract chain at message level — and
ended by naming this one as open, because the peer used there does not request contract provisioning
in its default configuration. It does in another one.

**Counterparty:** EVerest 2026.02.1, native WSL2 build. Their car module `PyEvJosev` (Josev-derived)
with `is_cert_install_needed: true` — a first-class key in its own manifest — on the reverse -20 config
that puts their station on `lo` and their car on `eth0`, so the car discovers *our* SDP.

**Their material, untouched:** `OEMRootCA → OEMSubCA1 → OEMSubCA2 → OEMProvCert`, all
`O=EVerest, C=DE, DC=OEM`, leaf P-256. This is a **third self-signed root** in their test PKI, separate
from the V2G root the earlier run's TLS used and from the MO root its contract chain was anchored at.

**Ours:** `WWCP_ISO15118_SECC --listen 55000 --protocol 20 --mode dc --sdp --interface eth0
--trust-roots …`, plain TCP. Contract provisioning is offered by default (`OfferPlugAndCharge`), so the
station announces `CertificateInstallationService: true` and their car takes it up.

## Three runs, one question each

The signature verified identically in all three — `digest OK, signature OK
(grammar=xmldsig-standalone)`, the Josev form rather than the combined grammar our own EVCC uses. Only
the chain verdict moves:

| `--trust-roots` | Verdict |
|---|---|
| their OEM root alone | `OEM chain valid (anchored at DC=OEM, C=DE, O=EVerest, CN=OEMRootCA)` |
| both OEM Sub-CAs, **no** root | `OEM chain REJECTED — unable to get local issuer certificate` |
| their **V2G** root — a real root, wrong branch | `OEM chain REJECTED — unable to get local issuer certificate` |

`openssl verify` agrees on all three, run against the same four files.

**Row 1 says their car ships its Sub-CAs.** The two intermediates were not in the trust store, so they
can only have arrived in the message — `load_cert_chain(leaf, sub_ca2, sub_ca1)` in their
`iso15118_20_states.py`, and the fragment our station digested is the whole chain. That is now three
observations of the same vendor: their EV sends intermediates for its contract chain and for its OEM
chain, their station sends a bare leaf at TLS. Root-alone is sufficient against their car and
insufficient against their station.

**Row 2 repeats at message level what the earlier run measured at TLS**, and it was worth repeating,
because the two go through different code: `X509Chain` with `CustomRootTrust` refuses to treat a
non-self-signed certificate in the trust store as an anchor, whichever entry point loaded it. Two
Sub-CAs that can build the whole path from the leaf are still not a root.

**Row 3 is the interesting one, and it is not the same negative control as before.** Their request
*names a root of its own*: `RootCertificateIDList`, built in their EVCC from `CertPath.V2G_ROOT_DER` —
issuer and serial of the **V2G** root. A station that read that field as "here is my anchor" would
validate an OEM chain against a V2G root and reject every legitimate provisioning request it ever saw.
The field is the car telling the backend which roots *it* can verify, for the contract it is about to
be given; it says nothing about the chain the car is presenting. Row 3 is that mistake made
deliberately, and the rejection is correct.

So the validator's third entry point is now foreign-checked, each against a different root of a
counterparty's own making:

| Entry point | Foreign anchor it was measured against |
|---|---|
| TLS peer chain | their **V2G** root |
| contract chain, `-20 AuthorizationReq` | their **MO** root |
| OEM provisioning chain, `-20 CertificateInstallationReq` | their **OEM** root |

## What their side did, and where it stopped

Their EVCC decoded our signed `CertificateInstallationRes` and then hit its own wall: Josev's
CertificateInstallation *response* handler is `raise NotImplementedError`, in the EVerest fork as in
SwitchEV's. The session ends there, our station reports the closed connection, and their manager
retries SDP once and shuts down high-level communication. Identical to the SwitchEV run of 2026-07-22 —
the second independent stack to send a real request and the second to be unable to consume the answer.
Nothing here changes that cell: it stays `◐`, now with two counterparties behind it.

One consequence worth stating: the contract key our station wraps is **still** only round-trip-tested
by us. Their OEM leaf is P-256, which cannot join the secp521r1 ECDH `-20` prescribes, so the blob is
well-formed and undecryptable for that car — `EncryptedForOem=false`, exactly as with SwitchEV's P-256
provisioning cert. **The chain is now foreign-checked; the provisioning crypto is not, and no
counterparty is currently able to check it.**

## Their `-20` PKI is P-256 throughout — measured, not inferred

[`open-work.md`](../../open-work.md) holds a seventeenth filing back pending *"confirm what their script
actually emits"*. For this counterparty it is now measured rather than read off one certificate: every
certificate their `pki/create_certs.sh` writes into `iso15118_20/certs/` is 256-bit — all five branches,
roots, Sub-CAs and leaves alike (V2G, CPO, CPS, MO, OEM, and the `O=Pionix` vehicle branch). The script
itself marks the spot: it selects `prime256v1` for ISO 15118-2 with a comment explaining that OpenSSL
spells `secp256r1` that way, and then selects `prime256v1` again for ISO 15118-20 with
`# TODO Check correct version for ISO 15118-20` beside it. The -20 profile asks for secp521r1 with Ed448
alongside. This is the same finding already recorded on
[EVerest's page](../../everest-cross-validation.md), now with the whole set behind it instead of one leaf.

## Rig notes

**The prerequisite that bites first.** Their -20 cert-install path loads `ca/oem/OEM_SUB_CA1.der` and
`ca/oem/OEM_SUB_CA2.der`; this dist's certificate store had every other DER file Josev's `CertPath`
enum names and not those two. Their own `pki/create_certs.sh` emits both (and they are present under
`pki/iso15118_20/certs/ca/oem/`), so this is the install-time copy into `dist/etc/everest/certs` and not
a hole in their generator. Converting the two PEMs in place is enough, and additive.

**Their manager's log carries no per-message Josev output** at this level — only the module lifecycle
and, at the end, the `SDPFailedError` traceback from the retry after the session died. The SwitchEV
Docker rig logged every state transition; this one does not, so the evidence for what their car sent is
on our side: a signature that verifies against `CN=OEMProvCert, O=EVerest` under the standalone-xmldsig
grammar, over a chain fragment containing both of their Sub-CAs.

Script: [`oem-certinstall-chain.sh`](../../../tools/interop-everest/oem-certinstall-chain.sh). Logs:
[`secc-oemroot.log`](secc-oemroot.log), [`secc-oemsubs.log`](secc-oemsubs.log),
[`secc-v2groot.log`](secc-v2groot.log), [`ev-oemroot.log`](ev-oemroot.log).

## Next

- The **contract-key wrap** is what is left of "self-checked only" in `-20` provisioning. It needs a
  counterparty that both requests installation *and* consumes the response; neither Josev fork does, and
  no other stack here implements `-20` provisioning at all. This is a structural gap, not a backlog item.
- Their P-256 `-20` PKI now has enough behind it to write the seventeenth filing, if it is worth
  sending: their generator's own `TODO` is the strongest evidence such a report can have.
