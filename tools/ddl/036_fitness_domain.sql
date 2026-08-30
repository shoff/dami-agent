-- 036 — physical fitness as its own domain (H9, phase 1 of the Hermes cutover).
--
-- Not a seventh category on dami.health_events. That table is medical, CHECK-constrained
-- to diagnosis/appointment/medication/vital/procedure/symptom, and it is fed by an LLM
-- extracting facts from narrative observations. Fitness is the opposite shape: already
-- structured, numeric, high volume, and nothing to extract. "8 reps at 135 lb, RPE 7" is
-- not a sentence to be read, it is a row.
--
-- The design is a faithful port of the Hermes schema on the Mac mini (sbadmin), which has
-- been logging daily since 2026-04-04 and is better than anything here. Reference tables
-- become text with CHECK constraints, following dami.health_events rather than carrying a
-- dozen lookup tables across.
--
-- source_event_id is the whole point of phase 1: it keeps the Hermes identity on every
-- row, so the import is idempotent and so phase 2 can reconcile the two databases
-- row-for-row instead of trusting that they agree.
--
-- LocalOnly (D-012). Health data never reaches an egress client, and no collector that
-- holds one may read these tables.

create table dami.fitness_event (
    fitness_event_id uuid        primary key,
    source_event_id  bigint      unique,
    occurred_at      timestamptz not null,
    duration_minutes integer,
    kind             text        not null,
    precision        text        not null,
    sensitivity      text        not null,
    source           text        not null,
    location_name    text,
    notes            text,
    verified_by_user boolean     not null default false,
    recorded_at      timestamptz not null default now(),
    constraint fitness_event_kind_known
        check (kind in ('cardio', 'resistance', 'weight')),
    constraint fitness_event_precision_known
        check (precision in ('exact', 'approximate', 'vague', 'all_day')),
    constraint fitness_event_sensitivity_known
        check (sensitivity in ('normal', 'private')),
    constraint fitness_event_source_known
        check (source in ('claude_chat', 'manual_entry', 'apple_health', 'polar',
                          'gym_machine', 'other', 'clinic_machine', 'clinical_staff')),
    constraint fitness_event_duration_sane
        check (duration_minutes is null or duration_minutes between 0 and 1440)
);

create index fitness_event_by_date on dami.fitness_event (occurred_at desc);
create index fitness_event_by_kind_date on dami.fitness_event (kind, occurred_at desc);

-- Kept with Hermes's integer ids so a set imported today still names the same lift after
-- the cutover, and so a reconciliation mismatch points at one exercise rather than a name.
create table dami.fitness_exercise (
    exercise_id          integer     primary key,
    name                 text        not null unique,
    primary_muscle_group text,
    body_region          text,
    equipment            text,
    notes                text,
    constraint fitness_exercise_name_present check (length(btrim(name)) > 0),
    constraint fitness_exercise_equipment_known
        check (equipment is null or equipment in ('barbell', 'dumbbell', 'cable', 'machine',
                                                  'bodyweight', 'kettlebell', 'band', 'other'))
);

create table dami.fitness_cardio (
    fitness_event_id uuid    primary key references dami.fitness_event (fitness_event_id)
                             on delete cascade,
    modality         text    not null,
    duration_seconds integer,
    distance_mi      numeric,
    calories         integer,
    speed_mph        numeric,
    incline_pct      numeric,
    watts_avg        integer,
    watts_max        integer,
    mets_avg         numeric,
    hr_avg           integer,
    hr_max           integer,
    is_pr            boolean not null default false,
    pr_dimension     text,
    notes            text,
    constraint fitness_cardio_modality_known
        check (modality in ('treadmill', 'elliptical', 'rowing', 'cycling', 'walking',
                            'swimming', 'sauna', 'yard_work', 'other_cardio')),
    -- A heart rate of zero is a missing reading recorded as a number, which is worse than
    -- a null because it averages.
    constraint fitness_cardio_hr_sane
        check ((hr_avg is null or hr_avg between 20 and 250)
           and (hr_max is null or hr_max between 20 and 250)),
    constraint fitness_cardio_hr_ordered
        check (hr_avg is null or hr_max is null or hr_max >= hr_avg)
);

create table dami.fitness_resistance (
    fitness_event_id uuid primary key references dami.fitness_event (fitness_event_id)
                          on delete cascade,
    location         text,
    notes            text
);

create table dami.fitness_resistance_set (
    set_id           uuid     primary key,
    source_set_id    bigint   unique,
    fitness_event_id uuid     not null references dami.fitness_resistance (fitness_event_id)
                              on delete cascade,
    exercise_id      integer  references dami.fitness_exercise (exercise_id),
    set_number       smallint not null,
    reps             smallint,
    weight_lbs       numeric,
    rpe              smallint,
    is_warmup        boolean  not null default false,
    notes            text,
    constraint fitness_set_number_positive check (set_number > 0),
    constraint fitness_set_reps_sane check (reps is null or reps between 0 and 500),
    constraint fitness_set_weight_sane check (weight_lbs is null or weight_lbs between 0 and 2000),
    -- RPE is a 1-10 scale; anything else is a units mistake worth failing on.
    constraint fitness_set_rpe_sane check (rpe is null or rpe between 1 and 10),
    constraint fitness_set_unique_per_event unique (fitness_event_id, set_number, exercise_id)
);

create index fitness_set_by_exercise on dami.fitness_resistance_set (exercise_id);

create table dami.fitness_weight (
    fitness_event_id uuid    primary key references dami.fitness_event (fitness_event_id)
                             on delete cascade,
    weight_lbs       numeric not null,
    notes            text,
    constraint fitness_weight_sane check (weight_lbs between 20 and 1000)
);

grant select, insert, update on dami.fitness_event to dami_app;
grant select, insert, update on dami.fitness_exercise to dami_app;
grant select, insert, update on dami.fitness_cardio to dami_app;
grant select, insert, update on dami.fitness_resistance to dami_app;
grant select, insert, update on dami.fitness_resistance_set to dami_app;
grant select, insert, update on dami.fitness_weight to dami_app;
