#!/usr/bin/env python3
"""B10 one-off: recover dates from the bodies of epoch-zero observations.

Append-only discipline: writes only into dami.observation_date_repairs, never
touches dami.observations. Idempotent — already-examined rows are skipped.
Run as: sudo -u postgres python3 tools/repair_epoch_dates.py
"""
import re
import subprocess

ISO = re.compile(r"\b(20[12]\d)-(\d{2})-(\d{2})\b")
PROSE = re.compile(
    r"\b(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\.?\s+(\d{1,2}),?\s+(20[12]\d)\b",
    re.IGNORECASE)
MONTHS = {m: i + 1 for i, m in enumerate(
    ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"])}


def psql(sql):
    return subprocess.run(
        ["psql", "-d", "dami-data", "-tA", "-F", "\x1f", "-c", sql],
        check=True, capture_output=True, text=True).stdout


def valid(y, m, d):
    return 2020 <= y <= 2026 and 1 <= m <= 12 and 1 <= d <= 31


def recover(body):
    match = ISO.search(body)
    if match:
        y, m, d = int(match[1]), int(match[2]), int(match[3])
        if valid(y, m, d):
            return f"{y:04d}-{m:02d}-{d:02d}", "body-iso"
    match = PROSE.search(body)
    if match:
        y, m, d = int(match[3]), MONTHS[match[1].lower()[:3]], int(match[2])
        if valid(y, m, d):
            return f"{y:04d}-{m:02d}-{d:02d}", "body-prose"
    return None, "unrecoverable"


def main():
    rows = psql("""
        select o.observation_id, o.body from dami.observations o
        left join dami.observation_date_repairs r using (observation_id)
        where o.occurred_at < '1971-01-01' and r.observation_id is null
    """).strip("\n")
    if not rows:
        print("nothing to repair")
        return
    repaired = flagged = 0
    for line in rows.split("\n"):
        observation_id, body = line.split("\x1f", 1)
        date, method = recover(body)
        value = f"'{date} 12:00:00+00'" if date else "null"
        psql(f"""
            insert into dami.observation_date_repairs
                (observation_id, repaired_occurred_at, method)
            values ('{observation_id}', {value}, '{method}')
            on conflict (observation_id) do nothing
        """)
        if date:
            repaired += 1
        else:
            flagged += 1
    print(f"repaired: {repaired}, flagged unrecoverable: {flagged}")


if __name__ == "__main__":
    main()
