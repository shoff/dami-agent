-- C4: a memory-informed prompt becomes Egressable only as a reviewed, approved,
-- hash-pinned brief. The brief row is the exact bytes Steve approved; the executor
-- refuses on hash mismatch, so what egresses is provably what was reviewed.

create table dami.egress_briefs (
    brief_id     uuid primary key,
    approval_id  uuid not null references dami.approvals (approval_id),
    trace_id     uuid not null,
    question     text not null,
    brief        text not null,
    brief_sha256 text not null,
    created_at   timestamptz not null,
    sent_at      timestamptz null,
    answer       text null,

    constraint egress_briefs_answer_implies_sent check (answer is null or sent_at is not null)
);

create index egress_briefs_approval on dami.egress_briefs (approval_id);

grant select, insert, update (sent_at, answer) on dami.egress_briefs to dami_app;
