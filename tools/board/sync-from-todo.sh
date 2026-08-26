#!/usr/bin/env bash
#
# Dami Core — keep the task board current from TODO.md (O2b).
#
# Re-imports TODO.md into the board at the current HEAD. The importer advances only, so
# state written directly on the board is never regressed by a stale file, and whatever
# the file and the board disagree about is printed as a conflict rather than forced.
#
# Called by .githooks/post-commit when a commit touches TODO.md; safe to run by hand.
# Never fails the caller: a board that cannot be reached is reported, not fatal.
#
#   DAMI_ACTOR       who is importing (default: the login user)
#   DAMI_ACTOR_KIND  Human (default) or Agent
set -uo pipefail

repo="$(git rev-parse --show-toplevel 2>/dev/null)" || { echo "board-sync: not in a repository" >&2; exit 0; }
revision="$(git -C "$repo" rev-parse HEAD)"
actor="${DAMI_ACTOR:-$(id -un)}"
kind_flag=()
[[ "${DAMI_ACTOR_KIND:-Human}" =~ ^[Aa]gent$ ]] && kind_flag=(--agent)

if ! command -v dami >/dev/null 2>&1; then
    echo "board-sync: dami is not on PATH; board not updated for $revision" >&2
    exit 0
fi

echo "board-sync: importing TODO.md at ${revision:0:7} as $actor"
if ! dami board-import "$repo/TODO.md" --revision "$revision" --actor "$actor" "${kind_flag[@]}" \
        | grep -vE '^  line [0-9]+:'; then
    echo "board-sync: import reported conflicts or could not reach the board (see above)" >&2
fi
exit 0
