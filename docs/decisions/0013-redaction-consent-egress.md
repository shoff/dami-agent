# ADR 0013 — Consent is the transform: how LocalOnly context earns Egressable

- **Decision:** A memory-informed prompt becomes Egressable through exactly one door: a locally-drafted redacted brief whose exact bytes Steve reviews and approves, hash-pinned so that what egresses is provably what was reviewed. Nothing else reclassifies LocalOnly content — the `ModelRouter` rule stays unconditional.
- **Date:** 2026-08-23
- **Status:** accepted (mechanism); the *default posture* — whether `dami chat` ever offers a brief unprompted — is deliberately not decided here
- **Supersedes:** none. Builds on ADR-0010 (two-door egress), ADR-0011 (subscription frontier), and the G7 approval contract.

## Context

D-012 draws a hard line: LocalOnly never reaches a frontier, enforced unconditionally
in the router. That protects Steve completely and helps him incompletely — the
frontier door (ADR-0011) exists but only for bare questions with no memory of him.
The register called the redaction/consent step "the highest-leverage open design in
the suite" because it unlocks frontier-quality answers *about Steve's actual
situation* without abandoning the line.

## The design

Three properties, each carried by a different component:

1. **Redaction is a draft, not a guarantee.** `PromptRedactor` has the local model
   rewrite question + retrieved context into a self-contained brief ("the user",
   no names, technical content intact). A local model cannot be trusted to catch
   everything — which is why redaction alone converts nothing.
2. **Consent is the transform.** The brief's exact bytes are stored with their
   SHA-256 behind a G7 approval (`dami brief` prints the full text; `dami
   approve`/`deny` resolves it, durable and single-resolution). The Egressable
   classification is *created by the approval of those bytes*, not by any
   property of the text.
3. **The executor is paranoid.** `BriefExecutor` sends only behind an Approved
   approval, recomputes the hash at send time, and refuses on mismatch — nothing
   can swap the reviewed bytes between approval and egress. The send itself goes
   through the ADR-0011 door, so it inherits the egress event trail and the C5
   budget.

## Alternatives considered

| Option | Why not |
|---|---|
| Trust the redactor (auto-egress redacted prompts) | A local model's redaction failures are exactly the leaks D-012 exists to prevent; "probably clean" is not a privacy class |
| Per-memory consent flags (mark memories Egressable at rest) | Reclassifies the corpus permanently for a per-question need; consent should cover a *composition*, not a category — a harmless fact plus another harmless fact can be identifying together |
| Interactive review in the chat flow | The CLI is not interactive mid-turn today; the approval queue already models "human decides later" and G7 built the machinery |

## Evidence

`dami brief` → full text + hash printed → `dami approve <id8>` → answer, live.
Executor refusals pinned by test: Pending refused, Denied refused, tampered bytes
refused with no frontier call. Store round-trip and answer recording pinned in
persistence tests.

## Consequences

Frontier chat about Steve's real context is now possible, one reviewed brief at a
time. The cost is friction by design: two commands and a read. G9 (frontier-informed
turns) can build on this; if the friction proves wrong in practice, the *posture*
can change without touching the guarantee — the hash-pinned consent artifact stays.

## Reversal path

Remove the `brief` verb and the executor hook; the migration keeps its (inert) rows
as the audit record of what was ever sent. The router rule never changed, so there
is nothing to re-tighten.
