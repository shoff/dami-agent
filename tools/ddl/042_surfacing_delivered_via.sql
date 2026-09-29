-- 042 — a surfacing knows whether Dami pushed it or Steve came to it.
--
-- ADR-0014 as amended 2026-09-29: once a day the top pending surfacing is pushed to
-- Steve's Discord DM. A reaction to something pushed may be rating the interruption
-- rather than the find, so the threshold tuner (H8) reads only reactions to surfacings
-- he came to. Null is every delivery before this column and every pulled one since:
-- `dami inbox`, the web view, or riding his next Discord message. `discord-dm` is the
-- daily check-in.

alter table dami.surfacings
    add column delivered_via text;

comment on column dami.surfacings.delivered_via is
    'How a delivered surfacing reached Steve when Dami pushed it (discord-dm); null when he came to it. H8 tunes only on the nulls.';
