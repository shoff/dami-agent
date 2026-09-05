-- 038 — the run log accepts every cadence the runtime can declare.
--
-- 035 enumerated the cadences that existed that day. ProactiveCadence.EightHourly was
-- added for the daily portrait (ADR-0027) without widening this check, so the first
-- enabled portrait pass drew its picture, then failed recording the run with SQLSTATE
-- 23514 — and in the hosted tier that exception stops the process (Handoff.md; N10 only
-- covered the disabled case). Proven on 2026-09-04: the PNG landed, the run row did not,
-- and `--run daily-portrait` exited 134.
--
-- Widened rather than dropped: a misspelled cadence is still worth refusing. The
-- persistence suite now records one run per enum value, so the next cadence cannot be
-- added without this list following it.

alter table dami.proactive_runs
    drop constraint proactive_runs_cadence_known;

alter table dami.proactive_runs
    add constraint proactive_runs_cadence_known check (
        cadence is null or cadence in ('Nightly', 'Weekly', 'Quarterly', 'EightHourly')
    );
