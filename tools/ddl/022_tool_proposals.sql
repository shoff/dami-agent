-- F5a: inert self-authored tool review artifacts. Staging is not compilation,
-- approval, promotion, registration, or execution.

create table dami.tool_proposals (
    proposal_id       uuid primary key,
    trace_id          uuid        not null,
    span_id           uuid        not null,
    parent_span_id    uuid            null,
    origin            text        not null,
    capability_id     uuid        not null,
    artifact_version  char(64)    not null,
    artifact          jsonb       not null,
    proposed_at       timestamptz not null,

    constraint tool_proposals_parent_not_self check (
        parent_span_id is null or parent_span_id <> span_id),
    constraint tool_proposals_origin_known check (
        origin in ('UserTurn', 'ScheduledService', 'ReactiveTrigger', 'SelfAudit')),
    constraint tool_proposals_version_hash check (
        artifact_version ~ '^[0-9a-f]{64}$'),
    constraint tool_proposals_version_matches_artifact check (
        artifact_version = artifact ->> 'Version'),
    constraint tool_proposals_artifact_object check (
        jsonb_typeof(artifact) = 'object'),
    constraint tool_proposals_capability_matches_artifact check (
        capability_id = ((artifact #>> '{Schema,CapabilityId}')::uuid)),
    constraint tool_proposals_artifact_bounded check (
        octet_length(artifact::text) <= 16777216)
);

create index tool_proposals_capability_time
    on dami.tool_proposals (capability_id, proposed_at desc, proposal_id);

create trigger tool_proposals_append_only
before update or delete on dami.tool_proposals
for each row execute function dami.reject_mutation();

grant select, insert on dami.tool_proposals to dami_app;
revoke update, delete, truncate, references, trigger on dami.tool_proposals from dami_app;
