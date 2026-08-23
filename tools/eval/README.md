# Retrieval eval

**Purpose: satisfy D-010 — the embedding model is chosen by measurement on the real
corpus, not by leaderboard rank.** Today `bge-m3` is serving because it was a sensible
default, which is exactly what D-010 says must not decide it.

```bash
uv run tools/eval/retrieval_eval.py \
  --corpus  tools/eval/sample-corpus.jsonl \
  --queries tools/eval/sample-queries.jsonl \
  --label   bge-m3
```

`uv` resolves the dependency inline; there is no virtualenv to create or activate.

## Input format

`--corpus` — JSONL, one document per line:

```json
{"id": "d01", "text": "The reflection pass runs weekly and correlates …"}
```

`--queries` — JSONL, one query per line. `relevant` lists the ids that *should* come
back:

```json
{"query": "which background job connects different areas of life?", "relevant": ["d01"]}
```

D-010 calls for **50 queries built from the existing 7,000 memories with known-good
answers**. That corpus is Phase 0 work on the Mac. The `sample-*.jsonl` files here are
synthetic, 15 documents and 8 queries, and exist only to prove the harness runs — they
are not an eval set and no decision should rest on them.

## What it measures, and what it deliberately does not

**Exact search, no HNSW index.** The metric is the embedding model's quality, and an
index would confound it with index recall. Two consequences:

- Numbers here are the model's ceiling. A deployed HNSW index will be at or below them.
- **A model too large to index can still be evaluated.** Qwen3-Embedding-8B at 4096
  dimensions exceeds `halfvec`'s 4000-dimension index ceiling on this cluster, but the
  eval will score it. That separates "is it good" from "can we deploy it", which are
  different questions and were being conflated.

Metrics are `recall@k`, `MRR`, and `nDCG@k` with binary relevance, plus p50 wall-clock
per query. Each run reports **ANN only** and **ANN + rerank** so the reranker's
contribution is a number rather than an assumption.

## Switching models

Restart the TEI container with a different `--model-id`, then rerun with a new `--label`.
The label names the eval table, so runs do not overwrite each other. Dimension is probed
from the running service, not configured.

```bash
docker rm -f dami-embed
docker run -d --name dami-embed --gpus all --restart unless-stopped \
  -e LD_LIBRARY_PATH=/usr/local/cuda/lib64 \
  -p 127.0.0.1:8080:80 -v /home/steve/Data/tei-models:/data \
  ghcr.io/huggingface/text-embeddings-inference:89-1.9.0 \
  --model-id Qwen/Qwen3-Embedding-0.6B --dtype float16
```

The `LD_LIBRARY_PATH` override is mandatory — see the runbook, §4.2.

## Running comparison (draft query set — review pending)

On the real corpus (7,048 docs) with the 37-pair DRAFT query set. Numbers move when
Steve reviews the set; the table is the method working, not the final answer.

| model | dims | ANN recall@10 | ANN MRR | reranked MRR | reranked nDCG@10 |
|---|---|---|---|---|---|
| `BAAI/bge-m3` | 1024 | **0.8378** | **0.6115** | **0.6899** | **0.7122** |
| `Qwen/Qwen3-Embedding-0.6B` | 1024 | 0.7838 | 0.5674 | 0.6676 | 0.6895 |

**Reranking helps at scale** (+0.08–0.10 MRR for both models), reversing the
15-doc synthetic result exactly as predicted below — D-008's claim, now with evidence.
bge-m3 leads on every metric so far, corroborating ADR-0009's interim choice.

## A worked example, and why its result is not a finding

First run against the synthetic sample:

```
stage              recall@5          mrr       ndcg@5  p50_seconds
ANN only             0.9375       0.8750       0.8619       0.0272
ANN + rerank         0.9375       0.8167       0.8213       0.0739
rerank delta : recall@5 +0.0000  mrr -0.0583  ndcg@5 -0.0405
```

Reranking scored *worse* and cost 2.7× the latency. **Do not read that as evidence
against D-008.** With 15 documents and 15 candidates the ANN stage already returns the
whole corpus, so the reranker has no filtering job — it can only reorder, and every
mistake is a pure regression with no recall to win back. Reranking earns its place when
top-50 is drawn from thousands, which is precisely the case this sample cannot create.

What the run does show is that the harness detects a regression rather than assuming an
improvement. D-008 asserts reranking is "the largest single quality gain available";
this is the instrument that will confirm or refute it on real data.

## Notes

- Connects as `dami_ddl` — it creates tables. Credentials come from `~/.pgpass`.
- Eval tables are dropped after each run; pass `--keep` to retain one, `--reuse` to skip
  re-embedding and re-score an existing table.
- Requests are chunked at 32, TEI's `max_client_batch_size`. Reranking a 50-candidate
  list is therefore two calls whose scores are merged — cross-encoder scores are
  independent per pair, so chunking does not change the ordering.
