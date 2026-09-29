-- 047 — a scheduled job may watch a page, and wakes a model only when it changed (C3).
--
-- docs/agent-landscape-2026-09.md C3: fuzzy-criteria watchers (listings, event pages,
-- school sites) were among the most-kept uses in the 2026-09-29 survey, and the setups that
-- lasted put a deterministic check in front of the model. The runner fetches watch_url,
-- hashes its text, and compares with the job's last fingerprint; unchanged means no model.

alter table dami.scheduled_jobs
    add column watch_url text;

alter table dami.scheduled_job_runs
    add column fingerprint text;
