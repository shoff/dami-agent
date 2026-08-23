# /// script
# requires-python = ">=3.11"
# dependencies = ["psycopg[binary]>=3.2"]
# ///
"""Retrieval eval for D-010: choose the embedding model on evidence, not leaderboard rank.

Measures the embedding model, deliberately using exact search rather than an HNSW index
so index recall does not confound the result. A consequence worth knowing: a model whose
dimension exceeds pgvector's index ceiling can still be evaluated here, even though it
could not be deployed without Matryoshka truncation.

  uv run tools/eval/retrieval_eval.py --corpus c.jsonl --queries q.jsonl --label bge-m3
"""
from __future__ import annotations

import argparse
import json
import math
import re
import sys
import time
import urllib.request
from pathlib import Path

import psycopg

EMBED_URL = "http://127.0.0.1:8080"
RERANK_URL = "http://127.0.0.1:8081"
DSN = "host=127.0.0.1 dbname=dami-data user=dami_ddl"
BATCH = 32  # TEI max_client_batch_size


def post(url: str, payload: dict) -> object:
    body = json.dumps(payload).encode()
    request = urllib.request.Request(url, data=body, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=300) as response:
        return json.loads(response.read())


def get(url: str) -> dict:
    with urllib.request.urlopen(url, timeout=30) as response:
        return json.loads(response.read())


def embed(texts: list[str]) -> list[list[float]]:
    vectors: list[list[float]] = []
    for start in range(0, len(texts), BATCH):
        vectors.extend(post(f"{EMBED_URL}/embed", {"inputs": texts[start:start + BATCH]}))
    return vectors


def rerank(query: str, texts: list[str]) -> list[int]:
    """Indices of texts, best first. Chunked because TEI caps a client batch at 32."""
    scored: list[tuple[float, int]] = []
    for start in range(0, len(texts), BATCH):
        chunk = texts[start:start + BATCH]
        for item in post(f"{RERANK_URL}/rerank", {"query": query, "texts": chunk, "raw_scores": True}):
            scored.append((item["score"], start + item["index"]))
    scored.sort(key=lambda pair: pair[0], reverse=True)
    return [index for _, index in scored]


def read_jsonl(path: Path) -> list[dict]:
    return [json.loads(line) for line in path.read_text().splitlines() if line.strip()]


def slug(label: str) -> str:
    cleaned = re.sub(r"[^a-z0-9]+", "_", label.lower()).strip("_")
    if not cleaned:
        raise SystemExit("--label must contain at least one alphanumeric character")
    return cleaned


def load_corpus(connection, table: str, docs: list[dict], dimension: int) -> float:
    started = time.monotonic()
    with connection.cursor() as cursor:
        cursor.execute(f'drop table if exists dami."{table}"')
        cursor.execute(
            f'create table dami."{table}" '
            f"(doc_id text primary key, body text not null, embedding vector({dimension}) not null)"
        )
        for start in range(0, len(docs), BATCH):
            chunk = docs[start:start + BATCH]
            vectors = embed([doc["text"] for doc in chunk])
            cursor.executemany(
                f'insert into dami."{table}" values (%s, %s, %s::vector)',
                [(doc["id"], doc["text"], json.dumps(vector)) for doc, vector in zip(chunk, vectors)],
            )
    connection.commit()
    return time.monotonic() - started


def search(connection, table: str, vector: list[float], limit: int) -> list[tuple[str, str]]:
    with connection.cursor() as cursor:
        cursor.execute(
            f'select doc_id, body from dami."{table}" order by embedding <=> %s::vector limit %s',
            (json.dumps(vector), limit),
        )
        return cursor.fetchall()


def ndcg(ranked: list[str], relevant: set[str], k: int) -> float:
    gain = sum(1.0 / math.log2(position + 2) for position, doc in enumerate(ranked[:k]) if doc in relevant)
    ideal = sum(1.0 / math.log2(position + 2) for position in range(min(len(relevant), k)))
    return gain / ideal if ideal else 0.0


def score(ranked: list[str], relevant: set[str], k: int) -> dict[str, float]:
    hits = [index for index, doc in enumerate(ranked[:k]) if doc in relevant]
    return {
        f"recall@{k}": len(set(ranked[:k]) & relevant) / len(relevant),
        "mrr": 1.0 / (hits[0] + 1) if hits else 0.0,
        f"ndcg@{k}": ndcg(ranked, relevant, k),
    }


def evaluate(connection, table: str, queries: list[dict], candidates: int, k: int, use_rerank: bool) -> dict:
    totals: dict[str, float] = {}
    latencies: list[float] = []

    for entry in queries:
        started = time.monotonic()
        vector = embed([entry["query"]])[0]
        rows = search(connection, table, vector, candidates)
        ranked = [doc_id for doc_id, _ in rows]

        if use_rerank and rows:
            order = rerank(entry["query"], [body for _, body in rows])
            ranked = [rows[index][0] for index in order]

        latencies.append(time.monotonic() - started)
        for metric, value in score(ranked, set(entry["relevant"]), k).items():
            totals[metric] = totals.get(metric, 0.0) + value

    result = {metric: value / len(queries) for metric, value in totals.items()}
    result["p50_seconds"] = sorted(latencies)[len(latencies) // 2]
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--corpus", type=Path, required=True, help="JSONL of {id, text}")
    parser.add_argument("--queries", type=Path, required=True, help="JSONL of {query, relevant:[id]}")
    parser.add_argument("--label", required=True, help="Model identifier; names the eval table")
    parser.add_argument("--candidates", type=int, default=50, help="ANN depth before reranking")
    parser.add_argument("--k", type=int, default=10, help="Cutoff for the metrics")
    parser.add_argument("--reuse", action="store_true", help="Skip embedding; reuse the existing table")
    parser.add_argument("--keep", action="store_true", help="Leave the eval table in place")
    arguments = parser.parse_args()

    docs = read_jsonl(arguments.corpus)
    queries = read_jsonl(arguments.queries)
    served = get(f"{EMBED_URL}/info")
    dimension = len(embed(["dimension probe"])[0])
    table = f"eval_{slug(arguments.label)}"

    print(f"model served : {served['model_id']}  ({served['model_dtype']}, {dimension} dims)")
    print(f"label        : {arguments.label}   table dami.{table}")
    print(f"corpus       : {len(docs)} docs    queries: {len(queries)}")

    with psycopg.connect(DSN) as connection:
        if not arguments.reuse:
            seconds = load_corpus(connection, table, docs, dimension)
            rate = len(docs) / seconds if seconds else 0.0
            print(f"indexed      : {seconds:.1f}s  ({rate:.0f} docs/s)\n")

        rows = []
        for use_rerank in (False, True):
            metrics = evaluate(connection, table, queries, arguments.candidates, arguments.k, use_rerank)
            rows.append(("ANN + rerank" if use_rerank else "ANN only", metrics))

        headers = [key for key in rows[0][1]]
        print(f"{'stage':<14}" + "".join(f"{head:>13}" for head in headers))
        for name, metrics in rows:
            print(f"{name:<14}" + "".join(f"{metrics[head]:>13.4f}" for head in headers))

        delta = {head: rows[1][1][head] - rows[0][1][head] for head in headers if head != "p50_seconds"}
        print("\nrerank delta : " + "  ".join(f"{head} {value:+.4f}" for head, value in delta.items()))

        if not arguments.keep:
            with connection.cursor() as cursor:
                cursor.execute(f'drop table if exists dami."{table}"')
            connection.commit()
            print(f"dropped      : dami.{table}  (--keep to retain)")

    return 0


if __name__ == "__main__":
    sys.exit(main())
