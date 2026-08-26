-- 032 — the disclosure gate's decisions, and Steve's corrections to them (G9a).
--
-- The gate decides per item what may leave for the frontier (pass / disguise /
-- withhold) and, until now, only logged the counts. To learn Steve's boundaries rather
-- than boundaries in general it needs his corrections, and a correction needs the
-- decision it corrects. Decisions are append-only; a correction is one per decision,
-- keyed on it, and is what the gate reads back as an example.

create table dami.disclosure_decisions (
    decision_id uuid        primary key,
    trace_id    uuid        not null,
    question    text        not null,
    original    text        not null,
    disclosure  text        not null,
    sendable    text        not null,
    reason      text        not null,
    decided_at  timestamptz not null,
    constraint disclosure_decisions_known check (disclosure in ('Pass', 'Disguise', 'Withhold'))
);

create index disclosure_decisions_recent on dami.disclosure_decisions (decided_at desc);

create trigger disclosure_decisions_append_only
    before update or delete on dami.disclosure_decisions
    for each statement execute function dami.reject_mutation();

create table dami.disclosure_corrections (
    decision_id  uuid        primary key references dami.disclosure_decisions (decision_id),
    corrected    text        not null,
    note         text        not null,
    corrected_by text        not null,
    corrected_at timestamptz not null,
    constraint disclosure_corrections_known check (corrected in ('Pass', 'Disguise', 'Withhold'))
);

create trigger disclosure_corrections_append_only
    before update or delete on dami.disclosure_corrections
    for each statement execute function dami.reject_mutation();

grant select, insert on dami.disclosure_decisions to dami_app;
grant select, insert on dami.disclosure_corrections to dami_app;
