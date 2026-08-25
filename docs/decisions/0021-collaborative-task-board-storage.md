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
schema and uses the real `dami_app` role. Eighteen focused tests demonstrate recursive
round-trip, ordered and priority sibling sorting, single-winner concurrent claims,
prerequisite gating, acceptance evidence, child-gated completion, cycle rejection,
least-privilege create/read/claim, ordered append-only activity, block/reopen behavior,
task-derived board summaries, exact retry convergence, conflicting-id refusal,
planning-provenance round-trip, derived detail status, audited-only runtime mutation,
and bounded task count/depth. The earlier complete persistence suite passed 245/245
on the shared tree; the current focused task-board suite passes 18/18. A combined
isolated candidate built with 0 warnings and 0 errors; its whole-solution test gate
remained red only where concurrent uncommitted corpus/frontier work was intentionally
excluded, so O1a was not marked complete or committed at that checkpoint.

## Consequences

Containment uses `parent_task_id`; prerequisites use a separate same-board edge table.
`SubTasks` is assembled as a recursive collection of the same `BoardTask` type. A
parent owns the sort mode for its children. Claims and mutations compare a monotonically
increasing task version, so competing agents cannot both win. `Done` is not a generic
status update: the store requires the claimant, satisfied criteria, completed/cancelled
children, and completed prerequisites. Every successful mutation appends actor/time
evidence in the same SQL transaction. Reads use a repeatable-read snapshot and load
each relation once, avoiding recursive N+1 queries.

The runtime role cannot update task or criterion state directly. Four
schema-qualified, empty-search-path `SECURITY DEFINER` functions own guarded workflow
updates and their matching activity inserts; public execution is revoked. Boards are
bounded to 1,024 tasks and 64 containment levels because reads intentionally return a
complete point-in-time tree. Board status is derived from task state rather than
persisted twice.

Agent-generated boards also persist the planner route, disclosure class, and execution
origin. Directly created human boards may omit that grouped provenance, but the schema
rejects partially populated provenance.

Planning remains an application service above `ITaskBoardStore`; local, frontier, and
Dami-routed planner adapters produce the same provider-neutral proposal and never own
persistence.

## Reversal path

The API contracts do not expose table layout. A materialized-path or closure-table
index can be added and backfilled from `parent_task_id` without changing callers. If
boards become too large for whole-tree reads, add paged descendant queries while
retaining task identities and prerequisite edges. Migrating to document storage would
require replacing row-level mutation semantics and is intentionally the expensive path.
