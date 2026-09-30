-- 048 — what Steve ate, from meal photos, estimated locally (docs/agent-landscape-2026-09.md D6).
--
-- Calorie and protein logging by photo was one of the most repeated uses in the 2026-09-29
-- survey. Estimates by the local vision model, labelled as such; health data, so it never
-- reaches a frontier model and its confirmations go only to Steve's DM (ADR-0025).

create table dami.meals (
    meal_id     uuid        primary key,
    eaten_at    timestamptz not null,
    description text        not null,
    calories    integer     not null,
    protein_g   integer     not null,
    recorded_at timestamptz not null,

    constraint meals_calories_plausible check (calories between 0 and 5000),
    constraint meals_protein_plausible check (protein_g between 0 and 400)
);

create index meals_by_time on dami.meals (eaten_at desc);

grant select, insert on dami.meals to dami_app;
