-- Collaborative feature plans for humans and agents. A subtask is a task row whose
-- parent_task_id points at another row on the same board; there is no second subtype.

create table dami.task_boards (
    board_id        uuid        primary key,
    title           text        not null,
    feature_request text        not null,
    plan            text        not null,
    root_ordering   text        not null,
    planner_kind    text            null,
    privacy_class   text            null,
    execution_origin text           null,
    created_by_id   text        not null,
    created_by_kind text        not null,
    created_at      timestamptz not null,
    updated_at      timestamptz not null,

    constraint task_boards_title_present check (length(btrim(title)) > 0),
    constraint task_boards_request_present check (length(btrim(feature_request)) > 0),
    constraint task_boards_plan_present check (length(btrim(plan)) > 0),
    constraint task_boards_ordering_known check (root_ordering in ('Ordered', 'Priority')),
    constraint task_boards_planner_known check (
        planner_kind is null or planner_kind in ('Local', 'Frontier', 'Dami')
    ),
    constraint task_boards_privacy_known check (
        privacy_class is null or privacy_class in ('LocalOnly', 'Egressable')
    ),
    constraint task_boards_origin_known check (
        execution_origin is null
        or execution_origin in ('UserTurn', 'ScheduledService', 'ReactiveTrigger', 'SelfAudit')
    ),
    constraint task_boards_planning_context_consistent check (
        (planner_kind is null and privacy_class is null and execution_origin is null)
        or (planner_kind is not null and privacy_class is not null and execution_origin is not null)
    ),
    constraint task_boards_actor_kind_known check (created_by_kind in ('Human', 'Agent')),
    constraint task_boards_time_order check (updated_at >= created_at)
);

create table dami.task_board_tasks (
    task_id          uuid        primary key,
    board_id         uuid        not null references dami.task_boards (board_id) on delete cascade,
    parent_task_id   uuid            null,
    title            text        not null,
    description      text        not null,
    status           text        not null default 'Open',
    priority         smallint    not null,
    position         integer     not null,
    subtask_ordering text        not null,
    claimed_by_id    text            null,
    claimed_by_kind  text            null,
    claimed_at       timestamptz     null,
    version          bigint      not null default 1,
    created_at       timestamptz not null,
    updated_at       timestamptz not null,

    constraint task_board_tasks_board_identity unique (board_id, task_id),
    constraint task_board_tasks_parent_fk foreign key (board_id, parent_task_id)
        references dami.task_board_tasks (board_id, task_id) on delete cascade,
    constraint task_board_tasks_not_own_parent check (parent_task_id is distinct from task_id),
    constraint task_board_tasks_title_present check (length(btrim(title)) > 0),
    constraint task_board_tasks_status_known check (
        status in ('Open', 'InProgress', 'Blocked', 'Done', 'Cancelled')
    ),
    constraint task_board_tasks_priority_known check (priority between 0 and 3),
    constraint task_board_tasks_position_nonnegative check (position >= 0),
    constraint task_board_tasks_ordering_known check (subtask_ordering in ('Ordered', 'Priority')),
    constraint task_board_tasks_claim_consistent check (
        (claimed_by_id is null and claimed_by_kind is null and claimed_at is null)
        or (claimed_by_id is not null and claimed_by_kind in ('Human', 'Agent') and claimed_at is not null)
    ),
    constraint task_board_tasks_version_positive check (version > 0),
    constraint task_board_tasks_time_order check (updated_at >= created_at)
);

create index task_board_tasks_children
    on dami.task_board_tasks (board_id, parent_task_id, position, task_id);
create index task_board_tasks_priority
    on dami.task_board_tasks (board_id, parent_task_id, priority desc, position, task_id);

create table dami.task_acceptance_criteria (
    criterion_id      uuid        primary key,
    task_id           uuid        not null references dami.task_board_tasks (task_id) on delete cascade,
    description       text        not null,
    position          integer     not null,
    is_satisfied      boolean     not null default false,
    satisfied_by_id   text            null,
    satisfied_by_kind text            null,
    satisfied_at      timestamptz     null,

    constraint task_acceptance_description_present check (length(btrim(description)) > 0),
    constraint task_acceptance_position_nonnegative check (position >= 0),
    constraint task_acceptance_result_consistent check (
        (not is_satisfied and satisfied_by_id is null and satisfied_by_kind is null and satisfied_at is null)
        or (is_satisfied and satisfied_by_id is not null
            and satisfied_by_kind in ('Human', 'Agent') and satisfied_at is not null)
    ),
    constraint task_acceptance_position_unique unique (task_id, position)
);

