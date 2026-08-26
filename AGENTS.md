# Dami Core Agent Rules

Read `docs/onboarding.md` before working in this repository. Current architecture
and decisions take precedence as described there.

Work is found, claimed, and completed on the PostgreSQL task board:
`DAMI_ACTOR=codex DAMI_ACTOR_KIND=Agent dami board dami --open`, then
`dami board claim <id8> "<plan>"`, `dami board needs <id8> "<criterion>"`, and
`dami board complete <id8> "<evidence>"`. `TODO.md` trails the board; do not claim there.

## Development method

- Use strict test-driven development for every behavior change:
  1. Add or change one test.
  2. Run the narrowest relevant test and record the expected failure.
  3. Add the minimum production code needed to pass.
  4. Run the narrow test, then the affected suite, and record the results.
  5. Refactor only while the tests remain green.
- A test added after its implementation is coverage, not TDD. Do not describe it as
  TDD.
- Follow SOLID design. Keep interfaces focused, dependencies directed toward
  abstractions, and classes limited to one reason to change.
- Never weaken, delete, skip, or rewrite a valid test merely to make production code
  pass.
- Do not claim an interrupted, cancelled, or timed-out test passed.

## Work log

- Append an entry to `docs/work-log.md` for every work session and meaningful action.
- Identify the acting agent, the files affected, commands run, observed results, and
  any decisions, failures, or deviations.
- Log planned or started work before changing production code. Append verification
  evidence after commands finish; never rewrite history into a cleaner story.
- `docs/status.md` records current project state. The work log records who did what.
  Update status only when observed project state materially changes.

## Standing constraints

- No underscore prefixes on fields. Use `this.` for instance-member access.
- Do not add packages, projects, or architectural boundaries without explicit scope.
- Do not modify unrelated files or overwrite concurrent work.
- Never commit, push, or open a pull request unless explicitly asked.
- Never add AI co-author attribution or tool branding to version-control metadata.
