-- F5c3a: immutable exact-artifact verification and activation outcome state.
-- Activation outcomes are added after their contract is driven by its focused test.

create table dami.tool_verifications (
    verification_id  uuid primary key,
    proposal_id      uuid        not null,
    artifact_version char(64)    not null,
    assembly_sha256  char(64)    not null,
    test_evidence    text        not null,
    verified_at      timestamptz not null,

    constraint tool_verifications_artifact_fk
        foreign key (proposal_id, artifact_version)
        references dami.tool_proposals (proposal_id, artifact_version),
    constraint tool_verifications_artifact_unique
        unique (proposal_id, artifact_version),
    constraint tool_verifications_version_hash check (
        artifact_version ~ '^[0-9a-f]{64}$'),
    constraint tool_verifications_assembly_hash check (
        assembly_sha256 ~ '^[0-9a-f]{64}$'),
    constraint tool_verifications_evidence_bounded check (
        octet_length(test_evidence) between 1 and 65536)
);

create trigger tool_verifications_append_only
before update or delete on dami.tool_verifications
for each row execute function dami.reject_mutation();

grant select, insert on dami.tool_verifications to dami_app;
revoke update, delete, truncate, references, trigger on dami.tool_verifications from dami_app;

create table dami.tool_activation_outcomes (
    activation_id    uuid primary key,
    promotion_id     uuid        not null
        references dami.tool_promotions (promotion_id),
    verification_id  uuid        not null
        references dami.tool_verifications (verification_id),
    status            text        not null,
    failure_code      text            null,
    occurred_at       timestamptz not null,

    constraint tool_activation_outcomes_status_known check (
        status in ('Activated', 'Failed')),
    constraint tool_activation_outcomes_failure_shape check (
        (status = 'Activated' and failure_code is null)
        or (status = 'Failed' and length(failure_code) between 1 and 128))
);

create unique index tool_activation_outcomes_one_success
    on dami.tool_activation_outcomes (promotion_id)
    where status = 'Activated';

create function dami.validate_tool_activation_outcome()
returns trigger
language plpgsql
as $$
begin
    -- Serialize terminal-state checks per promotion. The partial unique index alone
    -- does not conflict with a concurrent Failed insert.
    perform 1
      from dami.tool_promotions promotion
     where promotion.promotion_id = new.promotion_id
       for update;

    -- Preserve an exact idempotent retry, including after success became terminal.
    if exists (
        select 1
          from dami.tool_activation_outcomes outcome
         where outcome.activation_id = new.activation_id
           and outcome.promotion_id = new.promotion_id
           and outcome.verification_id = new.verification_id
           and outcome.status = new.status
           and outcome.failure_code is not distinct from new.failure_code
           and outcome.occurred_at = new.occurred_at
    ) then
        return new;
    end if;

    if exists (
        select 1
          from dami.tool_activation_outcomes outcome
         where outcome.promotion_id = new.promotion_id
           and outcome.status = 'Activated'
    ) then
        raise exception 'successful tool activation is terminal'
            using errcode = '23514';
    end if;

    if not exists (
        select 1
          from dami.tool_promotions promotion
          join dami.approvals approval
            on approval.approval_id = promotion.approval_id
          join dami.tool_verifications verification
            on verification.verification_id = new.verification_id
           and verification.proposal_id = promotion.proposal_id
           and verification.artifact_version = promotion.artifact_version
         where promotion.promotion_id = new.promotion_id
           and approval.status = 'Approved'
           and approval.resolved_at is not null
    ) then
        raise exception 'tool activation requires an approved exact verification'
            using errcode = '23514';
    end if;

    return new;
end;
$$;

create trigger tool_activation_outcomes_validate
before insert on dami.tool_activation_outcomes
for each row execute function dami.validate_tool_activation_outcome();

create trigger tool_activation_outcomes_append_only
before update or delete on dami.tool_activation_outcomes
for each row execute function dami.reject_mutation();

grant select, insert on dami.tool_activation_outcomes to dami_app;
revoke update, delete, truncate, references, trigger
    on dami.tool_activation_outcomes from dami_app;

create or replace function dami.validate_tool_promotion()
returns trigger
language plpgsql
as $$
begin
    -- Preserve exact retries after the linked approval has been resolved.
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
          join dami.tool_verifications verification
            on verification.proposal_id = proposal.proposal_id
           and verification.artifact_version = proposal.artifact_version
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
        raise exception 'tool promotion approval provenance or verification is invalid'
            using errcode = '23514';
    end if;

    return new;
end;
$$;