create table dami.task_prerequisites (
    board_id             uuid not null,
    task_id              uuid not null,
    prerequisite_task_id uuid not null,

    primary key (task_id, prerequisite_task_id),
    constraint task_prerequisites_task_fk foreign key (board_id, task_id)
        references dami.task_board_tasks (board_id, task_id) on delete cascade,
    constraint task_prerequisites_dependency_fk foreign key (board_id, prerequisite_task_id)
        references dami.task_board_tasks (board_id, task_id) on delete cascade,
    constraint task_prerequisites_not_self check (task_id <> prerequisite_task_id)
);

create index task_prerequisites_reverse
    on dami.task_prerequisites (prerequisite_task_id, task_id);

create table dami.task_board_activity (
    sequence     bigint generated always as identity primary key,
    event_id     uuid        not null unique,
    board_id     uuid        not null references dami.task_boards (board_id),
    task_id      uuid            null references dami.task_board_tasks (task_id),
    criterion_id uuid            null references dami.task_acceptance_criteria (criterion_id),
    kind         text        not null,
    actor_id     text        not null,
    actor_kind   text        not null,
    occurred_at  timestamptz not null,
    from_status  text            null,
    to_status    text            null,
    detail       text            null,

    constraint task_board_activity_kind_known check (
        kind in ('BoardCreated', 'TaskClaimed', 'CriterionSatisfied',
                 'CriterionReopened', 'TaskCompleted', 'TaskStatusChanged')
    ),
    constraint task_board_activity_actor_kind_known check (actor_kind in ('Human', 'Agent')),
    constraint task_board_activity_status_known check (
        (from_status is null or from_status in ('Open', 'InProgress', 'Blocked', 'Done', 'Cancelled'))
        and (to_status is null or to_status in ('Open', 'InProgress', 'Blocked', 'Done', 'Cancelled'))
    ),
    constraint task_board_activity_transition_consistent check (
        (kind = 'TaskStatusChanged' and from_status is not null and to_status is not null
            and detail is not null and length(btrim(detail)) > 0)
        or (kind <> 'TaskStatusChanged' and from_status is null and to_status is null)
    )
);

create index task_board_activity_board_sequence
    on dami.task_board_activity (board_id, sequence);

create trigger task_board_activity_append_only
    before update or delete on dami.task_board_activity
    for each statement execute function dami.reject_mutation();

create function dami.task_board_try_claim(
    p_event uuid,
    p_task uuid,
    p_version bigint,
    p_actor text,
    p_actor_kind text,
    p_claimed_at timestamptz
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
            (event_id, board_id, task_id, kind, actor_id, actor_kind, occurred_at)
        select p_event, board_id, p_task, 'TaskClaimed', p_actor, p_actor_kind, p_claimed_at
          from changed
        returning true as succeeded
    )
    select coalesce((select succeeded from logged), false);
$function$;

create function dami.task_board_try_set_criterion(
    p_event uuid,
    p_criterion uuid,
    p_version bigint,
    p_satisfied boolean,
    p_actor text,
    p_actor_kind text,
    p_changed_at timestamptz
) returns boolean
language sql
security definer
set search_path = ''
as $function$
    with criterion_task as (
        select task_id
          from dami.task_acceptance_criteria
         where criterion_id = p_criterion
    ), versioned as (
        update dami.task_board_tasks task
           set version = version + 1, updated_at = p_changed_at
          from criterion_task candidate
         where task.task_id = candidate.task_id and task.version = p_version
           and task.status not in ('Done', 'Cancelled')
        returning task.task_id
    ), changed as (
        update dami.task_acceptance_criteria criterion
           set is_satisfied = p_satisfied,
               satisfied_by_id = case when p_satisfied then p_actor else null end,
               satisfied_by_kind = case when p_satisfied then p_actor_kind else null end,
               satisfied_at = case when p_satisfied then p_changed_at else null end
          from versioned task
         where criterion.criterion_id = p_criterion
           and criterion.task_id = task.task_id
        returning criterion.task_id
    ), logged as (
        insert into dami.task_board_activity
            (event_id, board_id, task_id, criterion_id, kind,
             actor_id, actor_kind, occurred_at)
        select p_event, task.board_id, changed.task_id, p_criterion,
               case when p_satisfied then 'CriterionSatisfied' else 'CriterionReopened' end,
               p_actor, p_actor_kind, p_changed_at
          from changed
          join dami.task_board_tasks task on task.task_id = changed.task_id
        returning true as succeeded
    )
    select coalesce((select succeeded from logged), false);
