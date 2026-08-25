-- An augmented frontier turn egresses under standing consent rather than a per-turn
-- approval, but it must still leave the same audit artefact: the exact bytes that were
-- sent, hash-pinned. The approval link therefore becomes optional — absent means
-- "standing consent", not "unrecorded".

alter table dami.egress_briefs alter column approval_id drop not null;
