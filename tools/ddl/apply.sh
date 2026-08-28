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

# Runs as the schema owner. The schema itself is provisioned administratively;
# dami_ddl owns objects within dami but intentionally cannot create schemas.
#
# The role is named explicitly. Without it psql defaults to the login user, and there is
# no 'steve' role in this cluster: every connection failed, the ledger lookup below
# swallowed the error, and the script reported "applied: (none)" with all 34 migrations
# pending against a database that already had every one of them. Running it in that state
# would have replayed 001 over live data. Never let the ledger read fail quietly.
DDL_ROLE="${DAMI_DDL_ROLE:-dami_ddl}"
PASSFILE="${PGPASSFILE:-$HOME/.pgpass}"
psql_ddl() {
    psql "host=127.0.0.1 port=5432 dbname=$DATABASE user=$DDL_ROLE passfile=$PASSFILE" \
        --no-psqlrc --quiet --set=ON_ERROR_STOP=1 "$@"
}

if ! psql_ddl -tAc 'select 1' > /dev/null 2>&1; then
    echo "apply: cannot connect to '$DATABASE' as '$DDL_ROLE'." >&2
    echo "       Check $PASSFILE, or set DAMI_DDL_ROLE. Refusing to guess what is applied." >&2
    exit 1
fi

# No '|| true' here. A ledger this script cannot read is a ledger it must not act on.
applied="$(psql_ddl -tAc "select filename from dami.schema_migrations")"

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
