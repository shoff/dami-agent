-- Durable source text and provenance for bounded deep-research traversals.
-- Large research payloads live here; execution events only reference them.
create table dami.research_runs (
    run_id uuid primary key,
    revision integer not null check (revision > 0),
    trace_id uuid not null,
    started_at timestamptz not null,
    summary jsonb not null,
    snapshot jsonb not null
);

create index research_runs_started on dami.research_runs (started_at desc, run_id);
create index research_runs_trace on dami.research_runs (trace_id, started_at desc);

grant select, insert, update on dami.research_runs to dami_app;
