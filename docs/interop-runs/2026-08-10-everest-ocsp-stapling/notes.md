# 2026-08-10 — EVerest never staples an OCSP response, and the reason is one missing line

A warning in their own boot log, chased to the end. `EvseV2G` **asks** libevse-security for the OCSP
data belonging to its certificate chain; libevse-security **collects** it; and the type conversion
between the two **drops the field**, so the TLS server is handed an empty list, refuses to cache
anything, and never staples. Both ISO 15118-2 `[V2G2-871]` and ISO 15118-20 `[V2G20-2388]` require the
stapling that this quietly disables.

Not a wire finding — no session was needed. The station says it at boot, and the MQTT message that
causes it can be read directly.

| | |
|---|---|
| Counterparty | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12b8`), source build in WSL2 |
| Their modules | `EvseV2G` (TLS server), `EvseSecurity` (libevse-security), `lib/everest/tls` |
| Config | `config-dc2-ours.yaml`, `tls_security: allow`, their own unmodified test PKI |
| Ours | nothing — the measurement is their MQTT traffic and their log |
| Outcome | **`ocsp` absent from a reply that requested it; no OCSP entry cached; no staple possible** |
| Control | OCSP responses installed through **their own** `update_ocsp_cache`, station restarted — **nothing changes** |
| Artifacts | [`their-evse-security-mqtt.log`](their-evse-security-mqtt.log), [`their-charger.log`](their-charger.log), [`their-cert-layout.txt`](their-cert-layout.txt), and for the control [`their-ocsp-request-data.json`](their-ocsp-request-data.json), [`provisioned-cache.txt`](provisioned-cache.txt), [`their-charger.after-provisioning.log`](their-charger.after-provisioning.log), [`their-evse-security-mqtt.after-provisioning.log`](their-evse-security-mqtt.after-provisioning.log) |
| Filed | [`everest-evse-security-ocsp-dropped.md`](../../reports/everest-evse-security-ocsp-dropped.md) — the twenty-fourth |

## What the station says about itself

At boot, on a config with no TLS session in sight:

```
[INFO] iso15118_charge :: TLS server on eth0 is listening on port [fe80::…%2]:64109
[WARN] iso15118_charge :: <n> certificates != <n> OCSP responses
```

The `<n>` are literal — `tls.cpp:1039` passes that string through unsubstituted, so the warning never
says which two numbers disagreed. That is why it reads as noise, and it is not.

## The two numbers

**Three, and zero.** The chain file holds three certificates
([`their-cert-layout.txt`](their-cert-layout.txt)):

```
$ grep -c "BEGIN CERTIFICATE" /etc/everest/certs/client/cso/CPO_CERT_CHAIN.pem
3
```

and the reply that `EvseV2G` got when it asked for the chain **and its OCSP data** carries no `ocsp`
member at all ([`their-evse-security-mqtt.log`](their-evse-security-mqtt.log), the response to
`everest/modules/evse_security/impl/main/cmd/get_all_valid_certificates_info`):

```json
{"retval": {"info": [{
    "certificate": "…/client/cso/CPO_CERT_CHAIN.pem",
    "certificate_count": 3,
    "certificate_root": "-----BEGIN CERTIFICATE-----\nMIIBxTCC…",
    "certificate_single": "…/client/cso/SECC_LEAF.pem",
    "key": "…/client/cso/SECC_LEAF.key",
    "password": "123456"
}], "status": "Accepted"}}
```

`EvseV2G` asked with `include_ocsp = true` — `tls_connection.cpp:298`,
`call_get_all_valid_certificates_info(V2G, PEM, true)` — and the field the interface documents as
*"Certificate related OCSP data, if requested"* is simply not in the answer.

**This is the whole finding, and it is why no session was needed.** libevse-security fills that list
unconditionally when `include_ocsp` is set (`evse_security.cpp:1586-1604`, `:1633-1634`): one entry per
certificate in the chain file, `ocsp_path` unset where no response is cached, and an empty-hash entry
where the hierarchy cannot place the certificate — the comment says *"Always add to preserve file
order"*. A correct reply here would therefore have been a **three-element array**, not a missing key.
So the list was built and then lost between the library and the module, whatever is on disk.

## Where it is lost

`lib/everest/conversions/evse_security/src/conversions.cpp`. The two directions are eleven lines apart
and disagree:

```cpp
// :192  MQTT type → internal type
evse_security::CertificateInfo from_everest(types::evse_security::CertificateInfo other) {
    …
    if (other.ocsp.has_value()) {                    // :199
        for (auto& ocsp_data : other.ocsp.value()) {
            lhs.ocsp.push_back(from_everest(ocsp_data));
        }
    }
    …
}

