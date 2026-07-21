#!/usr/bin/env bash
# Prepare a Josev clone for interop, the default way: rebase its Docker image off the pinned EOL
# `python:3.10.0-buster` onto a current Debian (trixie), generate the secc/evcc Dockerfiles, and
# create the ISO 15118-2 test certs. After this, `docker compose build` just works — no archived
# apt repos, no EOL base. The interop only depends on Python 3.10.x + Josev's poetry.lock, not the
# Debian release, so the rebase is safe.
#
# Usage: prepare-josev.sh [path-to-josev-clone]     (default: ./iso15118)
set -euo pipefail

josev="${1:-./iso15118}"
cd "$josev"

# 1. Rebase the pinned EOL buster base onto a current Debian. Full (non-slim) image keeps the build
#    tools the multi-stage build needs.
sed -i 's|python:3.10.0-buster|python:3.10-trixie|g' template.Dockerfile

# 2. Replicate the Makefile's Dockerfile templating by hand (its `make build` uses docker-compose v1;
#    with Compose v2 we drive `docker compose` directly).
cp -f template.Dockerfile iso15118/secc/Dockerfile
cp -f template.Dockerfile iso15118/evcc/Dockerfile
sed -i 's@/secc/g@/evcc/g@g' iso15118/evcc/Dockerfile          # switch the evcc image to the evcc entrypoint
sed -i 's@CMD /venv/bin/iso15118@CMD sleep 5 \&\& /venv/bin/iso15118@' iso15118/evcc/Dockerfile  # let the SECC start first

# 3. ISO 15118-2 test certificates.
( cd iso15118/shared/pki && ./create_certs.sh -v iso-2 )

cat <<'DONE'

Prepared (Debian trixie). Next:
  docker compose build
  # to capture EXI for cross-validation, set MESSAGE_LOG_EXI=True in .env.dev.docker, then:
  docker compose -f docker-compose.yml -f docker-compose.dev.yml up
DONE
