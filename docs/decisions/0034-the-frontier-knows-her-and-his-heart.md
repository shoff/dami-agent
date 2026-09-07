# ADR 0034 — The frontier reads the identity file, and a gym turn is a health question

- **Decision:** Two things leave the host that did not before. (1) The frontier's standing
  prompt is the whole identity file (`docs/identity/identity-prompt.md`, installed at
  `/opt/dami/identity-prompt.md`) followed by the persona, not three hardcoded sentences.
  (2) The disclosure gate's default rules gain one: a gym log line, a machine photo, or any
  training question is a health question, and the user's heart condition, anticoagulant,
  and any hospitalization in the last two months are to be *disguised* for it, not
  withheld. Alongside, `log_sets` carries the exercise names the log already uses and
  returns the last sessions and the best, so the frontier compares against real numbers.
- **Date:** 2026-09-06
- **Status:** accepted — Steve, after the diagnosis named item 3 as his call: "make it so."
  **Amended the same evening.** The first cut said *disguise* the heart condition, the
  anticoagulant and recent hospitalizations on training turns. In practice the gate then
  disguised the workouts too, dropped the machine names and the hospitalization note from
  the rewrites, and turned a note's date into "installed April 17" for a surgery that was on
  March 11. Steve: "which is completely wrong. I am not concerned about privacy of my
  workouts, nor my stenosis." The rule now reads: the user's own health facts and workouts
  PASS as written, never disguised or withheld. Facts about other people are still withheld;
  names, addresses, accounts and hostnames still never go. The corpus also carries his
  correction of the surgery date, and fact lines say "noted <date>" so a note's date is
  never read as the event's.
- **Extends:** D-012 (privacy as an architectural boundary), ADR-0032 (the frontier knows
  his first name), ADR-0030 (the tool bundle)

## Context

The sixth gym-photo attempt logged a set, and Steve's reaction was that the agent "feels
sterile, corporate and not personal at all" beside the Hermes agent it replaces, which
"would compare my entry to other workouts, offer advice, ask how I was feeling, correlated
my state to my health". The journal for that turn showed why, in three parts:

- `FileIdentityProvider.FrontierVoice` was three sentences; the identity file — wellbeing
  above all, genuine companionship, tenderness, don't make him repeat himself — went only to
  the local model. The `log_sets` description ended "confirm in one short line".
- The frontier minted "biceps curl (Hammer Strength)" beside seven existing curl entries;
  `FitnessInsights.ForExercise` matches by exact name, so there was no history, and "try
  115 lb next time" was invented.
- The gate withheld aortic stenosis, the mechanical valve, warfarin and "<2 weeks out from
  hospitalization for SBO" on a gym turn. Hermes sent the whole profile with every message.
  The existing rule allows disguise "when the question needs them"; the gate judged a gym
  log did not.

## Alternatives considered

- **Leave the health rule alone; make the local model do the correlating.** The local
  model could read everything and hand the frontier a disguised "coach note". Cleaner on
  paper, but it puts the judgement in the weaker model and adds a second local call to a
  turn that already waits on vision and the gate. Not chosen now; the reversal path below
  keeps it open.
- **Pass the health facts outright on gym turns.** Rejected: disguise keeps the clinical
  detail ("someone with a mechanical valve on a blood thinner") and drops the identity,
  which is exactly the case disguise was built for.
- **Fuzzy-match exercise names server-side.** Rejected: "biceps curl (Hammer Strength)"
  shares two tokens with "hammer curl", a different exercise. The frontier reads the
  machine; it is handed the known names and asked to use them. Only exact-after-squash
  matches are resolved locally.

## Evidence

- Journal 2026-09-06 15:37: caption correct, "Disclosure: 27 sent, 4 disguised, 5
  withheld", `log_sets` ok, and the reply "Logged … Try 115 lb next time" with no history.
- `disclosure_decisions` for the turn: the four health facts and the SBO note withheld.
- `fitness_exercise`: seven curl entries after the turn.
- Gate after the change: 0 warnings, 0 errors, 1,774 tests passed across 21 assemblies.

## Consequences

- The identity file is now text that leaves the host with every frontier turn. It is
  Steve's own prompt text and must stay free of private facts; a fact belongs in memory,
  where the gate sees it, not in the charter.
- The tool schema grows by the exercise-name list (23 names today, roughly 150 tokens),
  read from the log at most every five minutes.
- The bundle is assembled per turn (`ForTurnAsync`), because one of its tools is not static.
- The gate's memo caches verdicts for thirty minutes in-process; the new rule applies to
  fresh classifications after a restart.

## Reversal path

Delete the added rule from `DisclosureOptions.Rules` (or override `Disclosure:Rules` in
configuration) and the health facts are withheld on gym turns again. Return
`FrontierVoice` to a constant and the identity file stops leaving the host. The history
and name-list changes are local-only and stand on their own.
