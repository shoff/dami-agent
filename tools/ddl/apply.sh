#!/usr/bin/env bash
#
# Dami Core — DDL runner.
#
# Applies pending *.sql in filename order, each in its own transaction, recording the
# result in dami.schema_migrations with a checksum. Re-running is a no-op.
#
# Standards §10 specifies an Npgsql runner. This is bash + psql instead, deliberately:
# creating the schema must not require building the solution, and the solution has no
# host project yet. Revisit when Dami.Host exists.
#
#   ./apply.sh              apply pending migrations
#   ./apply.sh --status     list applied and pending, change nothing
set -euo pipefail

DATABASE="${DAMI_DB:-dami-data}"
DIRECTORY="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Runs as the schema owner. dami_ddl owns dami; dami_app never runs DDL.
psql_ddl() { psql --dbname="$DATABASE" --no-psqlrc --quiet --set=ON_ERROR_STOP=1 "$@"; }

psql_ddl -c 'create schema if not exists dami authorization dami_ddl' > /dev/null

applied="$(psql_ddl -tAc "select filename from dami.schema_migrations" 2>/dev/null || true)"

pending=()
for file in "$DIRECTORY"/[0-9]*.sql; do
    name="$(basename "$file")"
    if ! grep -qxF "$name" <<< "$applied"; then
        pending+=("$file")
    fi
done

if [[ "${1:-}" == "--status" ]]; then
    echo "applied:"; [[ -n "$applied" ]] && sed 's/^/  /' <<< "$applied" || echo "  (none)"
    echo "pending:"; ((${#pending[@]})) && printf '  %s\n' "${pending[@]##*/}" || echo "  (none)"
    exit 0
fi

verify_checksums() {
    # A file edited after it was applied is a silent divergence between the repository
    # and the database. Runs on every invocation, including no-op ones.
    while IFS='|' read -r name recorded; do
        [[ -z "$name" ]] && continue
        current="$(sha256sum "$DIRECTORY/$name" 2>/dev/null | cut -d' ' -f1)"
        if [[ -n "$current" && "$current" != "$recorded" ]]; then
            echo "apply: WARNING $name was edited after it was applied" >&2
        fi
    done < <(psql_ddl -tAc "select filename || '|' || checksum from dami.schema_migrations" 2>/dev/null)
}

if ((${#pending[@]} == 0)); then
    verify_checksums
    echo "apply: nothing pending"
    exit 0
fi

for file in "${pending[@]}"; do
    name="$(basename "$file")"
    sum="$(sha256sum "$file" | cut -d' ' -f1)"
    echo "apply: $name"

    # The migration and its bookkeeping commit together, so a failure leaves neither.
    {
        echo "begin;"
        cat "$file"
        echo ";"
        echo "insert into dami.schema_migrations (filename, checksum) values ('$name', '$sum');"
        echo "commit;"
    } | psql_ddl -f - > /dev/null
done

verify_checksums

echo "apply: ${#pending[@]} migration(s) applied to $DATABASE"
