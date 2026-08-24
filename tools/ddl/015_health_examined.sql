-- K2: the health collector's high-water marker. An observation that mentions nothing
-- medical still gets examined once; without this row it would be re-read every pass.
-- Separate from health_events because "examined and empty" is not the same as "has a
-- health fact" — the same reason the embedder tracks coverage separately from vectors.

create table dami.health_examined (
    observation_id uuid primary key references dami.observations (observation_id),
    examined_at    timestamptz not null default now()
);

grant select, insert on dami.health_examined to dami_app;
