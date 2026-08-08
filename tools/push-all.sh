#!/usr/bin/env bash
# Push one ref to all three remotes, one call at a time, and prove afterwards that it arrived.
#
# Why this exists: on 2026-08-08 a batched push of one branch to git1/git2/origin across the three
# repositories silently skipped 2 of 9 pushes. Every sign said success -- exit code 0, and GitHub's
# "Create a pull request for ..." hint printed for a branch that was not on the remote. Only
# `ls-remote` disagreed. The root cause was never established, so the defence here is not "avoid a
# loop", it is "ask the remote what it has, every time". That check is cheap and does not care why a
# push went missing.
#
# The second half of the same incident is encoded too. When a ref is absent after a push that looked
# fine, the reflex is to push again -- and that day it was wrong: the branch was gone because the PR
# had already been merged and GitHub deletes the head branch on merge. Re-pushing recreated three
# already-merged branches that then had to be deleted again. So before retrying, this asks whether
# the commit is already in the remote's main branch, and says so instead of resurrecting it.
#
# Runs against the repository you are CURRENTLY IN, not the one this file lives in -- the same ritual
# applies to all three, and only the innermost one carries this script. The repository is printed so
# you can see which one you got.
#
# Usage:
#   tools/push-all.sh [ref]              push ref (default: the current branch) to every remote
#   tools/push-all.sh --delete <ref>     delete ref from every remote, and verify it is gone
#
# Environment:
#   PUSH_REMOTES   space-separated remote names   (default: git1 git2 origin, minus any not configured)
#   MAIN_BRANCH    branch a merged ref would be in (default: master)
set -uo pipefail

remotes_wanted="${PUSH_REMOTES:-git1 git2 origin}"
main_branch="${MAIN_BRANCH:-master}"

mode=push
if [ "${1:-}" = "--delete" ]; then
    mode=delete
    shift
    [ $# -ge 1 ] || { echo "--delete needs a ref." >&2; exit 2; }
fi

ref="${1:-$(git symbolic-ref --quiet --short HEAD)}"
[ -n "$ref" ] || { echo "No ref given and HEAD is detached -- pass one explicitly." >&2; exit 2; }

git rev-parse --git-dir >/dev/null 2>&1 || { echo "Not inside a git repository." >&2; exit 2; }
repo=$(basename "$(git rev-parse --show-toplevel)")

# Only remotes this repository actually has. A typo in PUSH_REMOTES should not look like a failed push.
remotes=""
for r in $remotes_wanted; do
    if git config --get "remote.$r.url" >/dev/null 2>&1; then
        remotes="$remotes $r"
    else
        echo "  note: no remote '$r' in $repo -- skipping"
    fi
done
[ -n "$remotes" ] || { echo "None of the requested remotes exist here." >&2; exit 2; }

if [ "$mode" = push ]; then
    local_sha=$(git rev-parse --verify --quiet "$ref^{commit}") \
        || { echo "No such ref here: $ref" >&2; exit 2; }
    echo "$repo: pushing $ref ($(git rev-parse --short "$local_sha")) to$remotes"
else
    echo "$repo: deleting $ref from$remotes"
fi
echo

# One remote per iteration, each pushed and then verified on its own. Nothing is batched and no
# result is inferred from another remote's.
failed=""
for r in $remotes; do

    if [ "$mode" = delete ]; then
        # Ask first here too. A branch merged on GitHub is already gone, and asking git to delete it
        # prints two error lines before this reports the truth -- which reads like a failure in a
        # step that in fact succeeded before it started.
        if [ -z "$(git ls-remote --heads "$r" "$ref" 2>/dev/null)" ]; then
            echo "  $r: already gone"
            echo
            continue
        fi
        git push "$r" --delete "$ref" 2>&1 | sed 's/^/    /'
        if [ -z "$(git ls-remote --heads "$r" "$ref" 2>/dev/null)" ]; then
            echo "  $r: gone"
        else
            echo "  $r: STILL PRESENT -- not deleted"
            failed="$failed $r"
        fi
        echo
        continue
    fi

    # Ask BEFORE pushing, because the answer can be "do not push this at all". A branch that is
    # absent from the remote *and* already contained in its main branch was merged, and the remote
    # deleted it on merge -- pushing would resurrect a branch someone deliberately removed. Checking
    # afterwards would be useless: by then the push has recreated it and everything looks fine.
    if [ -z "$(git ls-remote --heads "$r" "$ref" 2>/dev/null)" ] &&
       git fetch --quiet "$r" "$main_branch" 2>/dev/null &&
       git merge-base --is-ancestor "$local_sha" FETCH_HEAD 2>/dev/null; then
        echo "  $r: skipped -- ${local_sha:0:7} is already in $r/$main_branch and the branch is gone."
        echo "        Merged, and the remote deleted it on merge. Nothing to push."
        echo
        continue
    fi

    git push "$r" "$ref" 2>&1 | sed 's/^/    /'
    remote_sha=$(git ls-remote --heads "$r" "$ref" 2>/dev/null | cut -f1)

    if [ "$remote_sha" = "$local_sha" ]; then
        echo "  $r: ok, ${local_sha:0:7}"
    elif [ -n "$remote_sha" ]; then
        echo "  $r: MISMATCH -- remote has ${remote_sha:0:7}, local is ${local_sha:0:7}"
        failed="$failed $r"
    else
        echo "  $r: absent after a push that reported success -- retrying once"
        git push "$r" "$ref" 2>&1 | sed 's/^/    /'
        remote_sha=$(git ls-remote --heads "$r" "$ref" 2>/dev/null | cut -f1)
        if [ "$remote_sha" = "$local_sha" ]; then
            echo "  $r: ok on the retry, ${local_sha:0:7}"
        else
            echo "  $r: STILL NOT THERE -- do not build on this"
            failed="$failed $r"
        fi
    fi
    echo
done

if [ -n "$failed" ]; then
    echo "FAILED on:$failed" >&2
    exit 1
fi
echo "All remotes agree."
