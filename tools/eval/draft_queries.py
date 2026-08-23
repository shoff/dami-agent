# /// script
# requires-python = ">=3.11"
# dependencies = ["psycopg[binary]>=3.2"]
# ///
"""Drafts the D-010 eval set from the real corpus.

Samples distinctive memories across categories and asks the LOCAL sidecar to write a
retrieval query for each - phrased as Steve would ask, deliberately avoiding the
memory's own words so the eval measures semantics rather than string overlap.

The output is a DRAFT. D-010 requires known-good answers, and "known-good" is Steve's
judgment: he reviews the file, deletes bad pairs, and adds relevant ids the sampler
missed. Nothing leaves the host.
"""
from __future__ import annotations

import json
import sys
import urllib.request
from pathlib import Path

import psycopg

DSN = "host=127.0.0.1 dbname=dami-data user=dami_app"
OLLAMA = "http://127.0.0.1:11434/api/generate"
OUT = Path("tools/eval/corpus-queries.draft.jsonl")

PER_CATEGORY = {
    "technical": 8, "personal": 8, "decision": 6, "preference": 6,
    "project": 5, "emotional": 5, "fact": 4,
}

PROMPT = """Write ONE short search query a person would type to find this note about themselves.
Use different words than the note where possible - synonyms and paraphrase, not copies.
No quotes, no explanation, just the query.

Note: {body}

Query:"""


def ask(prompt: str) -> str:
    body = json.dumps({
        "model": "qwen3:8b", "prompt": prompt, "think": False,
        "stream": False, "options": {"num_predict": 40, "temperature": 0.4},
    }).encode()
    request = urllib.request.Request(OLLAMA, data=body, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=180) as response:
        return json.loads(response.read()).get("response", "").strip().strip('"').splitlines()[0]


def sample(cursor, category: str, count: int) -> list[tuple[str, str]]:
    cursor.execute(
        """
        select observation_id::text, body from dami.observations
        where source = 'hermes-memory'
          and metadata->>'category' = %s
          and length(body) between 80 and 400
          and (metadata->>'importance')::float >= 0.7
        order by md5(observation_id::text)
        limit %s
        """,
        (category, count),
    )
    return cursor.fetchall()


def main() -> int:
    drafted = 0
    with psycopg.connect(DSN) as connection, connection.cursor() as cursor, OUT.open("w") as out:
        for category, count in PER_CATEGORY.items():
            for observation_id, body in sample(cursor, category, count):
                query = ask(PROMPT.format(body=body[:400]))
                if len(query) < 8 or len(query) > 160:
                    continue
                out.write(json.dumps({
                    "query": query,
                    "relevant": [observation_id],
                    "category": category,
                    "draft_source_text": body[:160],
                }, ensure_ascii=False) + "\n")
                out.flush()
                drafted += 1
                print(f"[{category}] {query}")
    print(f"\n{drafted} draft pairs -> {OUT}  (REVIEW BEFORE TRUSTING: delete bad pairs, add missed ids)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
