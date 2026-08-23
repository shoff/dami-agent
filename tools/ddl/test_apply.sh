#!/usr/bin/env bash
set -euo pipefail

directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
test_directory="$(mktemp -d)"
trap 'rm -rf "$test_directory"' EXIT

call_log="$test_directory/psql-calls"
export PSQL_CALL_LOG="$call_log"

mkdir "$test_directory/bin"
cat > "$test_directory/bin/psql" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail

printf '%s\n' "$*" >> "$PSQL_CALL_LOG"

if [[ "$*" == *"create schema"* ]]; then
    echo "status attempted a schema mutation" >&2
    exit 77
fi

if [[ "$*" == *"select filename from dami.schema_migrations"* ]]; then
    printf '%s\n' \
        001_migrations.sql \
        002_event_store.sql \
        003_memory.sql \
        004_append_only_truncate.sql \
        005_test_schema.sql \
        006_surfacings.sql \
        007_proactive_runs.sql \
        008_observation_embeddings.sql
fi
EOF
chmod +x "$test_directory/bin/psql"

PATH="$test_directory/bin:$PATH" "$directory/apply.sh" --status > /dev/null

if grep -qi 'create schema' "$call_log"; then
    echo "FAIL: --status attempted CREATE SCHEMA" >&2
    exit 1
fi

echo "PASS: --status only inspected migration state"
