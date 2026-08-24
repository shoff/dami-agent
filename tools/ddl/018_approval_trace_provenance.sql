-- G7a1: an approval is a trace node. Origin cannot be inferred truthfully from a
-- requester label, and the originating span is needed when the request came from a
-- tool. Backfill the three currently shipped requester classes before making origin
-- required; unknown historical requesters remain UserTurn because no better evidence
-- exists in the old row.

alter table dami.approvals
    add column origin text null,
    add column parent_span_id uuid null;

update dami.approvals
   set origin = case requested_by
       when 'media-librarian' then 'ScheduledService'
       else 'UserTurn'
   end;

update dami.approvals as approval
   set parent_span_id = proposal.span_id
  from dami.file_patch_proposals as proposal
 where proposal.approval_id = approval.approval_id;

alter table dami.approvals
    alter column origin set not null,
    add constraint approvals_origin_known check (
        origin in ('UserTurn', 'ScheduledService', 'ReactiveTrigger', 'SelfAudit')
    ),
    add constraint approvals_parent_not_self check (
        parent_span_id is distinct from approval_id
    );

comment on column dami.approvals.origin is
    'D-018 origin copied into ApprovalRequested and ApprovalResolved trace events.';
comment on column dami.approvals.parent_span_id is
    'Originating execution span when known; approval_id is the approval event span.';
