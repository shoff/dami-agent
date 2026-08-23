# ADR 0009 — bge-m3 as the interim observation embedder

- **Decision:** Embed the observation corpus with `BAAI/bge-m3` (1024 dims) now, recorded per-row in the schema, rather than leaving all semantic retrieval blocked on the D-010 eval.
- **Date:** 2026-08-23
- **Status:** accepted as interim — **does not decide D-010**; the eval still chooses the production embedder
- **Supersedes:** none

## Context

D-010 requires the embedding model be chosen by a 50-query eval built from the 7,000-memory corpus. That corpus is a Phase 0 export from the Mac with no scheduled date. Meanwhile the `004_observation_embeddings.sql.template` gate has an expanding blast radius: semantic retrieval over the corpus, `dami recall`, retrieval-augmented reflection, and the capability registry's retrieval (D-015) all sit behind it.

The schema was designed for exactly this situation: `embedding_model` is versioned per row, and D-010's own text says "changing embedders means re-embedding everything… the migration path must exist before it is needed." An interim embedder exercises that path instead of hoping it works.

## Alternatives considered

| Option | Why not |
|---|---|
| Keep waiting for the eval | Blocks four features on a Mac-side export with no ETA; the cost of waiting compounds while the cost of re-embedding stays flat (~100 docs/s measured on this host — the current corpus re-embeds in seconds, and even 7,000 memories in about a minute) |
| Qwen3-Embedding-4B interim | 2560 dims needs `halfvec`; ~8 GB VRAM resident against a 16 GB card already carrying two TEI services and an LLM sidecar; and it is no more validated than bge-m3 |
| Decide D-010 outright without the eval | Violates the register; the eval exists because leaderboard rank diverges from in-domain performance |

## Evidence

bge-m3 is already resident and serving (`dami health`), costs no additional VRAM, indexes with plain `vector` (1024 < 2000), and is D-010's own named "conservative production default". Re-embedding cost measured, not guessed: the eval harness indexed at ~107 docs/s through the same service.

## Consequences

Semantic retrieval unblocks now. When the eval later picks a different model, the migration is: add rows under the new model name, reindex, delete the old — per-row versioning makes both sets coexist during the switch. If the eval picks bge-m3, nothing happens at all.

## Reversal path

`delete from dami.observation_embeddings where embedding_model = 'BAAI/bge-m3'` and re-run the embedder service against the winner. The observations themselves are untouched — vectors are derived data.
