#!/usr/bin/env python3
"""Has anybody fixed it since we wrote it down?

Every draft in docs/reports/ is written against a pinned commit. Before one is filed it is
worth knowing whether the defect still exists on the project's current default branch --
filing a fixed bug is the other fast way to have a real finding dismissed.

This fetches, per finding, the cited file at `main` and tests the *marker* that makes the
defect true, rather than the line number. A marker that is gone is not proof of a fix; it
is the signal to go and read.

Network only, no checkout needed:

    python3 check_upstream.py            # everest-core, where fourteen of the drafts go
    python3 check_upstream.py --lib      # standalone EVerest/libiso15118, which has diverged

Note on the marker set: it is deliberately over-specific. A marker broad enough to survive
a refactor (a function name, a struct name) reports "still present" for code that has been
rewritten around it -- which is how the AC-namespace fix was nearly missed here.
"""
import argparse
import sys
import urllib.error
import urllib.request

CORE = "https://raw.githubusercontent.com/EVerest/everest-core/main/"
LIB = "https://raw.githubusercontent.com/EVerest/libiso15118/main/"

# (report, path under the tree, marker, what it is[, invert])
# The marker normally means "the defect is still there". Where the defective line survives a
# fix unchanged, the marker instead names the *fix* and the entry is marked invert=True.
CORE_CHECKS = [
    ("everest-d20-sequence-timeout", "lib/everest/iso15118/include/iso15118/d20/timeout.hpp",
     "constexpr auto TIMEOUT_SEQUENCE = 1000 * 60;", "single 60 s constant"),
    ("everest-evsev2g-session-id-zero", "modules/EVSE/EvseV2G/iso_server.cpp",
     "ev_v2g_data.received_session_id != 0", "the != 0 conjunct in the [V2G2-460] check"),
    ("everest-evsev2g-certificate-update", "modules/EVSE/EvseV2G/iso_server.cpp",
     "// TODO: implement CertificateUpdate handling", "unimplemented handler returning NO_EVENT"),
    ("everest-evsev2g-renegotiation-cablecheck", "modules/EVSE/EvseV2G/iso_server.cpp",
     "iso_dc_state_id::WAIT_FOR_CABLECHECK", "renegotiation lands in WAIT_FOR_CABLECHECK"),
    ("everest-evsev2g-metering-chain (back)", "modules/EVSE/EvseV2G/iso_server.cpp",
     "static void publish_iso_metering_receipt_req", "empty MeteringReceipt publisher"),
    ("everest-evsev2g-metering-chain (out)", "modules/EVSE/EvseV2G/charger/ISO15118_chargerImpl.cpp",
     "v2g_ctx->meter_info.meter_reading = powermeter.energy_Wh_import.total;",
     "unsigned total, not the signed sibling"),
    ("everest-evsev2g-paymentdetails-crash", "modules/EVSE/EvseV2G/iso_server.cpp",
     "getEmaidFromContractCert", "cert used before the parse result is checked"),
    ("everest-evsev2g-session-log-responses", "modules/EVSE/EvseV2G/v2g_server.cpp",
     "i < conn->payload_len + V2GTP_HEADER_LENGTH", "response logged with the request's length"),
    ("everest-evse-security-ocsp-dropped", "lib/everest/conversions/evse_security/src/conversions.cpp",
     "to_everest(evse_security::CertificateInfo other)", "the conversion that drops ocsp"),
    ("everest-isomux §1/§2", "modules/EVSE/IsoMux/connection/tls_connection.cpp",
     'config.ciphersuites = "";', "TLS capped at 1.2 in front of a -20 backend"),
    ("everest-isomux §3", "modules/EVSE/IsoMux/v2g_server.cpp",
     'dlog(DLOG_LEVEL_ERROR, "v2g_incoming_v2gtp() failed");',
     "failed header read logged, then used anyway"),
    # libiso15118, vendored into everest-core -- the three that have diverged from the standalone repo
    ("everest-iso20-ac-contactor-latch", "lib/everest/iso15118/src/iso15118/d20/state/power_delivery.cpp",
     "ac_connector_closed = control_data;", "pointer assigned to bool (no dereference)"),
    ("everest-loop-shutdown", "lib/everest/iso15118/src/iso15118/io/connection_ssl.cpp",
     'log_and_raise_openssl_error("Failed to SSL_accept()', "handshake failure throws out of the poll loop"),
    ("everest-d20-ac-namespace", "lib/everest/iso15118/src/iso15118/d20/state/supported_app_protocol.cpp",
     "and modes.dc", "no capability filter before the priority map", True),
    ("everest-d20-meter-info", "lib/everest/iso15118/src/iso15118/d20/state/dc_charge_loop.cpp",
     "// TODO(sl): Setting EvseStatus, MeterInfo, Receipt", "meter_info never set on the response"),
    ("everest-d20-trust-anchor", "lib/everest/iso15118/src/iso15118/io/connection_ssl.cpp",
     "path_certificate_mo_root", "MO root as the vehicle-certificate anchor"),
    ("everest-d20-rng-entropy", "lib/everest/iso15118/src/iso15118/d20/state/authorization_setup.cpp",
     "std::mt19937 generator(rd());", "mt19937 seeded from one 32-bit draw"),
    ("everest-d20-ocsp-absent", "modules/EVSE/Evse15118D20/charger/ISO15118_chargerImpl.cpp",
     "EncodingFormat::PEM, false)", "include_ocsp still false"),
    # everest-core moved this one out of libiso15118 into the TLS library; the mechanism is unchanged,
    # so the marker follows it rather than the draft's connection_ssl.cpp line numbers.
    ("everest-d20-client-auth §1", "lib/everest/tls/src/tls.cpp",
     "if (not m_verify_client_on_tls13) {", "client verification decided by the version the EV offered"),
    ("everest-d20-client-auth §2", "lib/everest/tls/src/tls.cpp",
     "SSL_CTX_set1_sigalgs", "no Table 8 signature-algorithm list", True),
]

