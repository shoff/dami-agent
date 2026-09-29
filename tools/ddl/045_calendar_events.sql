-- 045 — Steve's calendar as last read, for a window ahead (docs/agent-landscape-2026-09.md D1).
--
-- Read-only at the source: Google's "secret address in iCal format", fetched by the
-- calendar collector. Replaced per window on each read rather than appended, so a meeting
-- moved or cancelled at the source is gone here too. One row per occurrence: a weekly
-- meeting is its Tuesdays in the window.

create table dami.calendar_events (
    event_key  text        primary key,
    starts_at  timestamptz not null,
    ends_at    timestamptz     null,
    all_day    boolean     not null,
    summary    text        not null,
    location   text            null,
    fetched_at timestamptz not null
);

create index calendar_events_by_start on dami.calendar_events (starts_at);

comment on table dami.calendar_events is
    'Steve''s calendar, read-only mirror of a window ahead. Profile-derived: reaches a frontier model only through the disclosure gate.';

grant select, insert, delete on dami.calendar_events to dami_app;
