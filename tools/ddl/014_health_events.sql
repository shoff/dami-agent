-- K2: the first domain schema (D-007's canonical example — "correlate embeddings
-- against health rows against commit timestamps in one query"). Health is the most
-- sensitive domain in the system: these rows are LocalOnly by construction and have
-- no egress path anywhere in the code. Rows are DERIVED from observations (like
-- embeddings and conclusions), each carrying provenance back to the observation the
-- collector read, so a wrong extraction is always traceable to its source.

create table dami.health_events (
    health_event_id uuid primary key,
    observation_id  uuid not null references dami.observations (observation_id),
    event_date      date not null,
    category        text not null,
    description     text not null,
    extracted_at    timestamptz not null default now(),
    embedding_model text null,

    constraint health_events_category_known check (
        category in ('diagnosis', 'appointment', 'medication', 'vital', 'procedure', 'symptom')
    ),
    -- One row per (observation, description): a re-run of the collector over the same
    -- observation must not duplicate what it already extracted.
    constraint health_events_unique unique (observation_id, description)
);

create index health_events_by_date on dami.health_events (event_date);
create index health_events_by_category on dami.health_events (category);

grant select, insert on dami.health_events to dami_app;
