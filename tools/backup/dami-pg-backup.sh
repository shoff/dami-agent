#!/usr/bin/env bash
#
# Dami Core — PostgreSQL backup.
#
# The cluster's data directory lives under /home, which Timeshift excludes, so host
# snapshots do not cover the database at all. This is the only recurring copy.
#
# Runs as the postgres system user over the local socket with peer authentication, so
# no password is read, stored, or passed. See ADR-0003 for what this does and does not
# answer.
set -euo pipefail

DESTINATION="${DAMI_BACKUP_DIR:-/home/steve/Data/pg-backups}"
# A copy on a SECOND PHYSICAL DEVICE. The cluster and the primary backup directory
# both sit on /dev/nvme1n1p6, so one drive failure loses the database and every copy
# of it. This mirror lives on the root filesystem (a different NVMe), which closes
# that hole. It is NOT the off-host encrypted destination A4 asks for — it survives a
# dead disk, not a fire, a theft, or this machine being wrong. Set to "" to disable.
MIRROR="${DAMI_BACKUP_MIRROR:-/var/backups/dami}"
KEEP_DAYS="${DAMI_BACKUP_KEEP_DAYS:-14}"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"

mkdir -p "$DESTINATION"
chmod 700 "$DESTINATION"

fail() { echo "dami-pg-backup: $*" >&2; exit 1; }

# Roles and their SCRAM verifiers. Restoring a dump without these leaves tables owned by
# roles that do not exist.
globals="$DESTINATION/globals-$STAMP.sql"
pg_dumpall --globals-only --file="$globals" || fail "globals dump failed"
chmod 600 "$globals"

# One compressed custom-format dump per database. Custom format is required for
# selective restore and for pg_restore --list verification below.
mapfile -t databases < <(psql -tAc \
  "select datname from pg_database where datistemplate = false and datallowconn order by 1")

for database in "${databases[@]}"; do
    archive="$DESTINATION/${database}-$STAMP.dump"
    pg_dump --format=custom --compress=9 --file="$archive" "$database" \
        || fail "dump of $database failed"
    chmod 600 "$archive"

    # A dump that cannot be listed is not a backup. Fail loudly rather than retaining an
    # unreadable file that looks like protection.
    pg_restore --list "$archive" > /dev/null \
        || fail "verification of $archive failed - archive is unreadable"

    sha256sum "$archive" > "$archive.sha256"
done

sha256sum "$globals" > "$globals.sha256"

# Retention. Applied only after every dump above succeeded, so a failed run never
# deletes the last good copy.
find "$DESTINATION" -maxdepth 1 -type f \( -name '*.dump' -o -name '*.sql' -o -name '*.sha256' \) \
    -mtime "+$KEEP_DAYS" -delete

mirrored="not mirrored"
if [ -n "$MIRROR" ]; then
    if mkdir -p "$MIRROR" 2>/dev/null && chmod 700 "$MIRROR" 2>/dev/null; then
        # Copy this run's artefacts, then re-verify the checksums at the destination.
        # A mirror nobody checks is a belief, not a backup.
        cp -f "$DESTINATION"/*-"$STAMP".dump "$DESTINATION"/*-"$STAMP".dump.sha256 \
              "$globals" "$globals.sha256" "$MIRROR"/ 2>/dev/null || true
        if (cd "$MIRROR" && sha256sum --quiet --check ./*-"$STAMP".*.sha256 2>/dev/null); then
            find "$MIRROR" -maxdepth 1 -type f \
                \( -name '*.dump' -o -name '*.sql' -o -name '*.sha256' \) \
                -mtime "+$KEEP_DAYS" -delete
            mirrored="mirrored to $MIRROR (verified)"
        else
            # Loud, but not fatal: the primary copy exists and is verified.
            echo "dami-pg-backup: WARNING mirror verification failed at $MIRROR" >&2
            mirrored="MIRROR FAILED"
        fi
    else
        echo "dami-pg-backup: WARNING cannot write mirror at $MIRROR" >&2
        mirrored="MIRROR UNWRITABLE"
    fi
fi

echo "dami-pg-backup: ${#databases[@]} database(s) to $DESTINATION at $STAMP, verified, keeping ${KEEP_DAYS}d; $mirrored"
