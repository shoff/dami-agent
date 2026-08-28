# ADR 0023 — The runtime may work a board task, advisorily

- **Decision:** A board task can be handed to the runtime for one bounded turn that
  proposes and traces; it may not claim, complete, or change the status of that task.
- **Date:** 2026-08-28
- **Status:** proposed
- **Supersedes:** none

## Context

The task board (ADR-0021) is a ledger of intent shared by Steve, Claude, and Codex.
Nothing consumed it: no proactive service and no runtime path read a claimed task and
acted on it. Verified by inspection — the only consumers of `ITaskBoardStore` are the
TODO.md importer, the feature planner, the CLI verbs, the GUI panel, the Host endpoints,
and the Postgres store, and none of the ten `IProactiveService` implementations touches
the board at all.

The consequence was a user-visible dead end. Steve, looking at the desktop client:
"well how do I tell the application that I want it to work on a task NOW". The honest
answer was that he could not, and that work on a board task happened only because he
typed an instruction to an agent in a different program entirely. The board displayed
work it had no way to start.

Closing that gap turns the runtime into an actor on its own board, which is a change in
kind rather than degree — hence this record.

## Alternatives considered

| Option | Strengths | Weaknesses | Why not chosen |
|---|---|---|---|
| **Advisory run (chosen)** | No new trust boundary; reuses `ITurnRunner` and its existing tool budget; useful immediately for the ~11 `[STEVE]` decisions, which are mostly "draft/approve this ADR" | Cannot actually finish work; a proposal still needs a hand | Chosen: it is the whole of the value that can be had without a security design |
| Executing run — repo write, git, `dotnet build`/`test` | Would genuinely finish tasks | Grants an agent write access to the repository and shell on the workstation; needs a sandbox, per-action approval, output caps, and a kill switch; the Host's current allowlist (no shell, no git, 4 tool calls, 15 s) is nowhere near it | Deferred to its own ADR. It is a trust-boundary decision, not a feature |
| Queue + background worker | Long tasks would not block a request | Adds a scheduler, a retry policy, and a cancellation story before anything is known about whether the runs are useful | Premature. A synchronous run already streams into the execution graph |
| Do nothing; keep driving through Claude Code and Codex | Zero risk | Leaves the product showing work it cannot start | Rejected: this is the specific complaint |

## Evidence

- No board consumer among the proactive services: `grep -rln "ITaskBoardStore\|task-boards"`
  over `Dami/src` returns importer, planner, CLI, GUI, Host endpoints, and store only.
- The deployed Host's native tool budget, from
  `/etc/systemd/system/dami-host.service.d/native-tools.conf` and runbook §"dami-host":
  file access rooted at `/home/steve/DamiWorkspace` (not the repository), 64 KiB caps,
  process allowlist of `pwd` and `printf` only, 15-second executor timeout, at most four
  tool calls per turn. An executing run is not possible within it, which is what makes
  the V1/V2 split real rather than cautious.
- The completion gate this decision refuses to touch is SQL, from migration 028: a task
  goes Done only when `not exists` an unsatisfied criterion, `not exists` an unfinished
  child, and `not exists` an incomplete prerequisite.
- Board shape at the time of writing: 212 tasks — 170 Done, 20 Open, 14 Blocked,
  7 InProgress, 1 Cancelled — of which about 11 carry a `[STEVE]` marker.

## Consequences

**Easier.** A task can be turned into a proposal from the desktop client without leaving
it. The run is bracketed on the board by `TaskWorkStarted` / `TaskWorkFinished` carrying
the actor and the trace id, so "what did the machine do about this" is answerable from
the ledger, and the run streams into the execution graph while it happens.

**Harder.** The board now contains rows that no hand wrote, so "who did this" needs the
actor kind to be read, not assumed. Activity volume grows with every run, including
failed ones — deliberately, since a run that threw still happened.

**Locked in.** Two new activity kinds and a `task_board_log_work` function (migration
034). The append-only trigger means those rows can never be edited or deleted.

**Cost.** One turn's tokens per press, and a synchronous request that lasts as long as
the turn does.

**Explicitly not granted.** The run cannot claim, complete, block, cancel, or edit a
task; it cannot write files or run processes beyond the interactive turn's existing
allowlist; and the prompt states the advisory boundary so the model does not report work
as finished. `TaskWorkService` is pinned by tests that assert it logs only work events.

## Reversal path

Cheap. Remove the route, the service, and the GUI button; the two activity kinds and the
function become inert. The historical rows stay — the table is append-only by design —
but they are labelled and self-describing, so they read as history rather than as
corruption. Nothing else in the system depends on them. Migration 034 is additive: it
widens a check constraint and adds one function, so nothing written before it becomes
invalid, and a `035` narrowing the constraint again would only need to run after those
rows were tolerated or archived.
