-- 044 — a scheduled job remembers what it said, and may stay silent when nothing changed.
--
-- docs/agent-landscape-2026-09.md C1/C2: the flaky, repetitive morning brief is the most
-- abandoned thing people build on a personal agent. Each run's output is kept, the next
-- run is shown its last few, and a job marked only_when_new that answers "NOTHING NEW" is
-- recorded as not delivered and sends nothing.

alter table dami.scheduled_jobs
    add column only_when_new boolean not null default false;

create table dami.scheduled_job_runs (
    run_id    uuid        primary key,
    job_id    uuid        not null references dami.scheduled_jobs (job_id) on delete cascade,
    ran_at    timestamptz not null,
    output    text        not null,
    delivered boolean     not null
);

create index scheduled_job_runs_by_job on dami.scheduled_job_runs (job_id, ran_at desc);

grant select, insert on dami.scheduled_job_runs to dami_app;
