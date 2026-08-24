# ADR 0017 — Skill changes use a transactional write-ahead ledger

- **Decision:** Persist each version-pinned skill change and its bounded diff in PostgreSQL atomically with a `SkillChangeRequested` execution event before filesystem materialization; converge the filesystem afterward and record a terminal event.
- **Date:** 2026-08-24
- **Status:** accepted
- **Supersedes:** none

## Context

D-016 permits Dami to author, revise, and retire skills without approval, but F4c
requires every diff in the durable execution stream. The execution stream is canonical
in PostgreSQL (D-017), while F4a deliberately makes readable local files the skill
source. A database transaction cannot include a filesystem rename. Recording only
after a rename admits an unrecorded-change crash window; recording only an unqualified
success beforehand can claim a change that never reached disk. Multi-file in-place
revision is also not atomic as a set.

## Alternatives considered

| Option | Strengths | Weaknesses | Why not chosen |
|---|---|---|---|
| Append event after changing files | Simple happy path | Crash can leave an unrecorded change | Violates F4c's audit requirement |
| Append a success event before changing files | Diff is durable first | Event can falsely claim success | Conflates intent with observed outcome |
| PostgreSQL is the only skill store | One transaction for content and event | Loses direct readable/editable files and requires replacing F4a | Too large a reversal without evidence |
| Transactional write-ahead ledger, then convergent materialization | Diff and intent commit together; retries and recovery are explicit | Filesystem state is eventually, not transactionally, consistent with PostgreSQL | Chosen: it is honest about the resource boundary and closes the unrecorded-change window |

## Evidence

The existing `ExecutionEventCommand` supports appending an event inside a caller-owned
PostgreSQL transaction, as demonstrated by approval/file-patch aggregate tests. F4c1
demonstrates version-pinned change contracts and one-swap replacement of the complete
Skill registry source. F4c2 adds migration 020 plus exact event-collision checking;
integration tests demonstrate atomic commit, forced event-write rollback, immutable
rows, SELECT/INSERT-only runtime privileges, and exact/concurrent retry convergence.
Migration 020 is applied live with none pending. F4c3 will add recovery and
materialization evidence when that slice completes. F4c3a now demonstrates complete
same-filesystem staging, atomic Linux directory exchange, durable retirement
tombstones, idempotent convergence after each namespace transition, registry
postcondition verification, and success/failure terminal events. Migration 021 adds a
partial payload-reference index for bounded pending-change scans and is applied live.
F4c3b still owns the native/Host lifecycle demonstration.

## Consequences

`SkillChangeRequested` means the exact diff is durably accepted for materialization,
not that files changed. Its payload reference resolves to an immutable bounded ledger
row. A terminal success event means the version-pinned filesystem operation and source
reload converged; a failure event remains retryable and auditable. Exact retries reuse
the change ID; conflicting retries fail.

The materializer must verify the expected preimage, stage content on the same
filesystem, make an atomic namespace transition where the host supports it, and recover
accepted changes after interruption. Registry publication follows filesystem
convergence and remains an atomic source-snapshot swap.

## Reversal path

Disable lifecycle command registration, leaving the immutable ledger and events as
audit history. Existing filesystem skills continue to load read-only through F4a/F4b.
If PostgreSQL later becomes the authoritative skill-content store, replay ledger rows
into the new representation and retain `skill-change://` payload references as stable
audit identifiers.
