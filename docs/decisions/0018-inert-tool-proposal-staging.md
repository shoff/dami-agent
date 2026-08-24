# ADR 0018 — Tool proposals remain inert until verified promotion

- **Decision:** Store each self-authored tool proposal as an immutable, versioned,
  bounded C# source-and-test artifact in PostgreSQL with its rationale, motivating
  observation IDs, typed schema, read-only execution profile, and a transactional
  `ToolProposed` event; staging never writes source to disk, compiles it, loads it, or
  registers it.
- **Date:** 2026-08-24
- **Status:** accepted
- **Supersedes:** none

## Context

D-016 requires source, tests, rationale, and motivating observations in a staging
registry, followed by explicit human promotion. A proposal is arbitrary persistent
code, so treating "stored" or even "approved" as "safe to load in-process" would
collapse the governance boundary. .NET does not provide a trustworthy in-process
sandbox for arbitrary managed code; a declared read-only profile is review metadata,
not proof that source cannot call filesystem, network, process, reflection, or native
APIs.

F5 also crosses three resources with different meanings: the durable review artifact,
the human approval ledger, and the executable live registry. F5a owns only the first.
F5c must define verification and activation before any staged bytes can become code.

The v1 artifact is source rather than an assembly. It contains a typed tool schema,
retrieval tags, one or more UTF-8 C# implementation files, one or more UTF-8 C# test
files, a rationale, and at least one motivating observation ID. The Host will supply a
fixed build envelope later; proposals cannot smuggle project files, package references,
binaries, generated executables, or deployment scripts into staging.

The contract bounds each source/test set to 64 files and 1 MiB of strict UTF-8,
paths to 240 safe ASCII bytes, rationale and parameter schema to 64 KiB each,
description to 4 KiB, tags to 32 entries of 256 bytes, and motivating observations to
64 identifiers. PostgreSQL permits at most 16 MiB for the JSON representation. That
larger storage ceiling accounts for worst-case JSON escaping while remaining a hard
row bound; it does not widen the semantic contract.

## Alternatives considered

| Option | Strengths | Weaknesses | Why not chosen |
|---|---|---|---|
| Load submitted source or assemblies immediately | Fastest path to a running tool | Self-registration; arbitrary code executes before review; no safe in-process sandbox | Directly violates D-016 |
| Create files in the repository at proposal time | Familiar Git review | Lets an unapproved model mutate build inputs; collision and supply-chain surface | Staging must be inert |
| Store a patch only | Easy to inspect with existing file-patch machinery | Does not provide a canonical complete artifact or stable retry identity | Review and verification need exact inputs |
| Immutable source/test artifact in PostgreSQL | Exact replay, bounded review object, transactional event, no executable side effect | Promotion needs a later build/verification/materialization design | Chosen |

## Evidence

Architecture §7.6.5 and D-016 say tools are proposed, never self-registered, and that
promotion requires explicit human approval. The same text requires source, tests,
rationale, and motivating observations and forbids write/delete capability in v1.
The existing approval and skill-change stores demonstrate transactionally pairing a
domain row with its canonical execution event. F4c's live recovery work also showed
why durable acceptance and materialized runtime state must not share one status.

The implementation gate built 33 projects with 0 warnings and 0 errors, all sixteen
suites passed 713/713, and format verification exited 0. Migration 022 applied to the
live database with none pending; direct inspection observed an empty table, the
append-only trigger, all three redundant-column/size integrity checks, and only
SELECT/INSERT privileges for `dami_app`.

No evidence shows that dynamically loaded arbitrary managed code can be confined
inside the Host. The decision therefore treats source declarations and test results as
review evidence, not a security boundary. F5c remains responsible for proving the
promotion mechanism does not grant prohibited authority.

## Consequences

Proposal submission is idempotent and side-effect free beyond PostgreSQL. Reviewers
can inspect one exact version and tests cannot be omitted. The proposal event means
only "accepted into staging". It never means tests ran, approval was granted, source
was compiled, or a tool became invokable.

The source/test envelope is deliberately narrower than an arbitrary project. This
rules out self-selected packages and build scripts, keeps hashing deterministic, and
makes later verification policy enforceable. A tool needing a new dependency must be
implemented through the normal human-owned repository workflow instead.

F5c cannot promote by setting a database flag and calling `Assembly.Load`. It needs a
separate, evidence-backed activation design with exact approval provenance, fixed build
inputs, analyzer/test output, prohibited-authority checks, and a rollback path.

## Reversal path

The append-only proposal/event rows remain valid audit evidence if the artifact format
changes. Add a versioned format discriminator and a new reader/verification adapter;
do not rewrite old proposals. If C# source is abandoned for WASM or a declarative tool
DSL, future proposals use the new format while v1 artifacts remain inspectable and
non-executable.
