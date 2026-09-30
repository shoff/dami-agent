-- 051 — a meal's vitamin-K class, for the anticoagulation co-pilot (ADR-0037 slice 2).
--
-- Warfarin control depends on vitamin-K intake being steady, not low; the class lets the
-- weekly note notice when the pattern changes. Estimated by the local vision model.

alter table dami.meals
    add column vitamin_k text;

alter table dami.meals
    add constraint meals_vitamin_k_known check (vitamin_k is null or vitamin_k in ('low', 'moderate', 'high'));
