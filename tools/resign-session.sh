#!/usr/bin/env bash
# Re-sign the run of unsigned commits at the tip of the current branch with GPG, then guide the force-push.
#
# Why this exists: several recent live-interop commits were made with `--no-gpg-sign` because this project's
# non-interactive dev shell has no GPG pinentry (signing fails with "No pinentry"). Run THIS script from an
# INTERACTIVE terminal, with your GPG agent able to prompt (or its passphrase cached), to sign them.
#
# It rebases from the newest already-signed commit and re-signs every commit after it. Content is unchanged;
# only the signatures (and committer dates) change, so the rewritten commits get new hashes and the push must
# be `--force-with-lease` (the script does NOT push — it prints the command for you to run after reviewing).
#
# Usage: tools/resign-session.sh [base-commit]
#   base-commit  re-sign every commit *after* this one (default: auto-detect the newest signed commit).
#
# Note: run this BEFORE staging/committing the script itself — a rebase needs a clean working tree. Commit
# this file afterwards (it'll sign fine once your agent is unlocked), or leave it untracked.
set -uo pipefail

base="${1:-}"
if [ -z "$base" ]; then
  # Newest commit whose signature is Good (G) or good-but-unknown-validity (U); everything newer gets re-signed.
  while read -r hash sig; do
    case "$sig" in G|U) base="$hash"; break;; esac
  done < <(git log --format='%H %G?' -100)
fi
[ -n "$base" ] || { echo "No signed commit found in the last 100 to use as a base; pass one explicitly." >&2; exit 1; }

count=$(git rev-list --count "$base..HEAD")
[ "$count" -gt 0 ] || { echo "Nothing to re-sign — HEAD is already signed (or is the base)."; exit 0; }

echo "Commits to re-sign (everything after $(git rev-parse --short "$base")):"
git log --format='  %h %G? %s' "$base..HEAD"
echo
echo "This REWRITES these $count commit(s) with new hashes and will require: git push --force-with-lease"
printf 'Proceed? [y/N] '
read -r ans
case "$ans" in y|Y) ;; *) echo "aborted."; exit 0;; esac

if git rebase --exec 'git commit --amend --no-edit -S' "$base"; then
  echo
  echo "Re-signed. Status (want every line 'G'):"
  git log --format='  %h %G? %s' "$base..HEAD"
  echo
  echo "Reviewed and happy? Push with:  git push --force-with-lease"
else
  echo
  echo "!! rebase failed — most likely GPG could not sign (No pinentry / agent locked)." >&2
  echo "   Undo cleanly with:  git rebase --abort" >&2
  echo "   Then retry from an interactive terminal with your GPG agent unlocked." >&2
  exit 1
fi
