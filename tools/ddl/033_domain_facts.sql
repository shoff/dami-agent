-- 033 — one table for every domain after health (K4).
--
-- Health got its own schema because it was first and maximally sensitive. The domains
-- after it — network, civic, estate, workshop — share one shape: a dated, categorised,
-- one-clause fact with its source, deduplicated per day, correctable by rejection that
-- sticks. One table means a new domain is a collector and a name, not a migration.

create table dami.domain_facts (
    fact_id     uuid        primary key,
    domain      text        not null,
    as_of       date        not null,
    category    text        not null,
    description text        not null,
    source      text        not null,
    recorded_at timestamptz not null,
    constraint domain_facts_domain_present check (length(btrim(domain)) > 0),
    constraint domain_facts_description_present check (length(btrim(description)) > 0),
    -- The same statement on the same day is the same fact; a state that persists across
    -- days is a row per day, which is the timeline.
    constraint domain_facts_daily_unique unique (domain, as_of, description)
);

create index domain_facts_by_domain_date on dami.domain_facts (domain, as_of desc);

create table dami.domain_fact_rejections (
    fact_id     uuid        primary key references dami.domain_facts (fact_id),
    reason      text        not null,
    rejected_at timestamptz not null default now()
);

grant select, insert on dami.domain_facts to dami_app;
grant select, insert on dami.domain_fact_rejections to dami_app;
