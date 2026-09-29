-- 046 — the emails Steve forwarded to Dami's own mailbox, as filed (docs/agent-landscape-2026-09.md D2).
--
-- Only the local model's structured reading is kept — kind and a one-sentence summary —
-- never the body: mail is untrusted input. One row per Message-ID, so a message is
-- filed once however many passes see it.

create table dami.mail_items (
    message_id  text        primary key,
    received_at timestamptz not null,
    kind        text        not null,
    summary     text        not null,
    filed_at    timestamptz not null
);

create index mail_items_by_received on dami.mail_items (received_at desc);

grant select, insert on dami.mail_items to dami_app;
