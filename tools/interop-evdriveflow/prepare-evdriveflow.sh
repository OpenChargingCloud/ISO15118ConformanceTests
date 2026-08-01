#!/usr/bin/env bash
#
# Check that an eVDriveFlow checkout is ready for an interop run — and install nothing.
#
# Their bring-up is conda plus a JDK plus a certificate script, all of which are theirs to run and yours
# to approve. This says what is missing and what to do about it.
#
# Usage: prepare-evdriveflow.sh <path-to-their-checkout>
set -uo pipefail

repo="${1:?usage: prepare-evdriveflow.sh <path-to-eVDriveFlow-checkout>}"
missing=0

say() { printf '%-34s %s\n' "$1" "$2"; }

echo "=== their checkout"
if [ -d "$repo/secc" ] && [ -d "$repo/evcc" ] && [ -d "$repo/shared" ]; then
    say "$repo" "ok"
else
    say "$repo" "does not look like eVDriveFlow (no secc/ evcc/ shared/)"
    echo
    echo "    git clone https://github.com/EDF-Lab/eVDriveFlow"
    exit 1
fi

echo "=== toolchain"
if command -v conda >/dev/null 2>&1; then
    say "conda" "$(conda --version 2>/dev/null)"
    if conda env list 2>/dev/null | grep -q '^edf15118-20'; then
        say "conda env edf15118-20" "created"
    else
        say "conda env edf15118-20" "MISSING — conda env create -f $repo/environment.yml"
        missing=1
    fi
else
    say "conda" "MISSING — see their README; not installing it here"
    missing=1
fi

# Their EXI is OpenEXI, a Java library. A missing or wrong JDK surfaces as an encoding failure, which is
# the most misleading possible symptom: it looks like a codec bug in whichever side you suspect first.
if command -v java >/dev/null 2>&1; then
    say "java (OpenEXI needs it)" "$(java -version 2>&1 | head -1)"
else
    say "java (OpenEXI needs it)" "MISSING — their EXI cannot run without a JDK"
    missing=1
fi

echo "=== their certificates"
if compgen -G "$repo/shared/certificates/*.pem" >/dev/null 2>&1 || \
   compgen -G "$repo/shared/certificates/*/*.pem" >/dev/null 2>&1; then
    say "shared/certificates" "generated"
else
    say "shared/certificates" "MISSING — cd $repo/shared/certificates && sh generateCertificates.sh"
    missing=1
fi

echo "=== their configuration"
for cfg in "$repo/secc/evse_config.ini" "$repo/evcc/ev_config.ini"; do
    if [ -f "$cfg" ]; then
        iface=$(grep -E '^\s*interface\s*=' "$cfg" | head -1 | sed 's/.*=\s*//')
        virt=$(grep -E '^\s*virtual_mode\s*=' "$cfg" | head -1 | sed 's/.*=\s*//')
        say "$(basename "$cfg")" "interface=${iface:-?}  virtual_mode=${virt:-?}"
        if [ -n "${iface:-}" ] && ! ip link show "$iface" >/dev/null 2>&1; then
            say "" "  ^ this machine has no interface '$iface'"
            missing=1
        fi
    else
        say "$(basename "$cfg")" "MISSING"
        missing=1
    fi
done

cat <<'EOF'

Note on virtual_mode: their documentation describes it as simulating the communication card. For a run
against a foreign peer over a real interface it is the first setting to question — see README.md.

TLS is on by default (mutual TLS 1.3) and is turned off for testing by editing SECURITY_PROTOCOL in
shared/global_values.py. Do the first run without it.
EOF

if [ "$missing" = 0 ]; then
    cat <<EOF

Ready. Next:
    ./reverse-iso20-dc.sh <iface> 55000     their EV against our station (Dynamic)
    ./live-evcc-iso20-dc.sh <iface>         our EVCC against their station

Theirs, headless:
    (cd $repo/secc && python3 start_evse.py)
    (cd $repo/evcc && python3 start_ev.py)
EOF
fi

exit "$missing"
