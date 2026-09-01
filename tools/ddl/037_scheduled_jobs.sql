create table dami.scheduled_jobs (
    job_id uuid primary key,
    name text not null,
    description text not null,
    kind text not null check (kind in ('Prompt', 'Command')),
    payload text not null,
    arguments jsonb not null default '[]'::jsonb,
    cron_expression text not null,
    time_zone_id text not null,
    status text not null check (status in ('Draft', 'Active', 'Paused')),
    created_at timestamptz not null,
    confirmed_at timestamptz,
    next_run_at timestamptz,
    last_run_at timestamptz,
    last_run_status text
);

create index scheduled_jobs_due_idx
    on dami.scheduled_jobs (next_run_at)
    where status = 'Active';

grant select, insert, update on dami.scheduled_jobs to dami_app;
