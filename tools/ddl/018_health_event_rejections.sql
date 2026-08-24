-- The health domain made correctable (F-09/F-10's discipline, applied to K2).
--
-- Health facts are model-derived and imperfect: the first backfill filed another
-- person's diagnosis under Steve's name. A wrong fact must be removable, and the
-- removal must STICK — deleting the row alone would let the next collector pass
-- re-extract it from the same observation and put it straight back.
--
-- Keyed on (observation_id, description) to match the extraction's own uniqueness,
-- so a rejection blocks exactly the fact that was wrong and nothing else.

create table dami.health_event_rejections (
    observation_id uuid not null references dami.observations (observation_id),
    description    text not null,
    reason         text not null,
    rejected_at    timestamptz not null default now(),

    primary key (observation_id, description)
);

grant select, insert on dami.health_event_rejections to dami_app;
