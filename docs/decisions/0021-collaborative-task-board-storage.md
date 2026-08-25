# ADR 0021 — Relational recursive task boards with optimistic claims

- **Decision:** Store feature requests, plans, recursive tasks, acceptance criteria,
  prerequisite edges, claims, and append-only activity in normalized PostgreSQL
  relations; expose one recursive task contract and use optimistic task versions for
  concurrent human/agent mutations.
- **Date:** 2026-08-24
- **Status:** accepted
- **Supersedes:** none

## Context

Steve requested a shared PDLC surface that replaces Markdown as the machine-readable
claim board: an agent receives a feature request, creates a plan and task hierarchy,
and humans and agents then claim and advance the same work through web and desktop
clients. A subtask has the same behavior and data as a root task. Prerequisites form a
graph distinct from containment, while sibling work is either explicitly ordered or
priority-sorted. Concurrent agents make last-write-wins updates unacceptable.

## Alternatives considered

| Option | Strengths | Weaknesses | Why not chosen |
|---|---|---|---|
| JSON document per board | Direct recursive shape; one-row write | Whole-board contention; weak foreign keys and queries; difficult concurrent claims | The UI shape should not become the storage/concurrency boundary |
| PostgreSQL `ltree` hierarchy | Efficient ancestor/descendant queries | Adds an extension and path-rewrite semantics; prerequisites still require relations | Current operations load bounded boards and do not justify path materialization |
| Adjacency-list tasks plus relations | One task type; ordinary foreign keys; row-level concurrency; direct status queries | Tree assembly occurs in C#; moving a subtree needs explicit cycle checks | Chosen |
| Separate task/subtask tables | Superficially simple root queries | Duplicates every behavior and prevents arbitrary depth | Violates the requested recursive abstraction and DRY |

## Evidence

The initial integration suite exercises the repository's deployed DDL in a throwaway
schema and uses the real `dami_app` role. Twelve focused tests demonstrate recursive
round-trip, ordered and priority sibling sorting, single-winner concurrent claims,
prerequisite gating, acceptance evidence, child-gated completion, cycle rejection,
least-privilege create/read/claim, ordered append-only activity, block/reopen behavior,
and task-derived board summaries. The complete persistence suite passed 245/245 on the
shared tree. A combined isolated candidate built with 0 warnings and 0 errors; its
whole-solution test gate remained red only where concurrent uncommitted corpus/frontier
work was intentionally excluded, so O1a was not marked complete or committed.

## Consequences

Containment uses `parent_task_id`; prerequisites use a separate same-board edge table.
`SubTasks` is assembled as a recursive collection of the same `BoardTask` type. A
parent owns the sort mode for its children. Claims and mutations compare a monotonically
increasing task version, so competing agents cannot both win. `Done` is not a generic
status update: the store requires the claimant, satisfied criteria, completed/cancelled
children, and completed prerequisites. Every successful mutation appends actor/time
evidence in the same SQL transaction. Reads use a repeatable-read snapshot and load
each relation once, avoiding recursive N+1 queries.

This design does not yet choose how an LLM produces a plan. Planning is an application
service above `ITaskBoardStore`; provider adapters must not become persistence owners.

## Reversal path

The API contracts do not expose table layout. A materialized-path or closure-table
index can be added and backfilled from `parent_task_id` without changing callers. If
boards become too large for whole-tree reads, add paged descendant queries while
retaining task identities and prerequisite edges. Migrating to document storage would
require replacing row-level mutation semantics and is intentionally the expensive path.
