# CLAUDE.md

**Before doing anything in this repository, read `docs/onboarding.md`.** It is
short and it will keep you from wasting Steve's time. `docs/dami-core-charter.md`
is the authoritative long-form spec behind it.

## Project

**Dami Core** — a lean, trace-first C#/.NET agent runtime replacing a customized
Hermes-based system. Two interfaces (CLI/SSH and a graphical workflow client)
over one runtime and one durable execution-event stream. Status: Phase 0/1,
planning and workstation validation. No source code exists yet.

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
