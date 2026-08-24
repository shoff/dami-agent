-- Bounds crash-recovery scans to skill terminal events instead of the full event stream.

create index execution_events_skill_outcomes
    on dami.execution_events (payload_reference)
    where type in ('SkillChanged', 'SkillChangeFailed')
      and payload_reference is not null;
