# /// script
# requires-python = ">=3.11"
# ///
"""Phase 0 preservation: dump every Weaviate class on the Mac mini to portable JSONL.

Read-only. One JSONL plus one schema file per class, plus a manifest with counts and
sha256 checksums, so 'verified backups' means verifiable.
"""
from __future__ import annotations

import hashlib
import json
import sys
import time
import urllib.request
from pathlib import Path

WEAVIATE = "http://192.168.4.23:8081"
EXPORT_DIR = Path("/home/steve/Data/corpus-export/full")
PAGE = 100


def fetch(path: str) -> dict:
    with urllib.request.urlopen(f"{WEAVIATE}{path}", timeout=120) as response:
        return json.loads(response.read())


def export_class(class_name: str, out_path: Path) -> int:
    exported = 0
    after = None
    with out_path.open("w") as out:
        while True:
            path = f"/v1/objects?class={class_name}&limit={PAGE}&include=vector"
            if after:
                path += f"&after={after}"
            objects = fetch(path).get("objects", [])
            if not objects:
                break
            for obj in objects:
                record = {
                    "id": obj["id"],
                    "class": class_name,
                    "properties": obj.get("properties", {}),
                }
                if obj.get("vector"):
                    record["vector"] = obj["vector"]
                out.write(json.dumps(record, ensure_ascii=False) + "\n")
            exported += len(objects)
            after = objects[-1]["id"]
    return exported


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def main() -> int:
    EXPORT_DIR.mkdir(parents=True, exist_ok=True)
    stamp = time.strftime("%Y%m%dT%H%M%SZ", time.gmtime())
    schema = fetch("/v1/schema")
    (EXPORT_DIR / f"schema-{stamp}.json").write_text(json.dumps(schema, indent=2))

    manifest = {"exported_at": stamp, "weaviate": fetch("/v1/meta").get("version"), "classes": {}}
    for cls in schema.get("classes", []):
        name = cls["class"]
        out_path = EXPORT_DIR / f"{name}-{stamp}.jsonl"
        count = export_class(name, out_path)
        manifest["classes"][name] = {
            "count": count,
            "file": out_path.name,
            "sha256": sha256(out_path),
        }
        print(f"{name:<24} {count:>6}  {manifest['classes'][name]['sha256'][:16]}")

    (EXPORT_DIR / f"manifest-{stamp}.json").write_text(json.dumps(manifest, indent=2))
    print(f"manifest: {EXPORT_DIR}/manifest-{stamp}.json")
    return 0


if __name__ == "__main__":
    sys.exit(main())
