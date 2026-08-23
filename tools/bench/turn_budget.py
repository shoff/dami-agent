# /// script
# requires-python = ">=3.11"
# dependencies = ["psycopg[binary]>=3.2"]
# ///
"""Measures the local stages of an interactive turn on this hardware.

N-01 demands sub-2s streamed responses "matching MAI". That number came from other
hardware; this decomposes the budget into measured stage costs here, so the runtime is
designed against measurements rather than an inherited target. Frontier-model time is
the one stage this cannot measure - everything local is.
"""
from __future__ import annotations

import json
import statistics
import sys
import time
import urllib.request

import psycopg

DSN = "host=127.0.0.1 dbname=dami-data user=dami_app"
RUNS = 20
QUERY = "what is steve worried about at work"


def post(url: str, payload: dict, timeout: int = 300) -> dict | list:
    body = json.dumps(payload).encode()
    request = urllib.request.Request(url, data=body, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=timeout) as response:
        return json.loads(response.read())


def timed(callable_, runs: int = RUNS) -> tuple[float, float]:
    samples = []
    for _ in range(runs):
        started = time.perf_counter()
        callable_()
        samples.append((time.perf_counter() - started) * 1000)
    samples.sort()
    return statistics.median(samples), samples[int(len(samples) * 0.95) - 1]


def main() -> int:
    rows: list[tuple[str, float, float]] = []

    # embed one query
    vector = post("http://127.0.0.1:8080/embed", {"inputs": [QUERY]})[0]
    rows.append(("embed query (TEI, bge-m3)", *timed(
        lambda: post("http://127.0.0.1:8080/embed", {"inputs": [QUERY]}))))

    # ANN top-24 over the 7k corpus
    literal = "[" + ",".join(str(v) for v in vector) + "]"
    connection = psycopg.connect(DSN)
    cursor = connection.cursor()

    def ann():
        cursor.execute(
            "select o.body from dami.observation_embeddings e "
            "join dami.observations o on o.observation_id=e.observation_id "
            "order by e.embedding <=> %s::vector limit 24", (literal,))
        cursor.fetchall()

    ann()
    rows.append(("ANN top-24 (pgvector, 7k rows)", *timed(ann)))

    cursor.execute(
        "select o.body from dami.observation_embeddings e "
        "join dami.observations o on o.observation_id=e.observation_id "
        "order by e.embedding <=> %s::vector limit 24", (literal,))
    bodies = [row[0][:400] for row in cursor.fetchall()]

    rows.append(("rerank 24 (TEI cross-encoder)", *timed(
        lambda: post("http://127.0.0.1:8081/rerank", {"query": QUERY, "texts": bodies}))))

    # local LLM: TTFT and streamed rate, thinking OFF and ON
    def ttft(think: bool) -> tuple[float, float, int]:
        body = json.dumps({
            "model": "qwen3:8b", "prompt": f"Answer briefly: {QUERY}",
            "think": think, "stream": True, "options": {"num_predict": 80},
        }).encode()
        request = urllib.request.Request(
            "http://127.0.0.1:11434/api/generate", data=body,
            headers={"Content-Type": "application/json"})
        started = time.perf_counter()
        first = None
        tokens = 0
        with urllib.request.urlopen(request, timeout=600) as response:
            for line in response:
                event = json.loads(line)
                if first is None and (event.get("response") or event.get("thinking")):
                    first = time.perf_counter() - started
                if event.get("response"):
                    tokens += 1
                if event.get("done"):
                    break
        return (first or 0) * 1000, (time.perf_counter() - started) * 1000, tokens

    post("http://127.0.0.1:11434/api/generate",
         {"model": "qwen3:8b", "prompt": "warm", "think": False, "stream": False,
          "options": {"num_predict": 2}})

    for think in (False, True):
        firsts, totals = [], []
        for _ in range(5):
            first, total, _ = ttft(think)
            firsts.append(first)
            totals.append(total)
        label = f"local LLM think={'on' if think else 'off'} (80 tok)"
        rows.append((label + " TTFT", statistics.median(firsts), max(firsts)))
        rows.append((label + " total", statistics.median(totals), max(totals)))

    print(f"{'stage':<38}{'p50 ms':>10}{'p95/max ms':>12}")
    local_total = 0.0
    for name, p50, p95 in rows:
        print(f"{name:<38}{p50:>10.1f}{p95:>12.1f}")
        if "LLM" not in name:
            local_total += p50
    print(f"\nretrieval subtotal (embed+ANN+rerank) p50: {local_total:.0f} ms")
    connection.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
