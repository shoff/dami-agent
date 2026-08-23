-- Found by testing 002/003 rather than by reading them: a BEFORE ... FOR EACH ROW
-- trigger does not fire on TRUNCATE, so the append-only guarantee had a hole. The
-- runtime role cannot exploit it - TRUNCATE is an owner privilege and dami_app is not
-- the owner - but the owner could have emptied the event store without the guard firing,
-- which is exactly the audit property the store exists to provide.
--
-- Added as a new file rather than by editing 002/003, because those are already applied
-- and the runner's checksum guard would flag the edit as a divergence between the
-- repository and the database.

create trigger execution_events_no_truncate
    before truncate on dami.execution_events
    for each statement execute function dami.reject_mutation();

create trigger observations_no_truncate
    before truncate on dami.observations
    for each statement execute function dami.reject_mutation();
