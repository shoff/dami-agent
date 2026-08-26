-- 029 — a detail slot on task-board claims and completions.
--
-- 028 gave TaskStatusChanged a detail and required it, but TaskClaimed and TaskCompleted
-- had none, so an import that recorded its source revision could attach it to sixteen of
-- 338 rows. The activity table already allows detail on every kind; only the two
-- functions lacked the parameter.
--
-- The six-argument signatures are kept as thin wrappers so a runtime built against 028
-- keeps working until it is redeployed. New callers pass the seventh argument.

create or replace function dami.task_board_try_claim(
    p_event uuid,
    p_task uuid,
    p_version bigint,
    p_actor text,
    p_actor_kind text,
    p_claimed_at timestamptz,
    p_detail text
) returns boolean
language sql
security definer
set search_path = ''
as $function$
    with changed as (
        update dami.task_board_tasks candidate
           set status = 'InProgress', claimed_by_id = p_actor,
               claimed_by_kind = p_actor_kind, claimed_at = p_claimed_at,
               updated_at = p_claimed_at, version = version + 1
         where task_id = p_task and version = p_version
           and status = 'Open' and claimed_by_id is null
           and not exists (
               select 1 from dami.task_prerequisites edge
                 join dami.task_board_tasks prerequisite
                   on prerequisite.task_id = edge.prerequisite_task_id
                where edge.task_id = candidate.task_id
                  and prerequisite.status <> 'Done'
           )
        returning board_id
    ), logged as (
        insert into dami.task_board_activity
            (event_id, board_id, task_id, kind, actor_id, actor_kind, occurred_at, detail)
        select p_event, board_id, p_task, 'TaskClaimed', p_actor, p_actor_kind, p_claimed_at,
               nullif(btrim(p_detail), '')
          from changed
        returning true as succeeded
    )
    select coalesce((select succeeded from logged), false);
$function$;

create or replace function dami.task_board_try_complete(
    p_event uuid,
    p_task uuid,
    p_version bigint,
    p_actor text,
    p_actor_kind text,
    p_completed_at timestamptz,
    p_detail text
) returns boolean
language sql
security definer
set search_path = ''
as $function$
    with changed as (
        update dami.task_board_tasks candidate
           set status = 'Done', updated_at = p_completed_at, version = version + 1
         where task_id = p_task and version = p_version and status = 'InProgress'
           and claimed_by_id = p_actor and claimed_by_kind = p_actor_kind
           and not exists (
               select 1 from dami.task_acceptance_criteria criterion
                where criterion.task_id = candidate.task_id and not criterion.is_satisfied
           )
           and not exists (
               select 1 from dami.task_board_tasks child
                where child.parent_task_id = candidate.task_id
                  and child.status not in ('Done', 'Cancelled')
           )
           and not exists (
               select 1 from dami.task_prerequisites edge
                 join dami.task_board_tasks prerequisite
                   on prerequisite.task_id = edge.prerequisite_task_id
                where edge.task_id = candidate.task_id
                  and prerequisite.status <> 'Done'
           )
        returning board_id
    ), logged as (
        insert into dami.task_board_activity
            (event_id, board_id, task_id, kind, actor_id, actor_kind, occurred_at, detail)
        select p_event, board_id, p_task, 'TaskCompleted', p_actor, p_actor_kind, p_completed_at,
               nullif(btrim(p_detail), '')
          from changed
        returning true as succeeded
    )
    select coalesce((select succeeded from logged), false);
$function$;

-- Compatibility wrappers for a runtime built against 028.
create or replace function dami.task_board_try_claim(
    p_event uuid, p_task uuid, p_version bigint, p_actor text, p_actor_kind text,
    p_claimed_at timestamptz
) returns boolean
language sql
security definer
set search_path = ''
as $function$
    select dami.task_board_try_claim(
        p_event, p_task, p_version, p_actor, p_actor_kind, p_claimed_at, null);
$function$;

create or replace function dami.task_board_try_complete(
    p_event uuid, p_task uuid, p_version bigint, p_actor text, p_actor_kind text,
    p_completed_at timestamptz
) returns boolean
language sql
security definer
set search_path = ''
as $function$
    select dami.task_board_try_complete(
        p_event, p_task, p_version, p_actor, p_actor_kind, p_completed_at, null);
$function$;

revoke all on function dami.task_board_try_claim(uuid, uuid, bigint, text, text, timestamptz, text)
    from public;
revoke all on function dami.task_board_try_complete(uuid, uuid, bigint, text, text, timestamptz, text)
    from public;
grant execute on function dami.task_board_try_claim(uuid, uuid, bigint, text, text, timestamptz, text)
    to dami_app;
grant execute on function dami.task_board_try_complete(uuid, uuid, bigint, text, text, timestamptz, text)
    to dami_app;
