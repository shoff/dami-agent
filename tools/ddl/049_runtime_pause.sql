-- 049 — one switch that stops everything Dami starts on her own (docs/agent-landscape-2026-09.md A10).
--
-- A user in the 2026-09-29 survey kept a kill switch on a group-chat agent; every agent that
-- acts unprompted should have one. Paused: no proactive pass, no scheduled job (each is moved
-- to its next run, so resuming does not fire a backlog), no check-in. Replies to Steve and the
-- outage alarm are unaffected. One row; null paused_until with a row present means "until resumed".

create table dami.runtime_pause (
    singleton    boolean     primary key default true,
    paused_until timestamptz     null,
    reason       text        not null,
    set_at       timestamptz not null,
    constraint runtime_pause_one_row check (singleton)
);

grant select, insert, update, delete on dami.runtime_pause to dami_app;
