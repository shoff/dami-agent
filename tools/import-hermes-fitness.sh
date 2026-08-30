#!/usr/bin/env bash
# Phase 1 of the Hermes fitness cutover: copy history into dami-data, additively.
#
# Hermes keeps writing. This is a read-only pull that can be run again at any time — every
# row carries its Hermes id (source_event_id, source_set_id) under a unique constraint, so
# a second run inserts only what is new. Those ids are also what phase 2 reconciles on:
# comparing the two databases row-for-row is only possible because identity survives the
# copy.
#
# The password is never in this file. Export it first:
#   HERMES_PGPASSWORD=... tools/import-hermes-fitness.sh
set -euo pipefail

HERMES_HOST="${HERMES_HOST:-192.168.4.23}"
HERMES_DB="${HERMES_DB:-sbadmin}"
HERMES_USER="${HERMES_USER:-sbadmin}"
DAMI="host=127.0.0.1 port=5432 dbname=dami-data user=dami_app"

if [[ -z "${HERMES_PGPASSWORD:-}" ]]; then
    echo "HERMES_PGPASSWORD is not set; refusing to guess." >&2
    exit 2
fi

hermes() { PGPASSWORD="${HERMES_PGPASSWORD}" psql "host=${HERMES_HOST} port=5432 dbname=${HERMES_DB} user=${HERMES_USER} connect_timeout=8" "$@"; }
dami()   { psql "${DAMI}" "$@"; }

work="$(mktemp -d)"
trap 'rm -rf "${work}"' EXIT

echo "pulling from ${HERMES_USER}@${HERMES_HOST}/${HERMES_DB}"

hermes -Atc "\copy (
    select e.id, e.name, mg.name, eq.name, e.notes
    from public.exercise e
    left join public.muscle_group mg on mg.id = e.primary_muscle_group_id
    left join public.equipment eq on eq.id = e.equipment_id
) to stdout csv" > "${work}/exercise.csv"

hermes -Atc "\copy (
    select e.id, e.occurred_at, e.duration_minutes, et.name, pt.name, sl.name, s.name,
           e.location_name, e.notes, coalesce(e.verified_by_user, false)
    from public.event e
    join public.event_type et on et.id = e.event_type_id
    join public.precision_type pt on pt.id = e.precision_id
    join public.sensitivity_level sl on sl.id = e.sensitivity_id
    join public.source s on s.id = e.source_id
    where e.deleted_at is null and et.name in ('cardio','resistance','weight')
) to stdout csv" > "${work}/event.csv"

hermes -Atc "\copy (
    select c.event_id, m.name, c.duration_seconds, c.distance_mi, c.calories, c.speed_mph,
           c.incline_pct, c.watts_avg, c.watts_max, c.mets_avg, c.hr_avg, c.hr_max,
           coalesce(c.is_pr,false), c.pr_dimension, c.notes
    from public.cardio_payload c
    join public.modality m on m.id = c.modality_id
    join public.event e on e.id = c.event_id and e.deleted_at is null
) to stdout csv" > "${work}/cardio.csv"

hermes -Atc "\copy (
    select r.event_id, r.location, r.notes
    from public.resistance_payload r
    join public.event e on e.id = r.event_id and e.deleted_at is null
) to stdout csv" > "${work}/resistance.csv"

hermes -Atc "\copy (
    select s.id, s.resistance_event_id, s.exercise_id, s.set_number, s.reps, s.weight_lbs,
           s.rpe, coalesce(s.is_warmup,false), s.notes
    from public.resistance_set s
    join public.event e on e.id = s.resistance_event_id and e.deleted_at is null
) to stdout csv" > "${work}/sets.csv"

hermes -Atc "\copy (
    select w.event_id, w.weight_lbs, w.notes
    from public.weight_payload w
    join public.event e on e.id = w.event_id and e.deleted_at is null
) to stdout csv" > "${work}/weight.csv"

wc -l "${work}"/*.csv | sed 's|.*/||'

echo "loading into dami-data"
sed "s|WORK|${work}|g" > "${work}/load.sql" <<'SQL'
begin;

