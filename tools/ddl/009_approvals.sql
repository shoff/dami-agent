-- Approvals (charter 10.2): first-class blocking records, not transient dialogs.
-- Consequential actions wait here until a human resolves them; the resolution is
-- durable and the whole exchange lives in the trace.

create table dami.approvals (
    approval_id   uuid        primary key,
    trace_id      uuid        not null,
    requested_by  text        not null,
    action        text        not null,
    scope         text        not null,
    resource      text        not null,
    status        text        not null,
    requested_at  timestamptz not null,
    resolved_at   timestamptz     null,
    resolved_note text            null,
    expires_at    timestamptz     null,

    constraint approvals_status_known check (
        status in ('Pending', 'Approved', 'Denied', 'Expired')
    ),
    constraint approvals_resolution_consistent check (
        (status = 'Pending' and resolved_at is null)
        or (status <> 'Pending' and resolved_at is not null)
    )
);

comment on table dami.approvals is
    'Charter 10.2: stable id, trace association, human-readable action, scope and resource, allowed responses, expiration, durable resolution. The GUI and CLI answer through this same table.';

create index approvals_pending on dami.approvals (requested_at) where status = 'Pending';

grant insert, select, update on dami.approvals to dami_app;
