-- The two memory layers (D-009), deliberately separate.
--
-- Observations record what happened and are never wrong, so they are append-only like
-- the event store. Conclusions are inferences, get retracted, and are therefore
-- relational and supersedable. Mixing them means a retracted conclusion stays
-- semantically retrievable forever, because nearest-neighbour search does not respect
-- tombstones unless it is made to.

create table dami.observations (
    observation_id uuid        primary key,
    occurred_at    timestamptz not null,
    recorded_at    timestamptz not null default now(),
    source         text        not null,
    body           text        not null,
    metadata       jsonb           null
);

comment on table dami.observations is
    'Append-only corpus of what happened. Never edited: the record is the record.';

-- NOTE: no embedding column. D-010 requires the embedding model be chosen by evaluation
-- on the real corpus, and a pgvector column has a fixed dimension, so adding one now
-- would hardcode a decision the eval is supposed to make. The embedding lands in a
-- sibling table once the model is chosen - see 004_observation_embeddings.sql.template.

create index observations_occurred_at on dami.observations (occurred_at);
create index observations_source on dami.observations (source);

create trigger observations_append_only
    before update or delete on dami.observations
    for each row execute function dami.reject_mutation();

grant insert, select on dami.observations to dami_app;


create table dami.conclusions (
    conclusion_id    uuid        primary key,
    supersedes_id    uuid            null references dami.conclusions (conclusion_id),
    subject          text        not null,
    statement        text        not null,
    confidence       double precision not null,
    source           text        not null,
    concluded_at     timestamptz not null,
    retracted_at     timestamptz     null,
    retraction_reason text           null,

    constraint conclusions_confidence_is_a_probability check (confidence >= 0.0 and confidence <= 1.0),
    constraint conclusions_no_self_supersession check (supersedes_id is distinct from conclusion_id),
    constraint conclusions_retraction_has_a_reason check (
        (retracted_at is null and retraction_reason is null)
        or (retracted_at is not null and retraction_reason is not null)
    )
);

comment on table dami.conclusions is
    'What Dami believes about Steve. Versioned and supersedable; only the active set is ever embedded.';
comment on column dami.conclusions.supersedes_id is
    'Correction replaces rather than coexists (charter §9.4). Following this chain is the audit trail.';

-- The active set: what a retrieval pass may see, and what the month-over-month diff
-- renders. Partial index because the retracted rows are history, not working memory.
create index conclusions_active on dami.conclusions (subject, concluded_at desc) where retracted_at is null;
create index conclusions_supersedes on dami.conclusions (supersedes_id) where supersedes_id is not null;

-- Conclusions are mutable by design: retraction sets retracted_at in place.
grant insert, select, update on dami.conclusions to dami_app;


create table dami.conclusion_observations (
    conclusion_id  uuid not null references dami.conclusions (conclusion_id),
    observation_id uuid not null references dami.observations (observation_id),
    primary key (conclusion_id, observation_id)
);

comment on table dami.conclusion_observations is
    'Provenance. Every conclusion carries the observations that support it; a conclusion with no rows here is an assertion, not an inference.';

create index conclusion_observations_by_observation on dami.conclusion_observations (observation_id);

grant insert, select, delete on dami.conclusion_observations to dami_app;


create table dami.pushbacks (
    pushback_id           uuid        primary key,
    trace_id              uuid        not null,
    challenge             text        not null,
    challenged_assumption text        not null,
    outcome               text        not null,
    occurred_at           timestamptz not null,
    follow_up_note        text            null,

    constraint pushbacks_outcome_known check (
        outcome in ('Accepted', 'Rejected', 'Deferred', 'Unresolved')
    )
);

comment on table dami.pushbacks is
    'D-011. Every challenge Dami makes, and what came of it. A falling rate over time is direct evidence the tuning loop is eating the auditor - which is invisible as tone and visible as a count.';

create index pushbacks_occurred_at on dami.pushbacks (occurred_at);

-- Outcome is filled in later, when Steve reacts, so update is permitted.
grant insert, select, update on dami.pushbacks to dami_app;