create temp table s_exercise(id int, name text, muscle text, equipment text, notes text) on commit drop;
create temp table s_event(id bigint, occurred_at timestamptz, duration_minutes int, kind text,
                          precision text, sensitivity text, source text, location_name text,
                          notes text, verified boolean) on commit drop;
create temp table s_cardio(event_id bigint, modality text, duration_seconds int, distance_mi numeric,
                           calories int, speed_mph numeric, incline_pct numeric, watts_avg int,
                           watts_max int, mets_avg numeric, hr_avg int, hr_max int, is_pr boolean,
                           pr_dimension text, notes text) on commit drop;
create temp table s_resistance(event_id bigint, location text, notes text) on commit drop;
create temp table s_set(id bigint, event_id bigint, exercise_id int, set_number smallint, reps smallint,
                        weight_lbs numeric, rpe smallint, is_warmup boolean, notes text) on commit drop;
create temp table s_weight(event_id bigint, weight_lbs numeric, notes text) on commit drop;

\copy s_exercise from 'WORK/exercise.csv' csv
\copy s_event from 'WORK/event.csv' csv
\copy s_cardio from 'WORK/cardio.csv' csv
\copy s_resistance from 'WORK/resistance.csv' csv
\copy s_set from 'WORK/sets.csv' csv
\copy s_weight from 'WORK/weight.csv' csv

insert into dami.fitness_exercise (exercise_id, name, primary_muscle_group, equipment, notes)
select id, name, muscle, equipment, notes from s_exercise
on conflict (exercise_id) do update
    set name = excluded.name,
        primary_muscle_group = excluded.primary_muscle_group,
        equipment = excluded.equipment;

insert into dami.fitness_event (fitness_event_id, source_event_id, occurred_at, duration_minutes,
                                kind, precision, sensitivity, source, location_name, notes,
                                verified_by_user)
select gen_random_uuid(), id, occurred_at, duration_minutes, kind, precision, sensitivity,
       source, location_name, notes, verified
from s_event
on conflict (source_event_id) do nothing;

insert into dami.fitness_cardio (fitness_event_id, modality, duration_seconds, distance_mi, calories,
                                 speed_mph, incline_pct, watts_avg, watts_max, mets_avg, hr_avg,
                                 hr_max, is_pr, pr_dimension, notes)
select f.fitness_event_id, c.modality, c.duration_seconds, c.distance_mi, c.calories, c.speed_mph,
       c.incline_pct, c.watts_avg, c.watts_max, c.mets_avg,
       nullif(c.hr_avg, 0), nullif(c.hr_max, 0), c.is_pr, c.pr_dimension, c.notes
from s_cardio c join dami.fitness_event f on f.source_event_id = c.event_id
on conflict (fitness_event_id) do nothing;

insert into dami.fitness_resistance (fitness_event_id, location, notes)
select f.fitness_event_id, r.location, r.notes
from s_resistance r join dami.fitness_event f on f.source_event_id = r.event_id
on conflict (fitness_event_id) do nothing;

insert into dami.fitness_resistance_set (set_id, source_set_id, fitness_event_id, exercise_id,
                                         set_number, reps, weight_lbs, rpe, is_warmup, notes)
select gen_random_uuid(), s.id, f.fitness_event_id, s.exercise_id, s.set_number, s.reps,
       s.weight_lbs, s.rpe, s.is_warmup, s.notes
from s_set s
join dami.fitness_event f on f.source_event_id = s.event_id
join dami.fitness_resistance r on r.fitness_event_id = f.fitness_event_id
on conflict (source_set_id) do nothing;

insert into dami.fitness_weight (fitness_event_id, weight_lbs, notes)
select f.fitness_event_id, w.weight_lbs, w.notes
from s_weight w join dami.fitness_event f on f.source_event_id = w.event_id
on conflict (fitness_event_id) do nothing;

commit;
SQL

dami -v ON_ERROR_STOP=1 -f "${work}/load.sql"

echo "imported:"
dami -Atc "select 'events '||count(*) from dami.fitness_event
union all select 'cardio '||count(*) from dami.fitness_cardio
union all select 'resistance '||count(*) from dami.fitness_resistance
union all select 'sets '||count(*) from dami.fitness_resistance_set
union all select 'weight '||count(*) from dami.fitness_weight
union all select 'exercises '||count(*) from dami.fitness_exercise"
