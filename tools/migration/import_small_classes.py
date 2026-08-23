# /// script
# requires-python = ">=3.11"
# dependencies = ["psycopg[binary]>=3.2"]
# ///
"""Imports the small high-value Weaviate classes into dami.observations.

RelationshipDynamics: observed interaction patterns with lessons - exactly what the
reflection pass should see. DevLog and ConversationThreads: sparse but real history.
Idempotent like the main import: Weaviate id = observation id.
"""
from __future__ import annotations

import glob
import json
import sys
from datetime import datetime, timezone
from pathlib import Path

import psycopg

DSN = "host=127.0.0.1 dbname=dami-data user=dami_app"
EXPORT = Path("/home/steve/Data/corpus-export/full")


def newest(pattern: str) -> Path:
    return Path(sorted(glob.glob(str(EXPORT / pattern)))[-1])


def parse_time(value: object) -> datetime:
    if isinstance(value, (int, float)) and value > 1e11:
        return datetime.fromtimestamp(value / 1000.0, tz=timezone.utc)
    if isinstance(value, str):
        try:
            return datetime.fromisoformat(value.replace("Z", "+00:00"))
        except ValueError:
            pass
    return datetime.fromtimestamp(0, tz=timezone.utc)


def body_for(cls: str, properties: dict) -> str | None:
    if cls == "RelationshipDynamics":
        pattern = properties.get("pattern_observed") or ""
        lesson = properties.get("lesson_learned") or ""
        text = f"{pattern} Lesson: {lesson}".strip()
        return text if text != "Lesson:" else None
    if cls == "DevLog":
        title = properties.get("title") or ""
        content = properties.get("content") or properties.get("body") or ""
        return f"{title}: {content}".strip(": ") or None
    if cls == "ConversationThreads":
        return (properties.get("content") or "").strip() or None
    return None


def main() -> int:
    plans = {
        "RelationshipDynamics": ("hermes-relationship", "timestamp"),
        "DevLog": ("hermes-devlog", "createdAt"),
        "ConversationThreads": ("hermes-thread", "timestamp"),
    }

    with psycopg.connect(DSN) as connection, connection.cursor() as cursor:
        for cls, (source, time_field) in plans.items():
            inserted = 0
            with newest(f"{cls}-*.jsonl").open() as lines:
                for line in lines:
                    record = json.loads(line)
                    properties = record["properties"]
                    body = body_for(cls, properties)
                    if not body:
                        continue
                    cursor.execute(
                        """
                        insert into dami.observations (observation_id, occurred_at, source, body, metadata)
                        values (%s, %s, %s, %s, %s::jsonb)
                        on conflict (observation_id) do nothing
                        """,
                        (
                            record["id"],
                            parse_time(properties.get(time_field)),
                            source,
                            body[:8000],
                            json.dumps({"class": cls}),
                        ),
                    )
                    inserted += cursor.rowcount
            print(f"{cls:<22} {inserted} imported as {source}")
        connection.commit()
    return 0


if __name__ == "__main__":
    sys.exit(main())
