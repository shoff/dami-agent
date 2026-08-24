-- Durable conversation boundaries. Turns join these rows in the same migration before
-- G4a is applied live; keeping the parent table explicit prevents messages from
-- becoming a second, implicit session source of truth.

create table dami.conversation_sessions (
    session_id uuid        primary key,
    state      text        not null,
    created_at timestamptz not null,
    updated_at timestamptz not null,

    constraint conversation_sessions_state_known check (
        state in ('Active', 'Interrupted')
    ),
    constraint conversation_sessions_time_order check (updated_at >= created_at)
);

create index conversation_sessions_recent
    on dami.conversation_sessions (updated_at desc, session_id);

grant select, insert on dami.conversation_sessions to dami_app;
grant update (state, updated_at) on dami.conversation_sessions to dami_app;

create table dami.conversation_turns (
    sequence          bigint generated always as identity primary key,
    session_id        uuid        not null references dami.conversation_sessions (session_id),
    request_id        uuid        not null,
    trace_id          uuid        not null unique,
    user_message      text        not null,
    assistant_message text            null,
    state             text        not null,
    requested_at      timestamptz not null,
    completed_at      timestamptz     null,

    constraint conversation_turns_request_unique unique (session_id, request_id),
    constraint conversation_turns_state_known check (
        state in ('Running', 'Completed', 'Interrupted', 'Failed')
    ),
    constraint conversation_turns_terminal_consistent check (
        (state = 'Running' and assistant_message is null and completed_at is null)
        or (state = 'Completed' and assistant_message is not null and completed_at is not null)
        or (state in ('Interrupted', 'Failed') and assistant_message is null and completed_at is not null)
    ),
    constraint conversation_turns_time_order check (
        completed_at is null or completed_at >= requested_at
    )
);

create index conversation_turns_recent
    on dami.conversation_turns (session_id, sequence desc);

grant select, insert on dami.conversation_turns to dami_app;
grant update (assistant_message, state, completed_at) on dami.conversation_turns to dami_app;
grant usage, select on sequence dami.conversation_turns_sequence_seq to dami_app;
