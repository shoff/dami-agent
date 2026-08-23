-- Migration bookkeeping. Applied first and by hand-rolled idempotence, because it is
-- the table the runner uses to decide what has already run.
create table if not exists dami.schema_migrations (
    filename     text        primary key,
    applied_at   timestamptz not null default now(),
    checksum     text        not null
);

comment on table dami.schema_migrations is
    'One row per applied DDL file. checksum detects a file edited after it was applied.';

grant select on dami.schema_migrations to dami_app;
