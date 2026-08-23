# ADR 0003 — Nightly verified pg_dump as the interim database backup

- **Decision:** Back the PostgreSQL cluster up nightly with `pg_dump`, verify each archive, keep 14 days on the local disk. This is an interim measure that answers one third of the open backup decision.
- **Date:** 2026-08-22
- **Status:** accepted as an interim measure — **does not close** the register's open decision on backup destinations, encryption, and retention
- **Supersedes:** none

## Context

The database had **no recurring backup at all**. ADR-0002 put Timeshift on the host, but
Timeshift excludes `/home`, and the cluster's data directory is
`/home/steve/Data/pgsql-dami-data`. That exclusion is correct — an rsync copy of a live
data directory is not a backup — but it left the gap uncovered rather than covered.

The only copy in existence was a single manual `pg_dumpall` taken before the PGDG
upgrade. The cluster is currently near-empty, so the cost of the gap today is nil. It
stops being nil the moment the 7,000-memory corpus lands in Phase 2, and that is the
wrong moment to be designing a backup.

The register lists "backup destinations, encryption, and retention schedule" as an open
decision. **This ADR does not settle it.** It puts a verified local copy in place so the
gap is not open while the larger question is answered.

## Alternatives considered

| Option | Strengths | Weaknesses | Why not chosen |
|---|---|---|---|
| **Nightly `pg_dump`, local, verified** | Logical, portable across major versions, selectively restorable, verifiable without a second machine. Costs nothing and needs no decision about off-host storage. | Same disk as the database. Point-in-time granularity is one day. No encryption. | — chosen, as interim |
| Continuous archiving / PITR (`wal_level=replica` + `archive_command`) | Recovery to a point in time, not to last midnight. The right answer for a system carrying irreplaceable personal data. | Needs a defined archive destination, retention policy, and restore rehearsal — exactly the open decision. Meaningful setup against an empty database. | Right destination, wrong moment. Revisit before Phase 2 loads the corpus. |
| Filesystem snapshot of the data directory | Cheap, already have Timeshift. | A snapshot of a running cluster is a crash-consistent copy, not a backup, and Postgres may refuse to start from one. ADR-0002 says this explicitly. | Not a backup |
| Nothing until the backup decision is made | No wasted work. | Leaves a known gap open across the whole of Phase 2 setup. | The gap is free to close now |

## Evidence

Implemented as `tools/backup/dami-pg-backup.sh` in the repository, installed to
`/usr/local/bin/dami-pg-backup`, run by `dami-pg-backup.timer` at 02:30 with
`Persistent=true` so a missed run happens at next boot.

**Verified rather than assumed.** A restore was actually performed — which is the thing
ADR-0002's Timeshift path still has not done:

```
$ pg_restore --dbname=dami_restore_probe --no-owner dami-data-20260823T030013Z.dump
$ psql -d dami_restore_probe -tAc "select nspname from pg_namespace where nspname='dami'"
schema: dami
$ psql -d dami_restore_probe -tAc "select extversion from pg_extension where extname='vector'"
extension: vector 0.8.6
$ sha256sum -c *.sha256
dami-data-20260823T030013Z.dump: OK
globals-20260823T030013Z.sql: OK
postgres-20260823T030013Z.dump: OK
```

The probe database was dropped afterwards. The service was then run through systemd
rather than by hand, and reported `2 database(s) ... verified, keeping 14d`.

Design points worth stating:

- **Every archive is verified at creation.** `pg_restore --list` must succeed or the run
  fails loudly. A dump that cannot be listed is not a backup, and retaining an unreadable
  file that looks like protection is worse than having none.
- **Retention runs only after every dump succeeds**, so a failed run can never delete the
  last good copy.
- `pg_dumpall --globals-only` captures roles. Restoring database dumps without it leaves
  tables owned by roles that do not exist.
- Peer authentication over the local socket. No password is read, stored, or passed.
- **14 days is an arbitrary number**, chosen because it spans a fortnight of iteration.
  It is not derived from a recovery objective, because none has been stated.

## Consequences

**Easier.** The database is recoverable to last midnight. Restores are proven, not
assumed. Archives are portable across PostgreSQL major versions, which matters while the
16-versus-17/18 question is open.

**Harder.** Nothing, at current size. When the corpus lands, a full logical dump of
embeddings will be substantially slower and larger, and that is the natural trigger to
revisit PITR.

**Three limits, stated plainly rather than left to be discovered:**

1. **The backups sit on the same physical disk as the database.** This protects against
   a bad migration, a dropped table, or a corrupted extension upgrade. **It does not
   protect against drive failure.** Neither does Timeshift, for the same reason. This
   host currently has no defence against losing `nvme0n1`.
2. **The archives are unencrypted**, and `globals-*.sql` contains SCRAM verifiers for
   every role. They are mode `0600` and owned by `postgres`, which is adequate on-host
   and **not** adequate for any off-host destination. Encryption must be settled before
   these leave the machine.
3. **One-day granularity.** Work lost between the last run and a failure is gone.

**Locked in.** Nothing. Logical dumps constrain no future choice.

## Reversal path

Disable and remove three files: `systemctl disable --now dami-pg-backup.timer`, then
delete the units and `/usr/local/bin/dami-pg-backup`. The script in the repository is the
source of truth; the installed copy exists so the unit does not depend on traversing
`/home/steve`, which is permitted today only by an ACL entry (`user:postgres:--x`).

Adopting PITR later does not require undoing this — the two coexist, and a logical dump
remains the portable form.
