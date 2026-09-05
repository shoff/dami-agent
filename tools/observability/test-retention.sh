#!/usr/bin/env bash
# Exercises dami-es-retention against a fake Elasticsearch: a curl shim that serves a
# canned index listing and records every DELETE. Run: bash tools/observability/test-retention.sh
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
sandbox="$(mktemp -d)"
trap 'rm -rf "$sandbox"' EXIT
export DELETE_LOG="$sandbox/deletes"
mkdir "$sandbox/bin"
cat > "$sandbox/bin/curl" <<'SHIM'
#!/usr/bin/env bash
if [[ "$*" == *"_cat/indices"* ]]; then
    # bytes: three days of 30 GB, 30 GB, 25 GB (85 GB); today is the newest.
    printf '.ds-dami-runtime-2026.09.03-2026.09.03-000001 %s\n' $((30*1024*1024*1024))
    printf '.ds-dami-runtime-2026.09.04-2026.09.04-000001 %s\n' $((30*1024*1024*1024))
    printf '.ds-dami-runtime-2026.09.05-2026.09.05-000001 %s\n' $((25*1024*1024*1024))
    exit 0
fi
if [[ "$*" == *"DELETE"* ]]; then
    printf '%s\n' "$*" >> "$DELETE_LOG"
    exit 0
fi
exit 22
SHIM
chmod +x "$sandbox/bin/curl"
export PATH="$sandbox/bin:$PATH"

fail() { echo "FAIL: $1" >&2; exit 1; }

# 85 GB against a 50 GB cap: the two oldest go (85 -> 55 -> 25), never today's.
out=$(DAMI_ES_CAP_GB=50 bash "$here/dami-es-retention")
grep -q "deleted dami-runtime-2026.09.03" <<< "$out" || fail "oldest not deleted: $out"
grep -q "deleted dami-runtime-2026.09.04" <<< "$out" || fail "second oldest not deleted: $out"
grep -q "2026.09.05" "$DELETE_LOG" && fail "today's stream was deleted"
[[ $(wc -l < "$DELETE_LOG") -eq 2 ]] || fail "expected 2 deletes, got $(cat "$DELETE_LOG")"

# Dry run deletes nothing and says what it would do.
: > "$DELETE_LOG"
out=$(DAMI_ES_CAP_GB=50 bash "$here/dami-es-retention" --dry-run)
grep -q "would delete dami-runtime-2026.09.03" <<< "$out" || fail "dry run silent: $out"
[[ ! -s "$DELETE_LOG" ]] || fail "dry run deleted something"

# Under the cap: nothing happens.
out=$(DAMI_ES_CAP_GB=100 bash "$here/dami-es-retention")
[[ ! -s "$DELETE_LOG" ]] || fail "deleted under the cap"
grep -q "cap 100 GB" <<< "$out" || fail "no report: $out"

# Even a cap below today's size never deletes today's stream.
: > "$DELETE_LOG"
DAMI_ES_CAP_GB=1 bash "$here/dami-es-retention" > /dev/null
grep -q "2026.09.05" "$DELETE_LOG" && fail "today's stream deleted under an impossible cap"

echo "test-retention: ok"