# The standalone library carries its own copy of the libiso15118 paths, without the lib/everest prefix.
LIB_PREFIX = "lib/everest/iso15118/"

cache = {}


def fetch(url):
    if url in cache:
        return cache[url]
    try:
        req = urllib.request.Request(url, headers={"User-Agent": "reports-audit"})
        with urllib.request.urlopen(req, timeout=30) as r:
            body = r.read().decode("utf-8", "replace")
    except urllib.error.HTTPError as e:
        body = f"<<HTTP {e.code}>>"
    except Exception as e:  # noqa: BLE001 - a network failure must not look like a fix
        body = f"<<ERROR {e}>>"
    cache[url] = body
    return body


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--lib", action="store_true",
                    help="check EVerest/libiso15118 instead of everest-core (libiso15118 findings only)")
    args = ap.parse_args()

    base, checks = CORE, CORE_CHECKS
    if args.lib:
        base = LIB
        checks = [(r, p[len(LIB_PREFIX):], m, n, *rest) for r, p, m, n, *rest in CORE_CHECKS
                  if p.startswith(LIB_PREFIX)]

    present = gone = err = 0
    for report, path, marker, note, *rest in checks:
        invert = bool(rest and rest[0])
        body = fetch(base + path)
        if body.startswith("<<"):
            err += 1
            print(f"  !!  {report:42s} {body}  ({path})")
            continue
        still_there = (marker not in body) if invert else (marker in body)
        if still_there:
            present += 1
            print(f"  =   {report:42s} STILL PRESENT   {note}")
        else:
            gone += 1
            print(f"  *   {report:42s} LOOK AGAIN      {note}")
            print(f"        -> {path}  :: {'fix marker found' if invert else 'defect marker gone'}: {marker!r}")

    print(f"\n{present} still present, {gone} changed, {err} could not be fetched", file=sys.stderr)
    return 1 if err else 0


if __name__ == "__main__":
    sys.exit(main())
