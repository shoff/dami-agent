-- F4c: every autonomous skill mutation is durable, diff-bearing, and trace-owned
-- before filesystem materialization. Rows are immutable write-ahead records.

create table dami.skill_changes (
    change_id            uuid primary key,
    trace_id             uuid        not null,
    span_id              uuid        not null,
    parent_span_id       uuid            null,
    origin               text        not null,
    kind                 text        not null,
    skill_id             uuid        not null,
    expected_version     char(64)         null,
    replacement_version  char(64)         null,
    replacement_document jsonb            null,
    diff                 text        not null,
    requested_at         timestamptz not null,

    constraint skill_changes_parent_not_self check (
        parent_span_id is null or parent_span_id <> span_id),
    constraint skill_changes_origin_known check (
        origin in ('UserTurn', 'ScheduledService', 'ReactiveTrigger', 'SelfAudit')),
    constraint skill_changes_kind_known check (kind in ('Author', 'Revise', 'Retire')),
    constraint skill_changes_expected_hash check (
        expected_version is null or expected_version ~ '^[0-9a-f]{64}$'),
    constraint skill_changes_replacement_hash check (
        replacement_version is null or replacement_version ~ '^[0-9a-f]{64}$'),
    constraint skill_changes_shape check (
        (kind = 'Author' and expected_version is null
         and replacement_version is not null and replacement_document is not null)
        or (kind = 'Revise' and expected_version is not null
            and replacement_version is not null and replacement_document is not null)
        or (kind = 'Retire' and expected_version is not null
            and replacement_version is null and replacement_document is null)),
    constraint skill_changes_diff_present check (btrim(diff) <> ''),
    constraint skill_changes_diff_bounded check (octet_length(diff) <= 1048576)
);

create index skill_changes_skill_time
    on dami.skill_changes (skill_id, requested_at desc, change_id);

create trigger skill_changes_append_only
before update or delete on dami.skill_changes
for each row execute function dami.reject_mutation();

grant select, insert on dami.skill_changes to dami_app;
revoke update, delete, truncate, references, trigger on dami.skill_changes from dami_app;
