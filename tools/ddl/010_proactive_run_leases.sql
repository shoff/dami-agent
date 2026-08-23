-- A cadence read followed by execution is not atomic across host processes. This
-- expiring lease admits one scheduler per service while still recovering after a crash.

create table dami.proactive_run_leases (
    service_name text        primary key,
    lease_id     uuid        not null,
    expires_at   timestamptz not null
);

comment on table dami.proactive_run_leases is
    'Expiring cross-process ownership for proactive service execution.';

grant select, insert, update, delete on dami.proactive_run_leases to dami_app;
