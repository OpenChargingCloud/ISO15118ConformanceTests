# Draft report to EVerest (`ext-switchev-iso15118`) and SwitchEV (`iso15118`) — the `iso-20` certificate branch emits ISO 15118-**2** key material

Status: **draft, not sent.** Found 2026-08-08 against `EVerest/ext-switchev-iso15118` @ **`26f7988`** (the
Josev fork `everest-core` **2026.02.1** builds and ships) and confirmed present in
`SwitchEV/iso15118` @ **`d645255`**. Post under your own name; see *Before sending* at the bottom.

**One issue, two places to file it.** The block below is in both projects, and the fork has diverged far
enough (557 lines vs 460) that a fix in one will not arrive in the other. The fork is where it matters
for anyone running EVerest today; upstream is where it started.

Evidence in this repository:
[`2026-08-08-everest-oem-provisioning-chain`](../interop-runs/2026-08-08-everest-oem-provisioning-chain/notes.md),
in particular the live consequence in [`secc-oemroot.log`](../interop-runs/2026-08-08-everest-oem-provisioning-chain/secc-oemroot.log).
The requirement side is summarised in [`normative-basis.md`](../normative-basis.md); this repository
holds clause identifiers and paraphrase, never ISO prose.

---

## Summary

`create_certs.sh` takes `-v iso-2 | iso-20` and branches on it. The `iso-2` branch is right, and
carries a comment explaining that OpenSSL spells `secp256r1` as `prime256v1`. The `iso-20` branch then
selects the same curve — and says so itself:

```bash
# iso15118/shared/pki/create_certs.sh:130-143   (ext-switchev-iso15118 @ 26f7988)
if [ "$VERSION" == "$ISO_2" ];
then
    ISO_FOLDER=iso15118_2
    SYMMETRIC_CIPHER=-aes-128-cbc
    SYMMETRIC_CIPHER_PKCS12=-aes128
    SHA=-sha256
    # Note: OpenSSL does not use the named curve 'secp256r1' (as stated in
    # ISO 15118-2) but the equivalent 'prime256v1'
    EC_CURVE=prime256v1
else
    ISO_FOLDER=iso15118_20
    SYMMETRIC_CIPHER=-aes-128-cbc  # TODO Check correct version for ISO 15118-20
    SYMMETRIC_CIPHER_PKCS12=-aes128  # TODO Check correct version for ISO 15118-20
    SHA=-sha256  # TODO Check correct version for ISO 15118-20
    EC_CURVE=prime256v1  # TODO Check correct version for ISO 15118-20
    # TODO: Also enable cipher suite TLS_CHACHA20_POLY1305_SHA256
fi
```

The same block is at `iso15118/shared/pki/create_certs.sh:121-134` in `SwitchEV/iso15118` @ `d645255`,
where the file's last functional change is dated 2022-10-12.

The result is measurable rather than theoretical. Every certificate the `iso-20` run writes into
`iso15118_20/certs/` is 256-bit — **all five branches**: `V2GRootCA` and `CPOSubCA1/2`, `MORootCA` and
its two Sub-CAs, the CPS pair, `OEMRootCA → OEMSubCA1 → OEMSubCA2 → OEMProvCert`, and the
`O=Pionix` vehicle chain. In a built `everest-core` 2026.02.1 tree the script is byte-identical
(md5 `3dc940f0a739`) in `_deps/josev-src`, in the installed `3rd_party/josev`, and in the copy used to
populate `dist/etc/everest/certs` — so this is the material every `-20` session actually runs on.

## What ISO 15118-20 asks for

- **Certificate keys:** secp521r1 with ECDSA (`[V2G20-2674]`), with Ed448/EdDSA additionally
  (`[V2G20-2319]`) and a configurable mechanism to switch between them (`[V2G20-2320]`).
- **Named groups**, Table 7: `secp521r1` and `x448`, both *shall* (`[V2G20-1634]`, `[V2G20-1637]`).
  Neither `secp256r1` nor `x25519` appears.
- **Signature algorithms**, Table 8: `ecdsa_secp521r1_sha512` and `ed448` — which puts the neighbouring
  `SHA=-sha256` in the same block in scope as well.

We are not reading a preference into the text: `-2` and `-20` prescribe different curves, the script
already has the branch that would distinguish them, and the `-20` side of it was never filled in.
Your own `TODO` says exactly that.

## Why this is worth fixing rather than left as a test-PKI quirk

**Two of the four `TODO`s in that block are cosmetic and two are not.** `SYMMETRIC_CIPHER` and
`SYMMETRIC_CIPHER_PKCS12` govern how the private key *files* are encrypted at rest; nothing in the -20
profile speaks to that, and we would leave them alone. `EC_CURVE` and `SHA` decide what goes on the
wire.

**The consequence we can demonstrate is not about TLS strength — it is that one -20 feature cannot
complete at all.** ISO 15118-20 contract provisioning wraps the issued contract private key by ECDH
against the EV's OEM provisioning key, and the schema's curve choice in `SignedInstallationData` has
exactly two members: `SECP521` and `X448`. A P-256 provisioning key fits neither. It is not that the
handshake is weaker than it should be; there is no code path, in any implementation, that can wrap a
contract key for that certificate.

We hit this live on 2026-08-08. `PyEvJosev` with `is_cert_install_needed: true` sent a correctly signed
`CertificateInstallationReq` carrying your OEM chain; our station verified the signature, validated the
chain to your `OEMRootCA`, issued the contract — and had to report:

