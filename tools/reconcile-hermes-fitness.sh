#!/usr/bin/env bash
# Compares Hermes and dami-data row-for-row and reports drift. Exits non-zero on any.
#
# This is the check the cutover turns on. "The import looked right" is not evidence, and
# this codebase's characteristic failure is the silent one — a vacuous test, an
# unregistered service, a gate that refuses everything while looking principled. A
# reconciler that fails loudly is worth more than an importer that succeeds quietly.
#
# Compares only live rows: Hermes soft-deletes with deleted_at, and those are correctly
# absent here.
#
#   HERMES_PGPASSWORD=... tools/reconcile-hermes-fitness.sh
set -euo pipefail

HERMES_HOST="${HERMES_HOST:-192.168.4.23}"
HERMES_DB="${HERMES_DB:-sbadmin}"
HERMES_USER="${HERMES_USER:-sbadmin}"
DAMI="host=127.0.0.1 port=5432 dbname=dami-data user=dami_app"

if [[ -z "${HERMES_PGPASSWORD:-}" ]]; then
    echo "HERMES_PGPASSWORD is not set; refusing to guess." >&2
    exit 2
fi

hermes() { PGPASSWORD="${HERMES_PGPASSWORD}" psql "host=${HERMES_HOST} port=5432 dbname=${HERMES_DB} user=${HERMES_USER} connect_timeout=8" -Atc "$1"; }
dami()   { psql "${DAMI}" -Atc "$1"; }

drift=0
check() {
    local label="$1" left="$2" right="$3"
    if [[ "${left}" == "${right}" ]]; then
        printf '  ok    %-28s %s\n' "${label}" "${left}"
    else
        printf '  DRIFT %-28s hermes=%s dami=%s\n' "${label}" "${left}" "${right}"
        drift=1
    fi
}

echo "reconciling ${HERMES_USER}@${HERMES_HOST}/${HERMES_DB} against dami-data"

for kind in cardio resistance weight; do
    check "${kind} events" \
        "$(hermes "select count(*) from public.event e join public.event_type t on t.id=e.event_type_id where t.name='${kind}' and e.deleted_at is null")" \
        "$(dami "select count(*) from dami.fitness_event where kind='${kind}'")"
done

check "resistance sets" \
    "$(hermes "select count(*) from public.resistance_set s join public.event e on e.id=s.resistance_event_id where e.deleted_at is null")" \
    "$(dami "select count(*) from dami.fitness_resistance_set")"

check "exercises" \
    "$(hermes "select count(*) from public.exercise")" \
    "$(dami "select count(*) from dami.fitness_exercise")"

# Sums catch the failure counts cannot: a row that arrived with the wrong number in it.
check "total reps" \
    "$(hermes "select coalesce(sum(s.reps),0) from public.resistance_set s join public.event e on e.id=s.resistance_event_id where e.deleted_at is null")" \
    "$(dami "select coalesce(sum(reps),0) from dami.fitness_resistance_set")"

check "total volume lb" \
    "$(hermes "select coalesce(sum(s.reps*s.weight_lbs),0) from public.resistance_set s join public.event e on e.id=s.resistance_event_id where e.deleted_at is null")" \
    "$(dami "select coalesce(sum(reps*weight_lbs),0) from dami.fitness_resistance_set")"

check "cardio distance mi" \
    "$(hermes "select coalesce(sum(c.distance_mi),0) from public.cardio_payload c join public.event e on e.id=c.event_id where e.deleted_at is null")" \
    "$(dami "select coalesce(sum(distance_mi),0) from dami.fitness_cardio")"

check "cardio seconds" \
    "$(hermes "select coalesce(sum(c.duration_seconds),0) from public.cardio_payload c join public.event e on e.id=c.event_id where e.deleted_at is null")" \
    "$(dami "select coalesce(sum(duration_seconds),0) from dami.fitness_cardio")"

# As epoch, not text. The two sessions render timestamptz in their own zones, so
# "21:41:34+00" and "16:41:34-05" are the same instant printed two ways — comparing the
# strings reported drift that did not exist.
check "latest event (epoch)" \
    "$(hermes "select coalesce(extract(epoch from max(e.occurred_at))::bigint::text,'none') from public.event e join public.event_type t on t.id=e.event_type_id where t.name in ('cardio','resistance','weight') and e.deleted_at is null")" \
    "$(dami "select coalesce(extract(epoch from max(occurred_at))::bigint::text,'none') from dami.fitness_event")"

# The one that matters most: an id present upstream and absent here is lost history.
missing="$(hermes "select string_agg(e.id::text, ',' order by e.id) from public.event e
    join public.event_type t on t.id=e.event_type_id
    where t.name in ('cardio','resistance','weight') and e.deleted_at is null")"
present="$(dami "select coalesce(string_agg(source_event_id::text, ',' order by source_event_id),'')
    from dami.fitness_event")"
check "event ids" "${missing:-}" "${present}"

if [[ "${drift}" -ne 0 ]]; then
    echo "RECONCILIATION FAILED — the two databases disagree."
    exit 1
fi

echo "reconciled clean."
