-- 043 — what Steve spent, from receipts first (docs/agent-landscape-2026-09.md D4).
--
-- Receipt photo → ledger was the most repeated durable finance habit in the 2026-09-29
-- survey of personal agents. Its own table rather than domain_facts: amounts are summed,
-- and a sentence cannot be. A bank CSV import (D5) lands here too, with its own source.
-- The same merchant, total and day is the same expense: a receipt photographed twice is
-- recorded once.

create table dami.expenses (
    expense_id  uuid          primary key,
    spent_on    date          not null,
    merchant    text          not null,
    total       numeric(12,2) not null,
    currency    text          not null,
    category    text          not null,
    source      text          not null,
    recorded_at timestamptz   not null,

    constraint expenses_merchant_present check (length(btrim(merchant)) > 0),
    constraint expenses_total_not_negative check (total >= 0),
    constraint expenses_once unique (spent_on, merchant, total)
);

create index expenses_by_day on dami.expenses (spent_on desc);

comment on table dami.expenses is
    'What Steve spent. Local only: read from receipt photos by the local vision model; never sent to a frontier model.';

grant select, insert on dami.expenses to dami_app;
