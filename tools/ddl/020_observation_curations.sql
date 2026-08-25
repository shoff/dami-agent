-- The Hermes import was lazy: it carried transcript voice straight in. A third of the
-- corpus reads "As of 2026-03-02 the user reports they are noticeably less afraid of
-- dying" — meeting minutes about a stranger, with the date restated in prose when it is
-- already a column. Every retrieval, every prompt, every belief formed from it inherits
-- that, forever.
--
-- Curation is DERIVED, like embeddings and date repairs: observations stay append-only
-- and the original text is never touched. Reads coalesce through this table, so the
-- corpus reads naturally while the source of truth stays exactly what was recorded.
--
-- The rewrite says "Steve", not "the user". De-identifying at rest is the wrong place
-- for it — that is the disclosure gate's job at the egress boundary (ADR-0019), and
-- doing it twice costs clarity everywhere and buys nothing.

create table dami.observation_curations (
    observation_id uuid primary key references dami.observations (observation_id),
    curated_body   text not null,
    method         text not null,
    curated_at     timestamptz not null default now(),

    constraint observation_curations_method_known check (method in ('local-model', 'manual'))
);

grant select, insert on dami.observation_curations to dami_app;
