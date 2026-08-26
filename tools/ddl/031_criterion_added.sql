-- 031 — an acceptance criterion can be added to a task that already exists.
--
-- Criteria arrived only with the task's draft, so most imported tasks have none and the
-- completion gate has nothing to check (O2e). Adding one changes the gate, so it bumps
-- the task's version like every other mutation, is refused on finished work, and is
-- recorded as CriterionAdded.

alter table dami.task_board_activity
    drop constraint task_board_activity_kind_known;

alter table dami.task_board_activity
    add constraint task_board_activity_kind_known check (
        kind in ('BoardCreated', 'TaskAdded', 'TaskClaimed', 'CriterionAdded', 'CriterionSatisfied',
                 'CriterionReopened', 'TaskCompleted', 'TaskStatusChanged')
    );

create function dami.task_board_try_add_criterion(
    p_event uuid,
    p_criterion uuid,
    p_task uuid,
    p_version bigint,
    p_description text,
    p_actor text,
    p_actor_kind text,
    p_added_at timestamptz
) returns boolean
language sql
security definer
set search_path = ''
as $function$
    with changed as (
        update dami.task_board_tasks
           set updated_at = p_added_at, version = version + 1
         where task_id = p_task and version = p_version
           and status not in ('Done', 'Cancelled')
           and not exists (select 1 from dami.task_acceptance_criteria where criterion_id = p_criterion)
        returning board_id
    ), inserted as (
        insert into dami.task_acceptance_criteria (criterion_id, task_id, description, position)
        select p_criterion, p_task, p_description,
               coalesce((select max(position) + 1 from dami.task_acceptance_criteria where task_id = p_task), 0)
          from changed
        returning task_id
    ), logged as (
        insert into dami.task_board_activity
            (event_id, board_id, task_id, criterion_id, kind, actor_id, actor_kind, occurred_at, detail)
        select p_event, changed.board_id, p_task, p_criterion, 'CriterionAdded', p_actor, p_actor_kind,
               p_added_at, p_description
          from changed join inserted on true
        returning true as succeeded
    )
    select coalesce((select succeeded from logged), false);
$function$;

revoke all on function dami.task_board_try_add_criterion(uuid, uuid, uuid, bigint, text, text, text, timestamptz)
    from public;
grant execute on function dami.task_board_try_add_criterion(uuid, uuid, uuid, bigint, text, text, text, timestamptz)
    to dami_app;