```
CertificateInstallation: OEM DC=OEM, C=DE, O=EVerest, CN=OEMProvCert; digest OK, signature OK
(grammar=xmldsig-standalone), contract issued (OEM key not P-521 — blob undecryptable for EV);
OEM chain valid (anchored at DC=OEM, C=DE, O=EVerest, CN=OEMRootCA).
```

Everything your side did was right. The certificate simply cannot take part. Whoever implements the
EVCC half of `CertificateInstallationRes` — currently `raise NotImplementedError` in both projects —
will meet this the moment they try to test it against this PKI, and will spend the first day assuming
their own crypto is wrong.

The second consequence is milder but wider: every `-20` TLS session run against this material is
carried by -2-grade keys, so a station or car that pins the -20 profile exactly cannot connect, and the
failure surfaces as a handshake error that reads like the tester's misconfiguration.

## Suggested fix

The narrow version is the branch you already have, filled in:

```bash
else
    ISO_FOLDER=iso15118_20
    SHA=-sha512
    EC_CURVE=secp521r1
fi
```

The complete version is the switch `[V2G20-2320]` asks for — secp521r1 or Ed448, chosen by a flag —
since the standard names both and a test PKI is exactly where having both is useful. That is a larger
change and entirely your call.

**We would rather ask than assert on one point.** There is a real cost to this, and it may be why the
`TODO` has outlived several releases: Windows Schannel cannot use secp521r1 *certificates* for TLS at
all, so a test PKI that must work on every platform drifts toward P-256 almost by force. If the current
choice is deliberate for that reason, a line in the script saying so — instead of a `TODO` — would be
worth more than the change, because it tells the next tester that the deviation is known. What we would
argue against is leaving it as an open `TODO` that reads as an oversight while being relied on as a
default.

It is achievable: eVDriveFlow's `-20` PKI is secp521r1 throughout, and we completed a mutual TLS 1.3
session against it with `TLS_AES_256_GCM_SHA384` on 2026-08-07. So this is not a case of the standard
asking for something the field cannot supply.

## Also seen, secondary

- **Your fourth `TODO` in the same block is right, and the standard agrees with it.** Table 6 makes
  `TLS_AES_256_GCM_SHA384` *and* `TLS_CHACHA20_POLY1305_SHA256` mandatory for SECC (`[V2G20-2458]`) and
  EVCC (`[V2G20-2459]`), offered in table order (`[V2G20-1856]`, `[V2G20-1858]`). Worth noting that
  `TLS_AES_128_GCM_SHA256` — TLS 1.3's own mandatory-to-implement suite — is **not** in the -20 profile,
  which surprises most people the first time.
- **The `-20` cert-install path loads two DER files the `-20` run does not appear to install.**
  `iso15118/evcc/states/iso15118_20_states.py` reads `ca/oem/OEM_SUB_CA1.der` and
  `ca/oem/OEM_SUB_CA2.der`; `create_certs.sh` writes both, but the certificate store shipped in the
  built `dist/etc/everest/certs` had every other DER the `CertPath` enum names and not those two, so
  `is_cert_install_needed: true` fails before the session starts. We converted the PEMs in place and
  moved on. This looks like the install/copy step rather than the generator, and we have not chased it
  far enough to file it — **question rather than report**.

---

## Before sending

- [x] **Reproduce it yourself.** Done 2026-08-08: their generator, their PKI, their EV module. The
      curve was measured across all five branches of the generated set, not read off one certificate,
      and the functional consequence was observed in a live session rather than argued from the text.
- [x] **Establish whose script it is before filing.** It is Josev's, in two homes: byte-identical
      (md5 `3dc940f0a739`) in `ext-switchev-iso15118` @ `26f7988` and in what a built `everest-core`
      2026.02.1 installs, and present in the same shape in `SwitchEV/iso15118` @ `d645255`.
- [x] **Re-check both line ranges against the current HEAD of each project — done 2026-08-11, and the
      pin *is* the HEAD in both.** `SwitchEV/iso15118` `master` = `d645255` (2026-05-19) and
      `EVerest/ext-switchev-iso15118` `everest` = `26f7988` (2026-05-04) are exactly what this suite
      pinned; the `EC_CURVE=prime256v1` line is still inside the `iso-20` branch in both trees. Both
      filings are current.
- [ ] **File in `ext-switchev-iso15118` first, then upstream**, cross-referencing. Two filings, because
      the two trees have diverged and one merge will not carry to the other.
- [ ] **Ask before asserting.** Open with "is `prime256v1` for `iso-20` deliberate, given Schannel?"
      rather than "your test PKI is non-conformant". Their `TODO` suggests it is not deliberate, but a
      `TODO` is not a decision record either way.
- [ ] **Lead with the provisioning consequence, not the conformance table.** "Contract provisioning
      cannot complete with this key material, for reasons no implementation can work around" is a fact
      about their own feature; "Table 7 says secp521r1" is a fact about a document they may not have to
      hand.
- [ ] **Do not bundle the missing DER files with it.** That is a different owner and probably a
      different repository; it is in *Also seen* here so it is not lost, not so it is filed together.
- [ ] **Offer the two-line patch only if they want it.** The choice between the narrow fix and the
      `[V2G20-2320]` switch is theirs, and the second one touches how the whole script is invoked.
