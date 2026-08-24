# ADR 0015 — D-010 evidence: measured retrieval, two models, one corpus

- **Decision (proposed):** Move the production embedder from `BAAI/bge-m3` to
  `BAAI/bge-large-en-v1.5`. It wins on every retrieval metric against Steve's real
  7,048-document corpus at identical dimensionality, so the migration is a re-embed
  with no schema change.
- **Date:** 2026-08-24
- **Status:** proposed — **the relevance labels are still drafts.** B6 asks Steve to
  review the 37 query/target pairs; these numbers are only as good as those labels,
  and the decision is his.
- **Supersedes:** the interim choice in ADR-0009 (bge-m3), if accepted.

## What was measured

D-010 requires the embedder be chosen by measurement on the real corpus rather than
leaderboard rank. Until today that measurement had never been run. Both models were
evaluated by the same harness over the same 7,048 documents and 37 queries, using
exact search rather than an HNSW index so index recall could not confound the result.

| model | stage | recall@10 | MRR | nDCG@10 | p50 |
|---|---|---|---|---|---|
| `bge-m3` (current) | ANN only | 0.8108 | 0.6081 | 0.6569 | 0.148s |
| `bge-m3` (current) | ANN + rerank | 0.7838 | 0.6923 | 0.7145 | 0.234s |
| **`bge-large-en-v1.5`** | ANN only | 0.8108 | 0.6795 | 0.7124 | 0.132s |
| **`bge-large-en-v1.5`** | **ANN + rerank** | **0.8108** | **0.7194** | **0.7415** | 0.235s |

## What the numbers say

**The English-specialised model is better on this corpus.** Same recall before
reranking, better recall after it (0.8108 vs 0.7838), and meaningfully better ranking
quality — MRR +0.027, nDCG +0.027 — at slightly lower ANN latency. Steve's corpus is
English; `bge-m3` is multilingual, and that generality appears to cost precision here.

**The rerank stage earns its place, and this is the first proof.** §9.3's
embed→ANN→rerank pipeline was an assumption. Measured: reranking lifts MRR by 0.084
and nDCG by 0.058 on `bge-m3`. It is not free — on `bge-m3` it *costs* recall@10
(−0.027), because reordering can push a relevant document out of the top ten while
putting the best one higher. On `bge-large-en-v1.5` that cost disappears entirely
(recall unchanged, ranking still improves), which is a second argument for the switch.

**Migration is cheap.** Both are 1024 dimensions, so `observation_embeddings` needs no
schema change — this is the per-row `embedding_model` versioning ADR-0009 built for.
At the measured 193 docs/s the whole corpus re-embeds in about a minute.

## What would change the answer

- **The labels.** 37 draft pairs, unreviewed. If Steve's review corrects a meaningful
  share of them, both columns move. That is B6 and it is the real gate.
- **Non-English content.** `bge-large-en-v1.5` is English-only. If the corpus ever
  holds meaningful non-English material, `bge-m3`'s generality becomes the point.
- **Larger candidates.** `Qwen3-Embedding-4B` was not evaluated: at 2560 dimensions it
  needs `halfvec`, and ~8 GB of VRAM does not fit beside a pinned LLM, two TEI
  services, and the speech sidecar (measured: 9.7 GB of 16.4 GB already resident).

## Consequences

If accepted: run a second TEI on the new model, re-embed under its name, verify the
belief and observation paths, then retire the old vectors. Both sets coexist during
the switch by design. If rejected, nothing changes and the measurement stands as the
D-010 record.

## A harness bug found and fixed

`retrieval_eval.py` printed the model from the *default* embedding URL rather than the
one under test, so every comparison run was labelled with the incumbent's name. A
benchmark that mislabels its subject is worse than no benchmark; fixed in the same
change as this evidence.
