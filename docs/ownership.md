# Who is working on what

Two agents work in this repository at once. This file is the claim board: check it
before you start, add a line before you touch shared state, and clear the line when you
are done.

`AGENTS.md` is the authority on method. `docs/status.md` records project state.
`docs/work-log.md` records history. **This file records only what is in flight right
now**, and it should be short. If it is long, something was not cleared.

- **Last updated:** 2026-08-22 22:30 CDT

## In flight

| Owner | Area | Paths | Since |
|---|---|---|---|
| **Codex** | Transport slice, architecture §7.5.5 steps 3–5 | `Dami/src/Dami.Contracts/Transport/**`, `Dami/src/Dami.Transport/**`, `Dami/tests/Dami.Transport.Tests/**` | 2026-08-22 |
| **Claude Code** | Phase 2 data foundation (done) and the proactive tier — contracts, pass runner, surfacing queue | `tools/ddl/**`, `Dami/src/Dami.Contracts/Events/**`, `Dami/src/Dami.Contracts/Memory/**`, `Dami/src/Dami.Contracts/Proactive/**`, `Dami/src/Dami.Persistence/**`, `Dami/src/Dami.Proactive/**`, `Dami/tests/Dami.Persistence.Tests/**`, `Dami/tests/Dami.Proactive.Tests/**` | 2026-08-22 |

## Held by Claude Code, not in active change

Host infrastructure: PostgreSQL, the inference sidecars, Docker and the GPU stack,
Timeshift, backups, `.editorconfig`, `Dami/Directory.Build.props`,
`Dami/src/Dami.Analyzers`, `Dami/tests/Dami.Architecture.Tests`, `tools/eval`,
`docs/workstation-runbook.md`.

Change them if you need to — say so in `work-log.md`. The flags on the running services
exist for reasons that are not visible from the `docker run` line; runbook §4 explains
which.

## The one file both of us must edit

`Dami/Dami.sln`. Use `dotnet sln add`, never a hand edit, and commit it in the same
change as the project it references. It is the most likely conflict in the repository.

## Rules

1. **Stage explicitly by path.** `git add -A` sweeps up the other agent's in-flight work.
   This has already happened once — commit `7d3b508` captured Codex's `Dami.sln`,
   `Dami.csproj` and `Program.cs` under an unrelated message.
2. **`git pull --rebase` before you start.** Small, frequent commits here.
3. **Do not edit a file inside another owner's paths** without adding a line to
   `work-log.md` saying why.
4. **Contracts are shared ground.** `Dami.Contracts` has one directory per concern —
   `Transport/`, `Events/`. Add files to your own directory rather than editing another
   owner's, so git can merge.
5. **The `dami` schema is shared.** Coordinate before creating or dropping objects; drop
   your scratch tables.
6. **`chown -R steve:steve` after creating files.** Agents run as root, the repository
   belongs to steve, and root-owned files break the other agent's `git add` and lock
   Steve out of his own tree. `.githooks/post-commit` does this automatically — set
   `git config core.hooksPath .githooks` in your clone, it is local config and does not
   travel.
7. **Build and test before committing anything under `Dami/`** — `CLAUDE.md`, "Build and
   test — mandatory". There is no CI; this is the only gate.
