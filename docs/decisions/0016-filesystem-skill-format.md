# ADR 0016 — Bounded filesystem skill format

- **Decision:** Store each skill in one direct child directory containing a strict `skill.json` descriptor, a required `SKILL.md` body, and explicitly listed relative reference files; reject links and version the normalized descriptor plus exact content bytes.
- **Date:** 2026-08-24
- **Status:** accepted
- **Supersedes:** none

## Context

D-014 defines a skill as procedural knowledge loaded into context, D-015 requires it
to normalize into the unified capability registry, and F4a requires bounded filesystem
loading with stable versioning. The registry needs small retrieval metadata without
inlining procedural bodies, while later progressive disclosure needs a stable opaque
body reference. Local files are editable and recoverable, but path traversal, links,
unbounded reads, ambiguous descriptors, and formatting-sensitive versions would make
that boundary unsafe or noisy.

## Alternatives considered

| Option | Strengths | Weaknesses | Why not chosen |
|---|---|---|---|
| JSON descriptor beside Markdown body | Strict built-in parser; metadata stays small; body remains ordinary Markdown | Two required files | Chosen: it keeps retrieval data separate from progressively disclosed instructions without another parser dependency |
| YAML/front matter in one Markdown file | Familiar authoring format; one file | Requires another parser and couples trusted routing metadata to instruction text | The extra dependency and mixed trust boundary buy little for a local format |
| Recursive convention-only discovery | Minimal metadata | Ambiguous identity and references; difficult to bound and version deterministically | Stable IDs and explicit references are required for registry and audit behavior |
| Store skill bodies in PostgreSQL | Transactional mutation and history | Poor direct authoring ergonomics; adds persistence scope before F4c | Files plus durable mutation events preserve readable source while F4c supplies history |

## Evidence

The F4a tests demonstrate registry publication without body inlining, semantic version
stability across JSON formatting, strict UTF-8 validation, duplicate-ID all-or-none
publication, single-line retrieval metadata, and refusal of linked reference and root
directories. The shared registry test deterministically observed the old
half-published batch and now sees one atomic snapshot. The focused suites passed 7/7
skills tests and 35/35 capability tests. The solution gate built all 33 projects with
0 warnings and 0 errors, passed 625/625 tests across sixteen suites, and format/analyzer
verification exited 0.

## Consequences

Descriptor whitespace does not change a version; normalized metadata order, body
bytes, explicit reference paths, or reference bytes do. Discovery and reads have hard
count and byte ceilings. Absolute/escaping paths, linked parents, linked files, linked
skill directories, and linked roots fail closed. The registry carries only retrieval
metadata and `skill://<id>/SKILL.md`; F4b owns resolving that opaque reference and
loading body or bundled content only when selected.

Skill authors must maintain `skill.json`, and references must be declared. Filesystem
replacement races cannot be eliminated portably by path validation alone; F4c must use
same-filesystem atomic replacement for writes, and each read remains bounded even if a
trusted local process changes a file concurrently.

## Reversal path

Introduce a versioned descriptor reader or another `ISkillContentStore` implementation,
load both formats during migration, then rebuild the derived capability index. Stable
skill IDs and opaque body references keep registry consumers independent of the physical
format.
