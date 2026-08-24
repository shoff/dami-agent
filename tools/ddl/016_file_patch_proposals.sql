-- G6c3: exact replacement text and its preimage are durable before a filesystem
-- approval can be resolved. Runtime may insert/select; proposal bytes are immutable.

create table dami.file_patch_proposals (
    proposal_id        uuid primary key,
    approval_id        uuid not null unique references dami.approvals (approval_id),
    trace_id           uuid not null,
    span_id            uuid not null,
    relative_path      text not null,
    replacement_content text not null,
    replacement_sha256 char(64) not null,
    expected_sha256    char(64) null,
    created_at         timestamptz not null,

    constraint file_patch_path_present check (btrim(relative_path) <> ''),
    constraint file_patch_replacement_hash check (replacement_sha256 ~ '^[0-9A-Fa-f]{64}$'),
    constraint file_patch_expected_hash check (
        expected_sha256 is null or expected_sha256 ~ '^[0-9A-Fa-f]{64}$')
);

create trigger file_patch_proposals_append_only
before update or delete on dami.file_patch_proposals
for each row execute function dami.reject_mutation();

grant select, insert on dami.file_patch_proposals to dami_app;
