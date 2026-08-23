-- One row per proactive pass. The scheduler reads the latest row per service to decide
-- what is due, so cadence survives restarts - an in-memory last-run would re-run
-- everything on every boot.
--
-- Insert-only by privilege: a run happened or it did not, and the record does not get
-- revised.

create table dami.proactive_runs (
    run_id       uuid        primary key,
    service_name text        not null,
    trace_id     uuid        not null,
    ran_at       timestamptz not null,
    status       text        not null,

    constraint proactive_runs_status_known check (
        status in ('Completed', 'Failed', 'Cancelled')
    )
);

comment on table dami.proactive_runs is
    'The scheduler''s durable memory of when each service last ran. Failures count as runs: a service that fails is retried at its next cadence, not hammered in a loop.';

create index proactive_runs_latest on dami.proactive_runs (service_name, ran_at desc);

grant insert, select on dami.proactive_runs to dami_app;
