#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
guard="$script_dir/../dami-llm-guard"
scratch=$(mktemp -d)
trap 'rm -rf -- "$scratch"' EXIT

mkdir "$scratch/bin"

# curl answers /api/ps with whatever $DAMI_GUARD_PS holds.
printf '%s\n' \
    '#!/usr/bin/env bash' \
    'printf '\''%s\n'\'' "$DAMI_GUARD_PS"' \
    > "$scratch/bin/curl"

# docker inspect reports running unless the container is named in $DAMI_GUARD_STOPPED;
# every other docker call is recorded.
printf '%s\n' \
    '#!/usr/bin/env bash' \
    'if [[ "$1" == inspect ]]; then' \
    '    [[ " $DAMI_GUARD_STOPPED " == *" ${!#} "* ]] && echo false || echo true' \
    '    exit 0' \
    'fi' \
    'printf '\''%s\n'\'' "$*" >> "$DAMI_GUARD_DOCKER_CALLS"' \
    > "$scratch/bin/docker"

# dami-host is active; the notification is recorded rather than shown.
printf '%s\n' '#!/usr/bin/env bash' 'exit 0' > "$scratch/bin/systemctl"
printf '%s\n' '#!/usr/bin/env bash' 'printf '\''%s\n'\'' "$*" >> "$DAMI_GUARD_NOTIFY_CALLS"' > "$scratch/bin/runuser"

chmod +x "$scratch/bin/"*
export DAMI_GUARD_DOCKER_CALLS="$scratch/docker-calls"
export DAMI_GUARD_NOTIFY_CALLS="$scratch/notify-calls"
export DAMI_GUARD_NOTIFY_USER="$(id -un)"
export PATH="$scratch/bin:$PATH"

fail() { echo "FAIL: $1" >&2; exit 1; }

# 1. A stopped GPU sidecar under a running dami-host is reported, not restarted.
export DAMI_GUARD_PS='{"models":[]}'
export DAMI_GUARD_STOPPED="dami-embed dami-rerank"
rm -f "$DAMI_GUARD_DOCKER_CALLS" "$DAMI_GUARD_NOTIFY_CALLS"
status=0
"$guard" 2>"$scratch/stderr" || status=$?
[[ $status -eq 1 ]] || fail "stranded sidecars exited $status, expected 1"
grep -q 'dami-embed dami-rerank stopped' "$scratch/stderr" || fail "stranded sidecars were not named: $(<"$scratch/stderr")"
[[ ! -f "$DAMI_GUARD_DOCKER_CALLS" ]] || fail "stranded sidecars were acted on: $(<"$DAMI_GUARD_DOCKER_CALLS")"
if [[ -S "/run/user/$(id -u)/bus" ]]; then
    grep -q 'notify-send' "$DAMI_GUARD_NOTIFY_CALLS" 2>/dev/null || fail "no desktop notification was sent"
fi
echo "PASS: stopped GPU sidecars under a running dami-host are reported and left alone"

# 2. Everything running and in VRAM: silent success.
export DAMI_GUARD_STOPPED=""
export DAMI_GUARD_PS='{"models":[{"size":100,"size_vram":100}]}'
rm -f "$DAMI_GUARD_DOCKER_CALLS"
"$guard" || fail "a healthy stack exited nonzero"
[[ ! -f "$DAMI_GUARD_DOCKER_CALLS" ]] || fail "a healthy stack was acted on"
echo "PASS: a healthy stack is left alone"

# 3. Degraded placement restarts dami-llm (samples twice, 30 s apart).
export DAMI_GUARD_PS='{"models":[{"size":100,"size_vram":0}]}'
rm -f "$DAMI_GUARD_DOCKER_CALLS"
"$guard"
[[ -f "$DAMI_GUARD_DOCKER_CALLS" ]] || fail "a model with no VRAM placement did not trigger a restart"
actual=$(<"$DAMI_GUARD_DOCKER_CALLS")
[[ "$actual" == "restart dami-llm" ]] || fail "expected 'restart dami-llm', got '$actual'"
echo "PASS: degraded placement restarts dami-llm"
