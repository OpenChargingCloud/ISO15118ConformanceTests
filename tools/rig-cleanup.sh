#!/bin/bash
#
# Take down what an interop session started, and keep what would cost a rebuild.
#
# The live counterparties (tux-evse, eVDriveFlow, EVerest) run from one WSL2/Linux host — see each
# tools/interop-*/README.md for how they are brought up. This is the other half: what to do when the
# runs are over. Everything it removes is recreated by a script that is committed to this repository;
# everything it keeps is named at the end, with what it would cost.
#
# Run as root (namespaces and other users' processes):  sudo bash tools/rig-cleanup.sh
# RIG_HOME overrides where the rigs live; it defaults to the invoking user's home, or to SUDO_USER's.
#
# root is only needed for what is still *running* — deleting a namespace, killing another user's
# binder, talking to dockerd. When a session ended cleanly there is nothing of the sort left, and this
# runs perfectly well as the owning user; every path it removes is under RIG_HOME. Worth knowing,
# because a `sudo` that stops for a password it cannot be given just hangs.
#
set -u

RIG_HOME="${RIG_HOME:-$(getent passwd "${SUDO_USER:-$(id -un)}" | cut -d: -f6)}"
[ -d "$RIG_HOME" ] || { echo "RIG_HOME=$RIG_HOME does not exist"; exit 1; }
echo "rig home: $RIG_HOME"
echo

echo "=== containers and docker (eVDriveFlow)"
docker rm -f edf-ev  2>/dev/null | sed 's/^/  removed container /'
docker network rm edfnet 2>/dev/null | sed 's/^/  removed network /'
pkill -x dockerd && echo "  dockerd stopped — it is started by hand for EDF runs, never at boot" \
                 || echo "  dockerd already stopped"

echo "=== tux-evse binders and the namespace"
# Their binder renames its process to whatever --name says, so it is afb-evse / afb-evcc in ps and
# `pkill -x afb-binder` never matches. And a wedged one does not answer SIGTERM at all — that is
# issue D of docs/reports/tux-evse-spin.md, measured at ten minutes — so escalate rather than wait.
pkill -x afb-evse 2>/dev/null; pkill -x afb-evcc 2>/dev/null
sleep 1
pkill -9 -x afb-evse 2>/dev/null; pkill -9 -x afb-evcc 2>/dev/null
ip netns delete tuxev 2>/dev/null && echo "  netns tuxev deleted (recreate: their run/netns.sh)" \
                                  || echo "  no netns tuxev"
ip link delete evse-veth 2>/dev/null && echo "  veth pair deleted" || echo "  no evse-veth"

echo "=== everest, if a SIL was left running"
pkill -f "everest/dist/bin/manager" 2>/dev/null && echo "  manager stopped" || echo "  no everest manager"

