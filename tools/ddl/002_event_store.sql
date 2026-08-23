-- The canonical execution event store (D-017). OpenTelemetry is an export path; this is
-- the source of truth, so it is append-only in the strong sense: the runtime role holds
-- no UPDATE or DELETE privilege, and a trigger refuses them even for the owner.
--
-- Shape follows dami-core-system-architecture.md §9.2. Note TraceId rather than TurnId,
-- and the Origin discriminator (D-018) - proactive work has no user turn, and without
-- this most of the system's activity would be invisible to the graph.

create table dami.execution_events (
    sequence          bigint generated always as identity primary key,
    event_id          uuid        not null unique,
    trace_id          uuid        not null,
    span_id           uuid        not null,
    parent_span_id    uuid            null,
    origin            text        not null,
    actor_id          text        not null,
    type              text        not null,
    status            text        not null,
    occurred_at       timestamptz not null,
    label             text        not null,
    payload_reference text            null,
    metadata          jsonb           null,

    constraint execution_events_origin_known check (
        origin in ('UserTurn', 'ScheduledService', 'ReactiveTrigger', 'SelfAudit')
    ),
    constraint execution_events_span_not_own_parent check (parent_span_id is distinct from span_id)
);

comment on column dami.execution_events.sequence is
    'Total order of persistence. Not the same as occurred_at, which can arrive out of order.';
comment on column dami.execution_events.event_id is
    'Idempotency key. A replayed emit conflicts on this and is discarded.';
comment on column dami.execution_events.origin is
    'D-018. Constrained because the four values are settled; type is left unconstrained because event types will grow.';

-- Replay of one trace in order; the dominant read.
create index execution_events_trace_sequence on dami.execution_events (trace_id, sequence);
-- Span-tree reconstruction for the workflow graph.
create index execution_events_parent_span on dami.execution_events (parent_span_id) where parent_span_id is not null;
-- Time-window queries for the proactive tier and retention.
create index execution_events_occurred_at on dami.execution_events (occurred_at);

create or replace function dami.reject_mutation() returns trigger
language plpgsql as $$
begin
    raise exception
        'dami.% is append-only; % is not permitted. Drop this trigger deliberately for a migration.',
        tg_table_name, tg_op
        using errcode = 'restrict_violation';
end;
$$;

comment on function dami.reject_mutation() is
    'Makes append-only a property enforced by the database rather than a convention. A deliberate migration drops the trigger, does its work, and recreates it - which leaves a trace in the DDL history.';

create trigger execution_events_append_only
    before update or delete on dami.execution_events
    for each row execute function dami.reject_mutation();

-- The runtime appends and reads. It cannot rewrite history even if a defect tries to.
grant insert, select on dami.execution_events to dami_app;
