-- M1: exactly one authoritative gateway. The charter is explicit that a second
-- Discord gateway must not run during cutover — two bots on one token means every
-- message is answered twice, and the failure is invisible from inside either process.
--
-- The lock itself is a Postgres session advisory lock (no table needed for
-- correctness); this table exists so that "who holds it, since when, and on what
-- host" is answerable without attaching a debugger to a running service.

create table dami.gateway_authority (
    gateway_name text primary key,
    holder_host  text not null,
    holder_pid   integer not null,
    acquired_at  timestamptz not null default now(),
    heartbeat_at timestamptz not null default now()
);

grant select, insert, update on dami.gateway_authority to dami_app;
