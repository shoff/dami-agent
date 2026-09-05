-- 039 — a scheduled job knows where its result goes.
--
-- Jobs created by the frontier's `schedule` tool (ADR-0030) come from a conversation in
-- a channel, and "send me a picture every morning" means *here*. Null keeps the G16
-- behaviour: the answer is surfaced to the inbox. `discord:<channelId>` delivers it as a
-- Discord turn with the same tool bundle.

alter table dami.scheduled_jobs
    add column delivery text;
