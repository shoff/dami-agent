#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
guard="$script_dir/../dami-llm-guard"
scratch=$(mktemp -d)
trap 'rm -rf -- "$scratch"' EXIT

mkdir "$scratch/bin"

printf '%s\n' \
    '#!/usr/bin/env bash' \
    'printf '\''%s\n'\'' '\''{"models":[{"size":100,"size_vram":0}]}'\''' \
    > "$scratch/bin/curl"

printf '%s\n' \
    '#!/usr/bin/env bash' \
    'printf '\''%s\n'\'' "$*" > "$DAMI_GUARD_DOCKER_CALLS"' \
    > "$scratch/bin/docker"

chmod +x "$scratch/bin/curl" "$scratch/bin/docker"
export DAMI_GUARD_DOCKER_CALLS="$scratch/docker-calls"
export PATH="$scratch/bin:$PATH"

"$guard"

if [[ ! -f "$DAMI_GUARD_DOCKER_CALLS" ]]; then
    echo "FAIL: a model with no VRAM placement did not trigger a restart" >&2
    exit 1
fi

actual=$(<"$DAMI_GUARD_DOCKER_CALLS")
if [[ "$actual" != "restart dami-llm" ]]; then
    echo "FAIL: expected 'restart dami-llm', got '$actual'" >&2
    exit 1
fi

echo "PASS: degraded placement restarts dami-llm"