// :429  internal type → MQTT type
types::evse_security::CertificateInfo to_everest(evse_security::CertificateInfo other) {
    types::evse_security::CertificateInfo lhs;
    lhs.key = other.key;
    lhs.certificate_root = other.certificate_root;
    lhs.certificate = other.certificate;
    lhs.certificate_single = other.certificate_single;
    lhs.password = other.password;
    lhs.certificate_count = other.certificate_count;
    return lhs;                                      // ← ocsp is never copied
}
```

Both types have the member: `evse_types.hpp:193` (`std::vector<CertificateOCSP> ocsp`) and the
generated `types/evse_security.hpp:1056`
(`std::optional<std::vector<CertificateOCSP>> ocsp`). The optional stays unset, so `to_json` omits the
key, which is exactly what the capture shows.

## What follows, in their code

1. `tls_connection.cpp:318` — `if (chain.ocsp)` is false, so `ref.ocsp_response_files` stays **empty**.
2. `tls.cpp:1026` — `certs.size() == i.ocsp_response_files.size()` is `3 == 0`, false.
3. `tls.cpp:1039` — the warning, and the `entries` list is left untouched: **not one OCSP entry is
   cached**, not even for certificates that do have a response on disk. It is all-or-nothing.
4. `tls.cpp:1084` / `:1089` — `m_cache.load(entries)` loads that empty list, and it is the only writer
   of the cache.
5. `status_request.cpp:168` / `:250` — every handshake lookup misses (`OcspCache::lookup: not in
   cache: …`, which our TLS runs recorded), and `:269` — *"don't include the extension when there are
   no OCSP responses"*.

So the station answers a `status_request` with nothing, always, on every deployment, whatever the
operator provisions.

## Why it matters, and to which protocol

Both, and the -2 side is the sharper one.

- **ISO 15118-2.** `[V2G2-871]`: a station outside a private environment owes the EV its chain, and —
  once the EV has asked for OCSP data — one response per certificate it puts into the handshake, in the
  RFC 6961 form, which is the `status_request_v2` `lib/everest/tls` implements. `[V2G2-873]` says what a
  conformant EV does when it asked and nothing came back: for a chain rooted at a **V2G root** it
  **closes the connection**. `[V2G2-872]` is the carve-out — a station in a *private* environment sends
  its private root instead — and `[V2G2-875]` puts the EV under a duty to verify those responses and
  abandon the setup when that fails. Carries the `-2` document caveat in
  [`normative-basis.md`](../../normative-basis.md): the text to hand is the 2022 DIS revision.
- **ISO 15118-20.** `[V2G20-2372]` removes the "if" — the EV is required to carry `status_request` in
  its `ClientHello`, so the question is always asked. `[V2G20-2388]` then puts a public station under a
  duty to answer with one response per certificate of the chain it presents, `[V2G20-2391]` extends it
  to a private station supporting PnC, and `[V2G20-1021]` caps reuse at a week. This reaches `EvseV2G`
  through `IsoMux`, which terminates TLS itself for both protocols — the finding behind
  [the nineteenth filing](../../reports/everest-isomux-iso20-over-tls12.md).

The practical consequence, stated once: **a strictly conformant ISO 15118-2 EVCC that requests OCSP and
meets a V2G-rooted EVerest station closes the connection**, so TLS — and therefore Plug & Charge — is
unreachable for it. Every EV that has met this station so far, ours included, does not enforce
`[V2G2-873]`, which is exactly why the defect has survived.

## Two things this is not

- **Not a missing feature.** The stapling is implemented, tested, and wired: `status_request` and
  `status_request_v2` in `lib/everest/tls/extensions/`, with unit tests that populate
  `ocsp_response_files` by hand (`tests/tls_connection_test.cpp:42`). The tests pass because they set
  the field the module boundary drops — the gap is precisely at the seam neither side tests.
- **Not caused by our rig having no OCSP data.** Argued from the JSON first, then measured. The
  argument: the key is *absent*, not `[]`, and an optional set to an empty vector would have serialized
  as `"ocsp": []` — so nothing ever set it, whatever was on disk. The measurement is below.

## The control: provision OCSP responses through their own API, change nothing

Their station has an interface for exactly this, and an OCPP-connected one uses it on a timer. So the
data went in the way a real deployment's would, with nothing patched and nothing hand-placed.

**Step 1 — ask them which certificates want a response.** `get_v2g_ocsp_request_data`, over MQTT
([`their-ocsp-request-data.json`](their-ocsp-request-data.json)) — two entries, because their own test
PKI's `SECCCert` carries no responder URL and their code says so out loud:

```
[WARN] evse_security :: Could not retrieve OCSP Responder URL from certificate
[WARN] evse_security :: When generating an OCSP request, could not find responder URL for certificate: SECCCert
```

**Step 2 — hand each one a response.** `update_ocsp_cache(certificate_hash_data, ocsp_response)` for
both, with a placeholder string as the response: their handler writes it to the cache file without
parsing it (`evse_security.cpp:1075-1135`), so no OCSP responder was needed and no real response was
forged.

```
[INFO] evse_security :: Updating OCSP cache
[INFO] evse_security :: Updating OCSP cache
```

**Step 3 — their code writes the cache** ([`provisioned-cache.txt`](provisioned-cache.txt)). The
directory did not exist before; they created it, named the files, and stored the hash data themselves:

```
client/cso/ocsp/M08_D10_Y2026_H13_M25_S33_i1_r530732214_ocsp.der
client/cso/ocsp/M08_D10_Y2026_H13_M25_S33_i1_r530732214_ocsp.hash
client/cso/ocsp/M08_D10_Y2026_H13_M25_S33_i2_r615549393_ocsp.der
client/cso/ocsp/M08_D10_Y2026_H13_M25_S33_i2_r615549393_ocsp.hash
```

**Step 4 — restart the station and look again.** Nothing moves
([`their-charger.after-provisioning.log`](their-charger.after-provisioning.log),
[`their-evse-security-mqtt.after-provisioning.log`](their-evse-security-mqtt.after-provisioning.log)):

```
[WARN] iso15118_charge :: <n> certificates != <n> OCSP responses
```

and the reply to `get_all_valid_certificates_info` still carries `certificate_count: 3` and **no `ocsp`
key**. Two OCSP responses sit in the cache their own API put them in, and not one of them reaches the
TLS server.

That closes the last reading-rather-than-observation in this finding: the drop is unconditional,
measured, on their own data path.

*Their test PKI was handed back as found — the `ocsp/` directory is ours and was removed afterwards.*

## A second, different cause with the same symptom

`IsoMux` also terminates TLS, and its `tls_connection.cpp:291` asks
`call_get_leaf_certificate_info(V2G, PEM, **false**)` — `include_ocsp = false`. So it never requests the
data at all, its `ocsp_response_files` is empty for a different reason, and fixing the conversion
alone leaves the `IsoMux` deployment — the one that serves both protocols — unstapled. Recorded in the
report so that a fix is not declared complete too early.

And `Evse15118D20` does not staple either, because `libiso15118` contains no OCSP handling of any kind:
the only matches in that tree are `authorityInfoAccess` lines in test certificate configs. That is a
missing feature rather than a lost field, and it is not part of the filing.

## How it was measured

```bash
/usr/sbin/mosquitto -p 1883 &
mosquitto_sub -v -t 'everest/modules/evse_security/#' > security.log &     # before the manager
~/everest/dist/bin/manager --config ~/everest/configs-ours/config-dc2-ours.yaml
```

The command and its reply are the first two lines of the capture. Nothing else is needed — no EV, no
session, no TLS handshake.
