-- 050 — Steve's INR readings and warfarin dose changes, as he reports them (ADR-0037).
--
-- Structured, unlike health_events (prose extracted from notes): a reading is a number on a
-- day, a dose is the clinic's instruction verbatim. Local only; never sent to a model.

create table dami.anticoag_log (
    entry_id    uuid          primary key,
    kind        text          not null,
    on_day      date          not null,
    inr         numeric(3,1)      null,
    dose        text              null,
    recorded_at timestamptz   not null,

    constraint anticoag_log_kind check (kind in ('inr', 'dose')),
    constraint anticoag_log_inr_shape check ((kind = 'inr') = (inr is not null)),
    constraint anticoag_log_inr_plausible check (inr is null or inr between 0.5 and 10.0),
    constraint anticoag_log_dose_shape check ((kind = 'dose') = (dose is not null))
);

create index anticoag_log_by_day on dami.anticoag_log (kind, on_day desc);

grant select, insert on dami.anticoag_log to dami_app;
