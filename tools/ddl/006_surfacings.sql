-- Surfacings: the things Dami actually says unprompted (D-021).
--
-- Deliberately a separate table from conclusions. Most passes produce conclusions and no
-- surfacings; that asymmetry is the scarcity principle, and keeping the queue separate
-- means the cap and the feedback loop live on exactly the rows that reached for Steve's
-- attention.
--
-- Every surfacing carries feedback columns because D-019's whole design is that the
-- reaction trains the interest model: "Steve knows within thirty seconds whether a
-- recommendation was good".

create table dami.surfacings (
    surfacing_id  uuid        primary key,
    trace_id      uuid        not null,
    service_name  text        not null,
    title         text        not null,
    body          text        not null,
    confidence    double precision not null,
    status        text        not null,
    created_at    timestamptz not null,
    delivered_at  timestamptz     null,
    feedback      text            null,
    feedback_at   timestamptz     null,

    constraint surfacings_confidence_is_a_probability check (confidence >= 0.0 and confidence <= 1.0),
    constraint surfacings_status_known check (status in ('Pending', 'Delivered', 'Suppressed')),
    constraint surfacings_feedback_has_a_timestamp check (
        (feedback is null and feedback_at is null) or (feedback is not null and feedback_at is not null)
    )
);

comment on table dami.surfacings is
    'What Dami said, or tried to say, unprompted. Suppressed rows are surfacings the cap refused - kept rather than dropped, because a cap that silently discards is invisible in the audit.';
comment on column dami.surfacings.feedback is
    'Steve''s reaction, recorded when he gives one. This is the training signal every proactive service depends on (D-019).';

-- The queue read: pending items, oldest first.
create index surfacings_pending on dami.surfacings (created_at) where status = 'Pending';
-- The cap check: how many did this service surface in a window.
create index surfacings_by_service_created on dami.surfacings (service_name, created_at);

grant insert, select, update on dami.surfacings to dami_app;
