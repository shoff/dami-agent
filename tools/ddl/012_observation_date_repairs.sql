-- B10: the migration left 278 observations with epoch-zero occurred_at. Observations
-- are append-only by trigger, so the repair is a sidecar, not an UPDATE: one row per
-- examined observation, carrying the recovered date or recording that none was
-- recoverable. Reads coalesce through this table; the original row is never touched.

create table dami.observation_date_repairs (
    observation_id       uuid primary key references dami.observations (observation_id),
    repaired_occurred_at timestamptz null,   -- null: examined, nothing recoverable
    method               text not null,
    repaired_at          timestamptz not null default now(),

    constraint observation_date_repairs_method_known check (
        method in ('body-iso', 'body-prose', 'manual', 'unrecoverable')
    ),
    constraint observation_date_repairs_unrecoverable_is_null check (
        (method = 'unrecoverable') = (repaired_occurred_at is null)
    )
);

grant select, insert on dami.observation_date_repairs to dami_app;
