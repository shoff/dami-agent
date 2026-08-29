-- 035 — a run records the cadence it ran on.
--
-- "Has that service run lately?" is unanswerable without knowing how often it is meant
-- to. Four services showed "1 run, 5 days ago" and looked stuck; establishing that they
-- were Weekly and Quarterly, and therefore healthy, meant reading the C# source, because
-- the cadence lives on IProactiveService in the proactive process and the Host — which
-- serves the view — cannot see it.
--
-- Mirroring the mapping in a lookup somewhere would drift the first time a cadence
-- changed. Recording it on the run makes the ledger self-describing: what a pass ran on
-- is a fact about that pass, and a later cadence change does not rewrite history.
--
-- Nullable because history predates the column, and backfilled from the service
-- definitions as they stand at this revision. That backfill is a statement about today,
-- which is why it is written down here rather than inferred at read time.

alter table dami.proactive_runs
    add column cadence text;

alter table dami.proactive_runs
    add constraint proactive_runs_cadence_known check (
        cadence is null or cadence in ('Nightly', 'Weekly', 'Quarterly')
    );

update dami.proactive_runs
   set cadence = case service_name
       when 'interest-scout'    then 'Nightly'
       when 'civic-agenda'      then 'Nightly'
       when 'civic-collector'   then 'Nightly'
       when 'network-collector' then 'Nightly'
       when 'health-collector'  then 'Nightly'
       when 'curator'           then 'Nightly'
       when 'embedder'          then 'Nightly'
       when 'reflection'        then 'Weekly'
       when 'codebase-audit'    then 'Weekly'
       when 'media-librarian'   then 'Weekly'
       when 'pushback-audit'    then 'Quarterly'
   end
 where cadence is null;
