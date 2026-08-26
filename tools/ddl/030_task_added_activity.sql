-- 030 — tasks can be added to a board that already exists.
--
-- 028 creates a board and its whole tree atomically and has no path for one more task;
-- a blueprint re-import or a person at the CLI therefore could not add work without a
-- new board. The ledger gains a kind for it. Insert rights already exist for dami_app.

alter table dami.task_board_activity
    drop constraint task_board_activity_kind_known;

alter table dami.task_board_activity
    add constraint task_board_activity_kind_known check (
        kind in ('BoardCreated', 'TaskAdded', 'TaskClaimed', 'CriterionSatisfied',
                 'CriterionReopened', 'TaskCompleted', 'TaskStatusChanged')
    );

-- Scope added under finished work reopens it. try_set_status deliberately has no
-- Done -> Open transition for hands; this one exists only for the add path, which runs it
-- in the same transaction as the insert, so a Done parent never holds an Open child.
-- A Cancelled parent stays cancelled: the add is refused instead.
create function dami.task_board_reopen_for_child(
    p_event uuid,
    p_task uuid,
    p_actor text,
    p_actor_kind text,
    p_detail text,
    p_changed_at timestamptz
) returns boolean
language sql
security definer
set search_path = ''
as $function$
    with changed as (
        update dami.task_board_tasks
           set status = 'Open', claimed_by_id = null, claimed_by_kind = null, claimed_at = null,
               updated_at = p_changed_at, version = version + 1
         where task_id = p_task and status = 'Done'
        returning board_id
    ), logged as (
        insert into dami.task_board_activity
            (event_id, board_id, task_id, kind, actor_id, actor_kind, occurred_at,
             from_status, to_status, detail)
        select p_event, board_id, p_task, 'TaskStatusChanged', p_actor, p_actor_kind,
               p_changed_at, 'Done', 'Open', p_detail
          from changed
        returning true as succeeded
    )
    select coalesce((select succeeded from logged), false);
$function$;

revoke all on function dami.task_board_reopen_for_child(uuid, uuid, text, text, text, timestamptz)
    from public;
grant execute on function dami.task_board_reopen_for_child(uuid, uuid, text, text, text, timestamptz)
    to dami_app;
