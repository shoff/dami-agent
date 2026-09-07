#!/usr/bin/env bash
# Exercises tools/dami-down and tools/dami-up against shims for docker, systemctl, sudo and
# curl: nothing on the workstation is touched. Run: bash tools/tests/dami-updown-tests.sh
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
sandbox="$(mktemp -d)"
trap 'rm -rf "$sandbox"' EXIT
export CALLS="$sandbox/calls"
mkdir "$sandbox/bin"

# docker: dami-llm and dami-embed are running, dami-kibana is exited, dami-stt does not exist.
cat > "$sandbox/bin/docker" <<'SHIM'
#!/usr/bin/env bash
printf '%s\n' "docker $*" >> "$CALLS"
if [[ "$1" == inspect ]]; then
    case "${@: -1}" in
        dami-llm|dami-embed|dami-rerank|dami-searxng|dami-elasticsearch|dami-filebeat) echo running ;;
        dami-kibana) echo exited ;;
        *) echo "Error: No such object" >&2; exit 1 ;;
    esac
fi
exit 0
SHIM
cat > "$sandbox/bin/systemctl" <<'SHIM'
#!/usr/bin/env bash
printf '%s\n' "systemctl $*" >> "$CALLS"
# list-unit-files: every unit but dami-tts is installed.
if [[ "$1" == list-unit-files ]]; then
    [[ "$2" == dami-tts.service ]] && exit 0
    echo "$2 enabled"
fi
exit 0
SHIM
cat > "$sandbox/bin/sudo" <<'SHIM'
#!/usr/bin/env bash
printf '%s\n' "sudo $*" >> "$CALLS"
exec "$@"
SHIM
cat > "$sandbox/bin/curl" <<'SHIM'
#!/usr/bin/env bash
printf '%s\n' "curl $*" >> "$CALLS"
exit 0
SHIM
chmod +x "$sandbox"/bin/*
export PATH="$sandbox/bin:$PATH"

fail() { echo "FAIL: $1" >&2; exit 1; }

# --- down ---
: > "$CALLS"
out=$(bash "$here/dami-down")
grep -q "^sudo systemctl stop dami-llm-guard.timer" "$CALLS" || fail "guard timer not stopped"
grep -q "^sudo systemctl stop dami-host.service" "$CALLS" || fail "host not stopped"
grep -q "dami-tts.service (not installed)" <<< "$out" || fail "missing unit not skipped: $out"
grep -q "^docker stop dami-llm" "$CALLS" || fail "dami-llm not stopped"
grep -q "^docker stop dami-kibana" "$CALLS" && fail "exited container was stopped again"
grep -q "^docker stop dami-stt" "$CALLS" && fail "missing container was stopped"
# services before containers
[[ $(grep -n -m1 "systemctl stop dami-host" "$CALLS" | cut -d: -f1) -lt $(grep -n -m1 "docker stop dami-llm" "$CALLS" | cut -d: -f1) ]] || fail "containers stopped before services"
# filebeat before elasticsearch
[[ $(grep -n -m1 "docker stop dami-filebeat" "$CALLS" | cut -d: -f1) -lt $(grep -n -m1 "docker stop dami-elasticsearch" "$CALLS" | cut -d: -f1) ]] || fail "elasticsearch stopped before filebeat"

# --- down, dry run touches nothing ---
: > "$CALLS"
out=$(bash "$here/dami-down" --dry-run)
grep -q "would: sudo systemctl stop dami-host.service" <<< "$out" || fail "dry run did not describe the stop: $out"
grep -q "^sudo\|^docker stop" "$CALLS" && fail "dry run ran something: $(cat "$CALLS")"

# --- up ---
: > "$CALLS"
out=$(DAMI_UP_WAIT=1 bash "$here/dami-up")
grep -q "^docker start dami-kibana" "$CALLS" || fail "exited container not started"
grep -q "^docker start dami-llm" "$CALLS" && fail "running container was started again"
grep -q "curl .*127.0.0.1:11434/api/version" "$CALLS" || fail "did not wait for Ollama"
grep -q "curl .*9200/_cluster/health" "$CALLS" || fail "did not wait for Elasticsearch"
grep -q "^sudo systemctl start dami-host.service" "$CALLS" || fail "host not started"
grep -q "^sudo systemctl start dami-llm-guard.timer" "$CALLS" || fail "guard timer not re-armed"
# every container check before the first service start
[[ $(grep -n -m1 "curl .*5601" "$CALLS" | cut -d: -f1) -lt $(grep -n -m1 "systemctl start dami-host" "$CALLS" | cut -d: -f1) ]] || fail "services started before the sidecars answered"

# --- up, dry run ---
: > "$CALLS"
out=$(bash "$here/dami-up" --dry-run)
grep -q "would: docker start dami-kibana" <<< "$out" || fail "dry run did not describe the start: $out"
grep -q "would: wait for http://127.0.0.1:11434" <<< "$out" || fail "dry run did not describe the wait: $out"
grep -q "^docker start\|^sudo" "$CALLS" && fail "dry run ran something: $(cat "$CALLS")"

echo "all dami-up/down tests passed"
