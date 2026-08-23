# /// script
# requires-python = ">=3.11"
# dependencies = ["psycopg[binary]>=3.2"]
# ///
"""Phase 0 + Phase 2 in one pass: export the Hermes memory corpus from Weaviate on the
Mac mini to a portable, schema-explicit JSONL, then import it into dami.observations.

Read-only against the Mac (hard rule: the Mac is the rollback). Idempotent into
Postgres: the Weaviate object id becomes the observation id, so a re-run re-imports
nothing and a partial failure resumes safely.

  uv run tools/migration/import_corpus.py --export-only     # JSONL only
  uv run tools/migration/import_corpus.py                   # export + import
"""
from __future__ import annotations

import argparse
import json
import sys
import time
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

import psycopg

WEAVIATE = "http://192.168.4.23:8081"
CLASS_NAME = "ClawdbotMemoryV2"
EXPORT_DIR = Path("/home/steve/Data/corpus-export")
DSN = "host=127.0.0.1 dbname=dami-data user=dami_app"
PAGE = 100


def fetch(path: str) -> dict:
    with urllib.request.urlopen(f"{WEAVIATE}{path}", timeout=60) as response:
        return json.loads(response.read())


def export_class(class_name: str, out_path: Path) -> int:
    """Cursor-paginated dump of every object, verbatim properties plus id."""
    exported = 0
    after = None
    with out_path.open("w") as out:
        while True:
            path = f"/v1/objects?class={class_name}&limit={PAGE}"
            if after:
                path += f"&after={after}"
            objects = fetch(path).get("objects", [])
            if not objects:
                break
            for obj in objects:
                record = {"id": obj["id"], "class": class_name, "properties": obj["properties"]}
                out.write(json.dumps(record, ensure_ascii=False) + "\n")
            exported += len(objects)
            after = objects[-1]["id"]
            print(f"\r  {class_name}: {exported}", end="", flush=True)
    print()
    return exported


def to_timestamp(millis: object) -> datetime:
    return datetime.fromtimestamp(int(millis) / 1000.0, tz=timezone.utc)


def import_observations(jsonl: Path) -> tuple[int, int]:
    inserted = 0
    skipped = 0
    with psycopg.connect(DSN) as connection, connection.cursor() as cursor:
        with jsonl.open() as source:
            for line in source:
                record = json.loads(line)
                properties = record["properties"]
                text = (properties.get("text") or "").strip()
                if not text:
                    skipped += 1
                    continue

                metadata = {
                    "category": properties.get("category") or "other",
                    "importance": str(properties.get("importance", "")),
                    "sensitive": str(bool(properties.get("sensitive"))).lower(),
                    "hermes_source": properties.get("source") or "",
                }
                if properties.get("sessionKey"):
                    metadata["session_key"] = properties["sessionKey"]

                cursor.execute(
                    """
                    insert into dami.observations (observation_id, occurred_at, source, body, metadata)
                    values (%s, %s, 'hermes-memory', %s, %s::jsonb)
                    on conflict (observation_id) do nothing
                    """,
                    (record["id"], to_timestamp(properties["createdAt"]), text, json.dumps(metadata)),
                )
                inserted += cursor.rowcount
        connection.commit()
    return inserted, skipped


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--export-only", action="store_true")
    arguments = parser.parse_args()

    meta = fetch("/v1/meta")
    print(f"weaviate {meta.get('version')} at {WEAVIATE}, class {CLASS_NAME}")

    EXPORT_DIR.mkdir(parents=True, exist_ok=True)
    stamp = time.strftime("%Y%m%dT%H%M%SZ", time.gmtime())
    jsonl = EXPORT_DIR / f"{CLASS_NAME}-{stamp}.jsonl"

    schema = fetch(f"/v1/schema/{CLASS_NAME}")
    (EXPORT_DIR / f"{CLASS_NAME}-{stamp}.schema.json").write_text(json.dumps(schema, indent=2))

    exported = export_class(CLASS_NAME, jsonl)
    print(f"exported {exported} to {jsonl}")

    if arguments.export_only:
        return 0

    inserted, skipped = import_observations(jsonl)
    print(f"imported {inserted} new observation(s); {skipped} empty-text skipped; re-runs are no-ops")
    return 0


if __name__ == "__main__":
    sys.exit(main())