$function$;

create function dami.task_board_try_complete(
    p_event uuid,
    p_task uuid,
    p_version bigint,
    p_actor text,
    p_actor_kind text,
    p_completed_at timestamptz
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
            (event_id, board_id, task_id, kind, actor_id, actor_kind, occurred_at)
        select p_event, board_id, p_task, 'TaskCompleted', p_actor, p_actor_kind, p_completed_at
          from changed
        returning true as succeeded
    )
    select coalesce((select succeeded from logged), false);
$function$;

create function dami.task_board_try_set_status(
    p_event uuid,
    p_task uuid,
    p_version bigint,
    p_next text,
    p_actor text,
    p_actor_kind text,
    p_detail text,
    p_changed_at timestamptz
) returns boolean
language sql
security definer
set search_path = ''
as $function$
    with eligible as (
        select task_id, board_id, status
          from dami.task_board_tasks
         where task_id = p_task and version = p_version
           and (
               (status = 'Open' and p_next in ('Blocked', 'Cancelled'))
               or (status = 'InProgress' and p_next in ('Blocked', 'Cancelled')
                   and claimed_by_id = p_actor and claimed_by_kind = p_actor_kind)
               or (status = 'Blocked' and p_next in ('Open', 'Cancelled'))
           )
         for update
    ), changed as (
        update dami.task_board_tasks task
           set status = p_next,
               claimed_by_id = case when p_next = 'Open' then null else claimed_by_id end,
               claimed_by_kind = case when p_next = 'Open' then null else claimed_by_kind end,
               claimed_at = case when p_next = 'Open' then null else claimed_at end,
               updated_at = p_changed_at,
               version = version + 1
          from eligible
         where task.task_id = eligible.task_id
        returning eligible.board_id, eligible.status
    ), logged as (
        insert into dami.task_board_activity
            (event_id, board_id, task_id, kind, actor_id, actor_kind, occurred_at,
             from_status, to_status, detail)
        select p_event, board_id, p_task, 'TaskStatusChanged', p_actor, p_actor_kind,
               p_changed_at, status, p_next, p_detail
          from changed
        returning true as succeeded
    )
    select coalesce((select succeeded from logged), false);
$function$;

grant select, insert on dami.task_boards to dami_app;
grant select, insert on dami.task_board_tasks to dami_app;
grant select, insert on dami.task_acceptance_criteria to dami_app;
grant select, insert on dami.task_prerequisites to dami_app;
grant select, insert on dami.task_board_activity to dami_app;
grant usage, select on sequence dami.task_board_activity_sequence_seq to dami_app;

revoke all on function dami.task_board_try_claim(uuid, uuid, bigint, text, text, timestamptz)
    from public;
revoke all on function dami.task_board_try_set_criterion(
    uuid, uuid, bigint, boolean, text, text, timestamptz) from public;
revoke all on function dami.task_board_try_complete(uuid, uuid, bigint, text, text, timestamptz)
    from public;
revoke all on function dami.task_board_try_set_status(
    uuid, uuid, bigint, text, text, text, text, timestamptz) from public;

grant execute on function dami.task_board_try_claim(uuid, uuid, bigint, text, text, timestamptz)
    to dami_app;
grant execute on function dami.task_board_try_set_criterion(
    uuid, uuid, bigint, boolean, text, text, timestamptz) to dami_app;
grant execute on function dami.task_board_try_complete(uuid, uuid, bigint, text, text, timestamptz)
    to dami_app;
grant execute on function dami.task_board_try_set_status(
    uuid, uuid, bigint, text, text, text, text, timestamptz) to dami_app;
