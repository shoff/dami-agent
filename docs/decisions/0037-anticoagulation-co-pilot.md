# ADR 0037 — The anticoagulation co-pilot notices and reminds; it never doses

- **Decision:** Dami keeps a local record of Steve's INR readings and warfarin dose changes, notices what is known to move INR (vitamin-K swings, interacting drugs and supplements, overdue checks), and tells him in his own DM — always as "noticed", never as advice on dose, and always pointing decisions to his anticoagulation clinic.
- **Date:** 2026-09-29
- **Status:** accepted in scope by Steve on 2026-09-29 (chose the feature; target 2.0–3.0; clinic/lab draws; log doses). The safety rules below are written to be read by him before slice 2.
- **Supersedes:** none. Extends H22 (INR cadence) and D6 (meal photos).

## Context

Steve's corpus records a mechanical aortic valve (surgery 2026-03-11) and lifelong
warfarin; an April 2026 episode in which rifampin and linezolid, given for a suspected
prosthetic-valve infection, destabilised his INR and checks went to twice weekly; and that
he quit drinking because of warfarin and alcohol. `health_events` holds 41 medication rows,
22 vital rows and 23 appointment rows — but **not one INR value, no target range and no
dose**. H22 (`inr-cadence`) has been quiet since 2026-09-16 for exactly that reason.

Warfarin control is moved by things Dami is now positioned to see: what he eats (D6 meal
photos), what he is prescribed or buys (what he tells Dami, forwarded mail, receipts), his
calendar (travel, appointments), and his check rhythm. This is the charter's success
sentence in its purest form — "something he did not ask for, did not know, and is glad to
have heard" — on data that must never leave this host. It was chosen by Steve on
2026-09-29 over a morning brief and whole-life recall (docs/agent-landscape-2026-09.md).

## The rules (what makes this safe to build)

1. **Never dosing.** Dami never proposes, computes or implies a warfarin dose or a change
   to one. A logged dose is Steve's clinic's decision, recorded verbatim.
2. **Out of range is the clinic's call.** A reading outside the target range is said with
   "your anticoagulation clinic decides any change — call them if they have not seen this
   result". Above range adds the standard label warning: unusual bleeding, black or bloody
   stools, or a sudden severe headache are urgent.
3. **Interactions are a prompt to ask, not a verdict.** A short, well-established list of
   warfarin interactors (FDA warfarin labelling) is matched in what Steve tells Dami; the
   notice says "known to interact with warfarin — ask whoever prescribed it whether your
   INR should be checked sooner". The list is not exhaustive and says so.
4. **Vitamin K is about consistency.** Steady intake is what keeps INR steady; Dami never
   tells him to avoid vitamin K, only notices when his pattern changed.
5. **Local only, DM only.** Readings, doses and notices never reach a frontier model; the
   fast path runs before the frontier, and replies are ProfileDerived, so the channel
   refuses them anywhere but his DM (ADR-0025).
6. **Configured, not assumed.** The target range comes from `Anticoag__TargetLow` /
   `Anticoag__TargetHigh` (2.0 / 3.0, set by Steve); without them, no in/out-of-range line.

## Slices

1. **Capture and interaction watch** (built 2026-09-29, `7af38c1`): "INR 2.4" and dose messages logged
   locally with an immediate DM (range, trend, days since last check); interactor mentions
   noticed.
2. **Vitamin-K consistency and the weekly note** (built 2026-09-29, `1cbc9a6` and the next commit): meal photos gain a vitamin-K class; a
   Monday DM with the week's pattern against his usual, the last reading, and whether a
   check looks overdue from his own interval.
3. **Joining the rest:** calendar travel before a check, interactors in forwarded mail and
   receipts, readings on the Health tab.

## Alternatives considered

| Option | Strengths | Weaknesses | Why not chosen |
|---|---|---|---|
| Frontier-answered anticoagulation questions | Fluent | Sends health data off-host; invites advice | Rules 1 and 5 |
| Dose suggestions from INR trend | What some apps do | Clinical decision; liability; wrong is dangerous | Rule 1 |
| Surfacing queue only | Uses existing muse channel | Health surfacings are withheld by the gate on the way to the check-in; a reading reply must be immediate | DM direct, locally rendered |

## Evidence

Corpus query 2026-09-29: 53 observations mention warfarin or INR; `health_events` rows
listed above. H22 status row: "0 valued checks on the timeline". Steve's answers on
2026-09-29: target 2.0–3.0, clinic/lab draws, log doses.

## Consequences

A new local table of readings and doses; a fast path before the frontier for INR and dose
messages; a side notice for interactors that does not stop the normal answer. The
interaction list is a maintained artefact with a source. Health notices go direct to the
DM rather than through the surfacing queue.

## Reversal path

Unset `Anticoag__*` and remove the responder registrations; the table keeps what was
logged. Nothing else depends on it.
