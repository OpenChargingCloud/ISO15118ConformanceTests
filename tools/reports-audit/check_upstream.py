#!/usr/bin/env python3
"""Has anybody fixed it since we wrote it down -- and in which tree?

Every draft in docs/reports/ is written against a pinned commit. Before one is filed it is
worth knowing whether the defect still exists -- filing a fixed bug is the other fast way to
have a real finding dismissed. And "still exists" has two answers that differ: on the
project's default branch, and in the newest release anyone can install. A fix on `main` is
not a fix anyone has.

This fetches, per finding, the cited file at a ref and tests the *marker* that makes the
defect true, rather than the line number. A marker that is gone is not proof of a fix; it
is the signal to go and read.

Network only, no checkout needed:

    python3 check_upstream.py                     # EVerest/everest-core @ main
    python3 check_upstream.py --ref 2026.02.2     # ... @ a release tag -- the installable tree
    python3 check_upstream.py --lib               # EVerest/libiso15118 @ main -- HISTORY, NOT STATUS

Run it twice, against `main` and against the newest release, and read the two columns
together. On 2026-11-05 that pair turned "ten fixed" into "one fixed in a release".

The standalone libiso15118 repository is NOT maintained; the live code is everest-core's vendored
lib/everest/iso15118/. --lib shows how far behind the mirror is and nothing else -- a STILL PRESENT
from it is not evidence that anything is live. On 2026-08-11 it produced the opposite conclusion:
three findings fixed in everest-core still read as defects there.

Two notes on the marker set, both learned by it being wrong:

1. It is deliberately over-specific. A marker broad enough to survive a refactor (a function name,
   a struct name) reports "still present" for code that has been rewritten around it -- which is
   how the AC-namespace fix was nearly missed here.
2. A marker must not survive its own fix. On 2026-11-05 three entries reported STILL PRESENT on
   `main` for defects fixed there: the PaymentDetails crash (the fix *reorders* two lines that both
   still exist), the OCSP conversion (the marker was the function's own signature) and IsoMux
   section 3 (the log line survives; what changed is the `return` after it). An ordering bug needs
   an ordering check, and those entries now carry a predicate instead of a substring.
"""
import argparse
import re
import sys
import urllib.error
import urllib.request

RAW = "https://raw.githubusercontent.com/{repo}/{ref}/"
CORE_REPO = "EVerest/everest-core"
LIB_REPO = "EVerest/libiso15118"


# ---------------------------------------------------------------------------------------------
# Predicates -- for defects a substring cannot see. Each takes `get(path) -> str` and returns
# (present, detail): True = defect present, False = fixed, None = could not decide.
# ---------------------------------------------------------------------------------------------

def _function_body(src, signature):
    """The text from `signature` to the first closing brace in column 0."""
    i = src.find(signature)
    if i < 0:
        return None
    j = src.find("\n}", i)
    return src[i:j if j >= 0 else len(src)]


def paymentdetails_order(get):
    src = get("modules/EVSE/EvseV2G/iso_server.cpp")
    if src.startswith("<<"):
        return None, src
    body = _function_body(src, "static enum v2g_event handle_iso_payment_details(")
    if body is None:
        return None, "handle_iso_payment_details not found"
    p = body.find("parse_contract_certificate(")
    if p < 0:
        return None, "parse_contract_certificate not found"
    use = body.find("getEmaidFromContractCert(", p)
    guard = body.find("if (err != 0)", p)
    if use < 0 or guard < 0:
        return None, "use or guard not found after the parse"
    return use < guard, ("certificate used before the parse result is checked" if use < guard
                         else "parse result checked before the certificate is used")


def ocsp_conversion(get):
    src = get("lib/everest/conversions/evse_security/src/conversions.cpp")
    if src.startswith("<<"):
        return None, src
    body = _function_body(src, "to_everest(evse_security::CertificateInfo other)")
    if body is None:
        return None, "to_everest(CertificateInfo) not found"
    return ("ocsp" not in body), ("ocsp not copied" if "ocsp" not in body else "ocsp copied")


