# ADR 0036 — The frontier changes its own code, on a branch, never in the tree

- **Decision:** The frontier bundle gains three tools — `change_code`, `explain_code`,
  `list_code_changes` — behind `CodeWork:Enabled` (off by default). `change_code` hands a
  task to the Codex subscription's coding agent in a fresh `git worktree` on a stamped
  `dami/…` branch under `~/.local/share/dami/code`, in a `workspace-write` sandbox; this
  host then runs `dotnet build` itself, commits on the branch, and reports branch,
  diffstat, build verdict and the agent's summary. Nothing is merged, pushed, or deployed:
  the branch is the proposal and Steve's merge is the approval. `ICodeWorker` is the seam
  (`Dami.Contracts.Code`); `CodexCodeWorker` (`Dami.Providers`) is the door; every run
  is an egress event carrying its purpose and never the task text.
- **Date:** 2026-09-16
- **Status:** accepted
- **Extends:** ADR-0030 (the frontier gets tools), ADR-0011 (the subscription door),
  ADR-0027 (a door is gated the same whichever way it faces)

## Context

Steve, 2026-09-16: *"I would like the discord bot to have the ability to create and edit
code. Especially it's own code in ~/dev/dami-agent."*

What existed: the frontier's per-turn bundle (pictures, recall, remember, schedule,
fitness, research, today), delivered to Discord through the Codex app-server's dynamic
tools; a native `propose-file-patch` / `read-file` / `run-process` tier for the *local*
model loop, rooted at `/home/steve/DamiWorkspace`, with a 4-call budget and a 15 s
executor; an approval contract (`IApprovalService`) with no route into a frontier turn;
and D-016, which forbids self-authored *tools* with write capability and says the
codebase audit proposes and does not commit.

This request is a different thing from D-016's. A self-authored tool is code Dami wrote
that executes again tomorrow at agent privilege, unobserved. A code change on a branch is
text Dami wrote that executes nowhere until a human merges and deploys it. The first is a
persistence hazard; the second is a pull request.

Two agents already share the main working tree of this repository (runbook §7; commit
`7d3b508` is the recorded collision). A third writer in that tree was never an option.

## Alternatives considered

| Option | Strengths | Weaknesses | Why not chosen |
|---|---|---|---|
| Expose the native `propose-file-patch` / `run-process` tools to the frontier | Exists; approval-gated writes | Per-file patches from a chat model over Discord, no build/test loop, 4 calls a turn, 15 s executor, rooted outside the repository; approvals resolve in the GUI, not in the conversation | Wrong shape for "fix the bug in X" |
| Run the coding agent in the main working tree | "Its own code", literally | Collides with Codex's and Claude's in-flight edits; a half-finished task leaves the shared tree dirty; nothing distinguishes Dami's change from anyone else's | Runbook §7 already forbids it in effect |
| `IApprovalService` request per change, applied on approval | Uses the contract | The frontier tool must return while the turn waits; the approval would resolve in another surface, and the thing to approve is a diff nobody has seen yet | The branch is a better artifact to approve than a description |
| Codex's own `--worktree` flag | One flag | Branch name and location chosen by the CLI; nothing to list, nothing stamped, nothing to tell Steve | Determinism is the point of the report |
| Asynchronous job with later delivery to the channel | No turn deadline pressure | New machinery (job store, delivery, progress) for v1; the scheduled-delivery path exists but carries prompts, not results | Deferred; v1 is bounded synchronous work inside the 600 s turn |
| A worktree per task on a `dami/` branch, this host builds and commits, merge is Steve's (chosen) | Isolated, reviewable with plain git, reversible by branch delete, every run recorded, off by default | Worktrees accumulate; a task that outruns 360 s is reported as such with whatever landed | — |

## Evidence

- `CodeToolsTests` (12): tools hidden when off, offered when on; task handed through;
  branch, diffstat and build in the result; "not merged" stated in the model's own result;
  a refusal becomes a failed result, not an exception.
- `CodexCodeWorkerTests` (26): refuses when code work, the subscription, or the budget
  says no, touching nothing; adds a worktree on `dami/<yyyyMMdd-HHmm>-<slug>` from `main`;
  runs codex with `--sandbox workspace-write --cd <worktree>` and never in the repository;
  the prompt carries the task and "Do not commit. Do not push. Do not add packages";
  a clean tree means no build and no commit; a dirty tree means build, commit, diffstat;
  a failed build is reported and the work kept; completed and failed events carry no task
  text; `explain_code` runs read-only in the repository and touches no git;
  `list_code_changes` reads the prefixed refs newest first.
- `FrontierToolBundleTests`: the three names dispatch to `IFrontierCode`; the bundle is
  fifteen tools plus whatever the worker offers.
- `EgressSeamTests.Codex_Process_Holders_Should_Be_The_Pinned_Set` pins who may spawn
  the CLI: `CodexProcess`, `CodexChatClient`, `CodexSubscriptionImageGenerator`,
  `CodexCodeWorker`.
- The research tripwire in `FrontierToolBundle` (`untrustedResearchSeen`) already refuses
  every later tool in a turn once a page or search result has entered it, so a fetched
  page cannot drive `change_code` in the same turn.
- **Not verified live.** `CodeWork:Enabled` is unset on this host; no task has been run
  against the real CLI. The first real branch is the proof.

## Consequences

- One drop-in line, `CodeWork__Enabled=true`, and Steve can say on Discord "fix the typo
  in the About window" and get back a branch name, a diffstat, this host's build verdict,
  and a summary — then review with `git diff main...dami/…` and merge or delete.
- The coding agent runs as `steve`, inside Codex's `workspace-write` sandbox, in a
  directory that is not the repository. What it can do to the rest of the machine is what
  that sandbox allows; the receipt (`dotnet build`) is this host's, not the agent's word.
- The commit author is the repository's git identity, which is Steve's, as every commit
  here is. No attribution lines are added.
- A task is bounded at 360 s plus a 180 s build inside the turn's 600 s. Larger work
  should be asked for in pieces; a timeout keeps whatever landed on the branch and says so.
- Worktrees accumulate under `~/.local/share/dami/code` until removed
  (`git worktree remove`). `list_code_changes` shows what is there.
- The frontier can now change `CodeTools` itself — on a branch. Behaviour only changes
  when Steve merges and deploys, which this ADR leaves exactly where it was.

## Reversal path

Unset `CodeWork__Enabled` and the tools are not offered on the next turn. Removing the
door is five contract files, two providers files, one Core file, the bundle's one
parameter and three dispatch lines, and the pinned holder.
