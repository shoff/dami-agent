# ADR 0016 — PostgreSQL 17, migrated now while it is free

- **Decision (proposed):** Move `dami-data` from PostgreSQL 16 to 17. Not for a feature — for the rehearsal. The migration path was proven end to end on 2026-08-24 with the database at 108 MB; doing it now costs about a minute of downtime and proves a path that becomes expensive to discover later.
- **Date:** 2026-08-24
- **Status:** proposed — Steve's call (A6)
- **Supersedes:** none

## Why this is a decision at all

Nothing forces it. PostgreSQL 16 is supported until November 2028. For a 108 MB
single-user database with one extension, the technical differences between 16, 17,
and 18 are close to irrelevant — no query in this system is limited by the server.

The argument is the one this project already makes everywhere else: **a migration path
must exist before it is needed.** ADR-0009 versioned embeddings per row so the model
could change; B10 built a repair sidecar rather than mutate history; the belief ledger
supersedes rather than overwrites. A major-version upgrade is the one migration nobody
had rehearsed, and it gets harder every month the corpus grows.

## The rehearsal, performed

A PostgreSQL 17.11 cluster was created alongside the live one (port 5433, untouched
production on 5432) and the live database restored into it:

| step | result |
|---|---|
| `pg_dump -Fc` of live `dami-data` | 38s, 34 MB |
| restore into 17.11 | 21s, **0 errors** |
| `vector` extension | 0.8.6 present and working |
| nearest-neighbour query | ran, returned results |
| row counts (observations, conclusions, events, embeddings) | **all matched exactly** — 7,092 / 5 / 477 / 7,051 |
| append-only guards | **intact** — DELETE and UPDATE both correctly refused on the restored cluster |

The invariant that matters most survived: the restored database still refuses to let
anything mutate history.

## One thing the rehearsal checked and found already correct

A restore fails with 63 permission errors if roles are restored after the schema —
`pg_dump` does not carry roles. I suspected a gap in the nightly backup and there
isn't one: `dami-pg-backup` already runs `pg_dumpall --globals-only` alongside each
database dump, and the current `globals-*.sql` contains both `dami_app` and
`dami_ddl`. The restore order is roles first, then database. Worth stating explicitly
in the runbook, because the failure is confusing if you meet it cold.

## Why 17 and not 18

`postgresql-18` and `postgresql-18-pgvector` are both packaged and would very likely
work. 17 simply has a year more field time under pgvector, and this system's whole
posture is to take the boring option where the exciting one buys nothing measurable.
Revisit 18 at the next rehearsal.

## Consequences

If accepted: `pg_upgradecluster` or dump/restore during a quiet window, re-point the
connection string (it lives in the systemd drop-ins, not the repo), verify with
`dami health` and one `dami chat`. Rollback is the 16 cluster, left in place until the
17 one has run for a week.

If rejected: nothing changes, and the rehearsal stands as proof the path works when it
is eventually needed.
