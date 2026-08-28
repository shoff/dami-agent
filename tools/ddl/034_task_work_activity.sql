-- 034 — the board records when an agent was asked to work a task, and what came back.
--
-- Until now the board recorded only decisions a hand made: claim, complete, block,
-- criterion. "Work this task now" makes the runtime itself an actor on the board, so the
-- ledger needs the two events that bracket a run. They are advisory by construction — a
-- run produces a proposal and a trace, and never moves the task to Done. That gate stays
-- where 028 put it: every criterion satisfied, every child finished, every prerequisite
-- done, asserted by a hand.
--
-- TaskWorkFinished carries the trace id in `detail`, which is the whole point: the answer
-- is replayable from the execution event stream rather than summarised here.

alter table dami.task_board_activity
    drop constraint task_board_activity_kind_known;

alter table dami.task_board_activity
    add constraint task_board_activity_kind_known check (
        kind in ('BoardCreated', 'TaskAdded', 'TaskClaimed', 'CriterionAdded',
                 'CriterionSatisfied', 'CriterionReopened', 'TaskCompleted',
                 'TaskStatusChanged', 'TaskWorkStarted', 'TaskWorkFinished')
    );

-- Logging a work event is not a task mutation: it does not touch status, claim, or
-- version, so it takes no expected version and cannot conflict. It exists as a function
-- rather than a bare insert so the board_id is resolved from the task inside the same
-- statement, and so a run against a task that has since been deleted writes nothing
-- instead of a dangling row.
create function dami.task_board_log_work(
    p_event uuid,
    p_task uuid,
    p_kind text,
    p_actor text,
    p_actor_kind text,
    p_detail text,
    p_at timestamptz
) returns boolean
language sql
security definer
set search_path = ''
as $function$
    with logged as (
        insert into dami.task_board_activity
            (event_id, board_id, task_id, kind, actor_id, actor_kind, occurred_at, detail)
        select p_event, task.board_id, task.task_id, p_kind, p_actor, p_actor_kind,
               p_at, p_detail
          from dami.task_board_tasks task
         where task.task_id = p_task
           and p_kind in ('TaskWorkStarted', 'TaskWorkFinished')
        returning true as succeeded
    )
    select coalesce((select succeeded from logged), false);
$function$;

revoke all on function dami.task_board_log_work(uuid, uuid, text, text, text, text, timestamptz)
    from public;
grant execute on function dami.task_board_log_work(uuid, uuid, text, text, text, text, timestamptz)
    to dami_app;
