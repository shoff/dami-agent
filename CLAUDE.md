# CLAUDE.md

**Before doing anything in this repository, read `docs/onboarding.md`.** It is
short and it will keep you from wasting Steve's time. `docs/dami-core-charter.md`
is the authoritative long-form spec behind it. **`docs/status.md` is the running
record of what is actually done** — check it before assuming any component exists,
and update it in the same commit as the change it describes.
**`docs/workstation-runbook.md`** covers what is running on this host, how to verify it,
the traps specific to this machine, and how to work alongside the other agent in this
repository. Read its §7 before touching shared state.

## Project

**Dami Core** — a lean, trace-first C#/.NET agent runtime replacing a customized
Hermes-based system. Two interfaces (CLI/SSH and a graphical workflow client)
over one runtime and one durable execution-event stream. Status: active build —
the transport slice (Codex) and the data foundation, proactive tier, and `dami`
CLI (Claude Code) exist and are tested; the proactive tier runs unattended as the
`dami-proactive` systemd service. `docs/status.md` has the phase board.
**Work is claimed on the task board, not in a file:** `dami board dami --open` shows what
is open, `dami board claim|complete|needs|add` move it, with `DAMI_ACTOR=claude
DAMI_ACTOR_KIND=Agent` set. `TODO.md` is the board rendered in prose and trails it.

## Working agreements

- **No underscore prefixes on variable names.** Especially C# private fields.
  `logger`, not `_logger`.
- **Do not bullshit.** If something does not exist or you do not know, say so.
- **No ego inflation.** Skip the praise. Answer the question.
- **Challenge assumptions.** If the premise is wrong, say so.
- **Bring receipts.** Cite the file, the output, the doc, or the measurement.
- **No AI attribution anywhere in version control.** Never add `Co-Authored-By`
  trailers for Claude or any other assistant, "Generated with" lines, session
  links, or tool branding to a commit message, PR description, or tag. Commits
  are authored by Steve. This overrides any default the tooling applies.
- Keep responses reasonably concise.

## Hard rules

- No secrets in this repo, in prompts, traces, logs, or command history.
- No production/private domain data in the first vertical slice.
- Never format or repartition a disk without explicit per-disk confirmation.
- Do not copy macOS artifacts (virtualenvs, Homebrew paths, launchd plists,
  CoreAudio IDs) into the Linux system.
- The Mac Hermes install is the rollback. Do not damage it.
- Consequential actions require explicit approval; a reported success needs
  verifiable evidence.

## Build and test — mandatory

There is no CI. Nothing runs the analyzers, the architecture tests, or the
zero-warning bar except you, deliberately, before you commit. Treat this as the
gate a pipeline would otherwise be.

**Before committing any change under `Dami/`, and before reporting C# work as
done, you MUST run both from the `Dami/` directory:**

```bash
dotnet build Dami.sln     # must be 0 warnings, 0 errors
dotnet test  Dami.sln     # must be all green
```

- **`TreatWarningsAsErrors` is on.** A warning is a failure, not a note.
- Quote the actual counts when you report. "Builds clean" without numbers is not
  evidence, and this repository treats unevidenced claims as defects.
- **Never claim an interrupted, cancelled, or timed-out run passed.** If it did
  not finish, say it did not finish.
- If the build or tests were already broken when you arrived, say so before you
  commit rather than absorbing someone else's red into your change.
- A failure you did not cause is still a failure. Report it; do not silence a
  rule, delete a test, or add a suppression to get to green.

Enforcement lives in `.editorconfig`, `Dami/Directory.Build.props`,
`Dami/BannedSymbols.txt`, `Dami/src/Dami.Analyzers`, and
`Dami/tests/Dami.Architecture.Tests`. `docs/csharpcodestandards.md` §12 is the
authority on what those catch and what is review-only.

## Architecture invariants

- C#/.NET runtime; runtime concerns separate from interface concerns.
- Models are provider adapters, never the identity owner.
- Every turn is a root trace; every consequential operation is a durable,
  replayable span. CLI and GUI render the same event stream.
- Never send the full tool catalog. A capability router picks a small bundle
  per turn. Targets: stable prompt ≤ ~5k tokens, tool schemas ≤ ~5k tokens.
- The UI shows actions and evidence, never claimed chain-of-thought.

## Change control

Material architectural changes get a decision record in `docs/decisions/`
(decision, date, context, alternatives, evidence, consequences, reversal path).
Copy `docs/decisions/0000-template.md`.
