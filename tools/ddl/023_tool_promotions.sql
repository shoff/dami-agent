-- F5c1: one immutable promotion request and single-resolution approval per exact
-- staged tool proposal. Activation remains a later, separately recorded transition.

alter table dami.tool_proposals
    add constraint tool_proposals_id_version_unique
    unique (proposal_id, artifact_version);

create table dami.tool_promotions (
    promotion_id      uuid primary key,
    approval_id       uuid        not null unique
        references dami.approvals (approval_id),
    proposal_id       uuid        not null unique,
    artifact_version  char(64)    not null,

    constraint tool_promotions_artifact_fk
        foreign key (proposal_id, artifact_version)
        references dami.tool_proposals (proposal_id, artifact_version),
    constraint tool_promotions_version_hash check (
        artifact_version ~ '^[0-9a-f]{64}$')
);

create function dami.validate_tool_promotion()
returns trigger
language plpgsql
as $$
begin
    -- PostgreSQL runs BEFORE INSERT triggers before ON CONFLICT. Preserve exact
    -- idempotent retries after the linked approval has subsequently been resolved.
    if exists (
        select 1
          from dami.tool_promotions promotion
         where promotion.promotion_id = new.promotion_id
           and promotion.approval_id = new.approval_id
           and promotion.proposal_id = new.proposal_id
           and promotion.artifact_version = new.artifact_version
    ) then
        return new;
    end if;

    if not exists (
        select 1
          from dami.approvals approval
          join dami.tool_proposals proposal
            on proposal.proposal_id = new.proposal_id
           and proposal.artifact_version = new.artifact_version
         where approval.approval_id = new.approval_id
           and approval.status = 'Pending'
           and approval.resolved_at is null
           and approval.resolved_note is null
           and approval.requested_by = 'tools:promotion'
           and approval.scope = 'tool-promotion'
           and approval.resource =
               'tool-proposal://' || new.proposal_id::text
               || '/versions/' || new.artifact_version::text
           and approval.trace_id = proposal.trace_id
           and approval.parent_span_id = proposal.span_id
           and approval.origin = proposal.origin
    ) then
        raise exception 'tool promotion approval provenance is invalid'
            using errcode = '23514';
    end if;

    return new;
end;
$$;

create trigger tool_promotions_validate
before insert on dami.tool_promotions
for each row execute function dami.validate_tool_promotion();

create trigger tool_promotions_append_only
before update or delete on dami.tool_promotions
for each row execute function dami.reject_mutation();

grant select, insert on dami.tool_promotions to dami_app;
revoke update, delete, truncate, references, trigger on dami.tool_promotions from dami_app;