echo "=== v2gdecoder oracle: the derived schema directories"
# Everything under ~/v2gdec except the jar and the pristine clone is derived, and some of it is
# actively unsafe to leave lying about. Investigating the fuzzy-grammar issues meant rigging the
# schema directories -- V2Gdecoder builds its grammar array from three fixed filenames, so putting
# the AppProtocol XSD at V2G_CI_MsgDef.xsd changes which grammar answers first. A directory like
# that decodes real frames into confident nonsense, which is issue A of
# docs/reports/v2gdecoder-fuzzy-grammar.md turned into a booby trap for our future selves.
# tools/interop-v2gdecoder/setup.sh recreates the one staged directory that is meant to exist.
rm -rf "$RIG_HOME"/v2gdec/p[0-9] "$RIG_HOME"/v2gdec/din "$RIG_HOME"/v2gdec/din2
rm -f  "$RIG_HOME"/v2gdec/*.json "$RIG_HOME"/v2gdec/*.py
# The -20 oracle's scratch: staged ISO schemas, the compiled driver, and a run's job/result files.
# roundtrip20.py rebuilds all of it, and the staged schemas are ISO's — no reason to leave copies.
rm -rf "$RIG_HOME"/v2gdec/exificient-work "$RIG_HOME"/v2gdec/classes \
       "$RIG_HOME"/v2gdec/iso20 "$RIG_HOME"/v2gdec/isolate "$RIG_HOME"/v2gdec/exhaust* \
       "$RIG_HOME"/v2gdec/rate "$RIG_HOME"/v2gdec/rel "$RIG_HOME"/v2gdec/access
# fuzzy-probe.sh is the script that *builds* those rigged directories, so it goes with them. The rest
# are staged copies of files that live in this repository — driving WSL from Windows means writing a
# script to /tmp and stripping its CRLFs first, and the copies outlive the run. Named individually
# rather than globbed: /tmp is shared, and nothing here should delete a file it did not write.
rm -f /tmp/fuzzy-probe.sh /tmp/rig-cleanup.sh /tmp/setup.sh /tmp/diffprobe.py /tmp/stringtable-probe.py
echo "  rigged and staged schema dirs removed (setup.sh restages din/)"

echo "=== transient run output"
# What is worth keeping from a run is excerpted into docs/interop-runs/ and committed. What is left
# here is the raw bulk — and with tux-evse that can be gigabytes: any peer that pauses or disconnects
# sends their binder into a ~20 MB/s log loop (issue C of the same report).
rm -rf "$RIG_HOME"/tux-evse/run/rec-* "$RIG_HOME"/tux-evse/run/*.log
rm -rf "$RIG_HOME"/tux-evse/porsche "$RIG_HOME"/tux-evse/porsche-refix "$RIG_HOME"/tux-evse/porsche-refix-none
rm -rf "$RIG_HOME"/edf/rec-* "$RIG_HOME"/edf/*.log
# Derived key material: regenerable from their own generators, and no reason to leave a private key
# lying about on a machine whose whole purpose is to be talked to by other people's software.
rm -f "$RIG_HOME"/edf/secc.pfx "$RIG_HOME"/tux-evse/run/server.pfx "$RIG_HOME"/tux-evse/run/pki/server.pfx
rm -f "$RIG_HOME"/edf/sap-reply.bin "$RIG_HOME"/tux-evse/run/sap-reply.bin
# Scenario JSON converted from a capture: megabytes, and regenerated by their own pcap-iso15118.
rm -f "$RIG_HOME"/v2gdec/tesla-din-tux.json
echo "  logs, recordings and derived PKCS#12 files removed"

echo
echo "=== kept deliberately"
echo "  run/*.json           relaxed scenarios — cheap to regenerate, and they document the runs"
echo "  run/*.sh             the runner scripts, including run-injector.sh with its 'timeout -k 5'"
echo "  everest/             a native build; hours"
echo "  tux-evse/cargo,src   their HEAD build — note the source Dockerfile omits injector-binding-rs"
echo "  i15118/              the conformance repo copy; rsync'd, but its build tree is not"
echo "  v2gdec/decoder.jar   6 MB, and the only oracle here that needs the network to come back"
echo "  v2gdec/V2Gdecoder    their clone, kept pristine — the schema sets live in it"
echo "  tux-evse/…/*.pcap    the captures, including tesla-3-din.pcap: irreplaceable, not ours"
du -sh "$RIG_HOME"/tux-evse/cargo "$RIG_HOME"/tux-evse/src "$RIG_HOME"/tux-evse/iso15118-simulator-rs \
       "$RIG_HOME"/tux-evse/run "$RIG_HOME"/edf "$RIG_HOME"/i15118 "$RIG_HOME"/everest \
       "$RIG_HOME"/v2gdec 2>/dev/null | sort -h

echo
echo "=== still running (should be nothing)"
(pgrep -a "afb-evse|afb-evcc|afb-binder|dockerd" | grep -v pgrep) || echo "  nothing"
ip netns list 2>/dev/null | sed 's/^/  netns /'
df -h "$RIG_HOME" | tail -1