def isomux_failed_read(get):
    src = get("modules/EVSE/IsoMux/v2g_server.cpp")
    if src.startswith("<<"):
        return None, src
    m = re.search(r'dlog\(DLOG_LEVEL_ERROR, "v2g_incoming_v2gtp\(\) failed"\);\s*\n\s*(\S[^\n]*)', src)
    if not m:
        return None, "the failure log line not found"
    acted = m.group(1).startswith("return")
    return (not acted), ("caller carries on after the failure" if not acted
                         else "caller returns on the failure")


def client_auth_by_version(get):
    # The mechanism moved between trees: on main it is tls.cpp's m_verify_client_on_tls13 dispatch,
    # in the 2026.02 line it is libiso15118's own client_hello_cb. Present if either is there.
    tls = get("lib/everest/tls/src/tls.cpp")
    if not tls.startswith("<<") and "if (not m_verify_client_on_tls13) {" in tls:
        return True, "tls.cpp: verification upgraded only when the EV offers TLS 1.3"
    ssl = get("lib/everest/iso15118/src/iso15118/io/connection_ssl.cpp")
    if not ssl.startswith("<<") and "Client supports TLS1.3: Change verify mode" in ssl:
        return True, "connection_ssl.cpp client_hello_cb: verification upgraded only for TLS 1.3"
    if tls.startswith("<<") and ssl.startswith("<<"):
        return None, "neither tls.cpp nor connection_ssl.cpp could be fetched"
    return False, "neither the tls.cpp dispatch nor the connection_ssl.cpp callback found"


def certificate_update_answer(get):
    # The handler is a TODO in every tree. What decides the finding is whether anything writes the
    # response's mandatory members: the generated init_iso2_*ResType functions reset optional
    # *_isUsed flags only, so an init call alone does not -- which is what main added.
    src = get("modules/EVSE/EvseV2G/iso_server.cpp")
    if src.startswith("<<"):
        return None, src
    body = _function_body(src, "static enum v2g_event handle_iso_certificate_update(")
    if body is None:
        return None, "handle_iso_certificate_update not found"
    writes = "ResponseCode" in body
    return (not writes), ("handler writes no ResponseCode; the union slot answers"
                          if not writes else "handler sets ResponseCode")


def ac_namespace_gate(get):
    # main gates the -20 namespaces on the configured energy modes in session/secc_sap.cpp;
    # the 2026.02 line has no such file and accepts both namespaces unconditionally.
    sap = get("lib/everest/iso15118/src/iso15118/session/secc_sap.cpp")
    if not sap.startswith("<<"):
        return ("energy_modes" not in sap), ("secc_sap.cpp gates on energy_modes" if "energy_modes" in sap
                                            else "secc_sap.cpp without an energy-mode gate")
    old = get("lib/everest/iso15118/src/iso15118/d20/state/supported_app_protocol.cpp")
    if old.startswith("<<"):
        return None, "neither secc_sap.cpp nor supported_app_protocol.cpp found"
    gated = "energy_mode" in old or "modes.dc" in old
    return (not gated), ("no capability filter before the priority map" if not gated
                         else "a capability filter is present")


