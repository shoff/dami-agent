-- F5c3c: serialize activation terminal-state checks without requiring the runtime
-- role to hold UPDATE on the immutable tool_promotions table. SELECT ... FOR UPDATE
-- requires UPDATE privilege even when no update occurs, so migration 024's trigger
-- could not execute under dami_app.

create or replace function dami.validate_tool_activation_outcome()
returns trigger
language plpgsql
as $$
begin
    -- Every activation writer passes through this trigger. A transaction-scoped lock
    -- keyed by promotion therefore serializes Failed/Activated checks without granting
    -- mutation rights on an append-only table. Hash collisions only add serialization.
    perform pg_advisory_xact_lock(
        hashtextextended('dami.tool_activation:' || new.promotion_id::text, 0));

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
