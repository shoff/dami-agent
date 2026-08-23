# /// script
# requires-python = ">=3.11"
# dependencies = ["psycopg[binary]>=3.2"]
# ///
"""Renders the draft eval set as a review sheet a human can judge in minutes.

For each draft pair: the query, the intended target, and what bge-m3 actually returns
top-3 — so a bad pair is visible at a glance (target missing, query too lexical, or the
retrieval finding something more relevant than the intended answer, which means the
'relevant' list needs additions, not the query deletion).

Marking: edit tools/eval/corpus-queries.draft.jsonl directly - delete a line to drop a
pair, extend "relevant" to accept additional ids. Re-run the eval after.
"""
from __future__ import annotations

import json
import sys
import urllib.request
from pathlib import Path

import psycopg

DSN = "host=127.0.0.1 dbname=dami-data user=dami_app"
DRAFT = Path("tools/eval/corpus-queries.draft.jsonl")
OUT = Path("tools/eval/REVIEW.md")


def embed(text: str) -> list[float]:
    request = urllib.request.Request(
        "http://127.0.0.1:8080/embed",
        data=json.dumps({"inputs": [text]}).encode(),
        headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=60) as response:
        return json.loads(response.read())[0]


def main() -> int:
    pairs = [json.loads(line) for line in DRAFT.read_text().splitlines() if line.strip()]

    with psycopg.connect(DSN) as connection, connection.cursor() as cursor, OUT.open("w") as out:
        out.write("# D-010 eval set review\n\n")
        out.write(f"{len(pairs)} draft pairs. For each: does the TARGET deserve to be found by the QUERY?\n\n")
        out.write("- pair is bad → delete its line from `corpus-queries.draft.jsonl`\n")
        out.write("- a TOP-3 hit is *also* a right answer → add its id to that line's `relevant`\n")
        out.write("- happy → do nothing\n\n---\n\n")

        for number, pair in enumerate(pairs, 1):
            vector = "[" + ",".join(str(v) for v in embed(pair["query"])) + "]"
            cursor.execute(
                "select o.observation_id::text, left(o.body, 220) "
                "from dami.observation_embeddings e "
                "join dami.observations o on o.observation_id = e.observation_id "
                "order by e.embedding <=> %s::vector limit 3", (vector,))
            top = cursor.fetchall()

            target_ids = set(pair["relevant"])
            hit = any(row[0] in target_ids for row in top)
            marker = "HIT " if hit else "MISS"

            out.write(f"## {number}. [{pair.get('category','?')}] {marker}\n\n")
            out.write(f"**Query:** {pair['query']}\n\n")
            out.write(f"**Intended target** (`{pair['relevant'][0][:8]}`): {pair.get('draft_source_text','')}\n\n")
            out.write("**bge-m3 top-3:**\n\n")
            for observation_id, body in top:
                tag = " ← target" if observation_id in target_ids else ""
                out.write(f"- `{observation_id[:8]}`{tag} {body}\n")
            out.write("\n---\n\n")

    print(f"review sheet -> {OUT}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