# (report, path under the tree, marker, what it is[, invert])  -- or --
# (report, None, predicate, what it is)
# The marker normally means "the defect is still there". Where the defective line survives a
# fix unchanged, the marker instead names the *fix* and the entry is marked invert=True.
CORE_CHECKS = [
    ("everest-d20-sequence-timeout", "lib/everest/iso15118/include/iso15118/d20/timeout.hpp",
     "constexpr auto TIMEOUT_SEQUENCE = 1000 * 60;", "single 60 s constant"),
    ("everest-evsev2g-session-id-zero", "modules/EVSE/EvseV2G/iso_server.cpp",
     "ev_v2g_data.received_session_id != 0", "the != 0 conjunct in the [V2G2-460] check"),
    ("everest-evsev2g-certificate-update", None, certificate_update_answer,
     "unimplemented handler; ResponseCode left to the union slot"),
    ("everest-evsev2g-renegotiation-cablecheck", "modules/EVSE/EvseV2G/iso_server.cpp",
     "iso_dc_state_id::WAIT_FOR_CABLECHECK", "renegotiation lands in WAIT_FOR_CABLECHECK"),
    ("everest-evsev2g-metering-chain (back)", "modules/EVSE/EvseV2G/iso_server.cpp",
     "static void publish_iso_metering_receipt_req", "empty MeteringReceipt publisher"),
    ("everest-evsev2g-metering-chain (out)", "modules/EVSE/EvseV2G/charger/ISO15118_chargerImpl.cpp",
     "v2g_ctx->meter_info.meter_reading = powermeter.energy_Wh_import.total;",
     "unsigned total, not the signed sibling"),
    ("everest-evsev2g-paymentdetails-crash", None, paymentdetails_order,
     "order of the parse guard and the first use"),
    ("everest-evsev2g-session-log-responses", "modules/EVSE/EvseV2G/v2g_server.cpp",
     "i < conn->payload_len + V2GTP_HEADER_LENGTH", "response logged with the request's length"),
    ("everest-evse-security-ocsp-dropped", None, ocsp_conversion,
     "the conversion that drops ocsp"),
    # Section 1 (routing on the first -20 entry, Priority only logged) has no marker: it is a
    # property of a function's return expression and is checked by reading.
    ("everest-isomux §2", "modules/EVSE/IsoMux/connection/tls_connection.cpp",
     'config.ciphersuites = "";', "empty TLS 1.3 suites: lib/everest/tls caps max proto at TLS 1.2"),
    ("everest-isomux §3", None, isomux_failed_read,
     "a failed header read is logged and then ignored"),
    # libiso15118, vendored into everest-core -- the three that have diverged from the standalone repo
    ("everest-iso20-ac-contactor-latch", "lib/everest/iso15118/src/iso15118/d20/state/power_delivery.cpp",
     "ac_connector_closed = control_data;", "pointer assigned to bool (no dereference)"),
    ("everest-loop-shutdown", "lib/everest/iso15118/src/iso15118/io/connection_ssl.cpp",
     'log_and_raise_openssl_error("Failed to SSL_accept()', "handshake failure throws out of the poll loop"),
    ("everest-d20-ac-namespace", None, ac_namespace_gate,
     "no capability filter before the priority map"),
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
    ("everest-d20-client-auth §1", None, client_auth_by_version,
     "client verification decided by the version the EV offered"),
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
    ap.add_argument("--ref", default="main",
                    help="branch or tag to read (default: main). Run once against main and once "
                         "against the newest release tag; a fix on main is not a fix anyone has.")
    ap.add_argument("--lib", action="store_true",
                    help="check the UNMAINTAINED standalone EVerest/libiso15118 mirror; history, "
                         "not status -- everest-core is the live tree")
    args = ap.parse_args()

    base, checks = RAW.format(repo=CORE_REPO, ref=args.ref), CORE_CHECKS
    if args.lib:
        print("NOTE: EVerest/libiso15118 is an unmaintained mirror. A 'STILL PRESENT' below means")
        print("      nobody has touched that file, not that the defect is live. everest-core decides.")
        print()
        base = RAW.format(repo=LIB_REPO, ref=args.ref)
        checks = [(r, p[len(LIB_PREFIX):], m, n, *rest) for r, p, m, n, *rest in CORE_CHECKS
                  if p is not None and p.startswith(LIB_PREFIX)]

    print(f"{base}\n")

    def get(path):
        return fetch(base + path)

    present = gone = err = 0
    for report, path, marker, note, *rest in checks:
        if callable(marker):
            still_there, detail = marker(get)
            if still_there is None:
                err += 1
                print(f"  !!  {report:42s} {detail}")
                continue
        else:
            invert = bool(rest and rest[0])
            body = get(path)
            if body.startswith("<<"):
                err += 1
                print(f"  !!  {report:42s} {body}  ({path})")
                continue
            still_there = (marker not in body) if invert else (marker in body)
            detail = None
        if still_there:
            present += 1
            print(f"  =   {report:42s} STILL PRESENT   {note}")
        else:
            gone += 1
            print(f"  *   {report:42s} LOOK AGAIN      {note}")
            if detail is not None:
                print(f"        -> {detail}")
            else:
                print(f"        -> {path}  :: {'fix marker found' if invert else 'defect marker gone'}: {marker!r}")

    print(f"\n{present} still present, {gone} changed, {err} could not be decided", file=sys.stderr)
    return 1 if err else 0


if __name__ == "__main__":
    sys.exit(main())
