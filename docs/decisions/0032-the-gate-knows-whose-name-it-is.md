# ADR 0032 — The disclosure gate knows whose name it is

- **Decision:** The gate's instructions now state that the frontier already knows it is
  talking to Steve and knows his first name; the name alone never makes an item
  identifying. Conversation-history items ("Earlier — …") pass unless they name another
  person or carry specific health, financial, or address details. Disguise is preferred
  over withhold whenever a fact bears on the question. The gate still fails closed, still
  records every decision, and Steve's corrections still override it.
- **Date:** 2026-09-05
- **Status:** accepted
- **Extends:** ADR-0013/0019 (local augmentation and disclosure), ADR-0026 (Discord on the frontier)

## Context

A day of Discord turns measured from the disclosure ledger (2026-09-04 21:00 to
2026-09-05 12:30): 67 items judged, **44 withheld, 9 disguised, 14 passed**. The most
frequent reason was *"Steve is a personal identifier and should be withheld"*, attached to
lines such as *"Earlier — Steve: Well where's the image?"* — the conversation history the
gateway sends so a message is not turn one again (ADR-0026). Withholding those is how she
answered "where's the image?" with "I don't see an image": the exchange had been judged
too personal to leave the host, so the frontier never saw it. The frontier prompt names
Steve in its identity text on every turn; withholding his name protects nothing.

The other large group — *"Contains personal details about Steve"* on a gym routine, a
monitoring directive — is the gate choosing withhold where its own definition says
disguise: the fact bore on the question, the identity did not.

## Alternatives considered

| Option | Strengths | Weaknesses | Why not chosen |
|---|---|---|---|
| Leave it; let corrections in the ledger teach the gate (G9a) | Steve stays in control item by item | 44 corrections a day for a systematic misreading; the history lines recur every turn | Corrections are for boundaries, not for a rule the prompt got wrong |
| Send history around the gate | Restores the thread | ADR-0026 gated history on purpose: "what Dami said last message" can be profile-derived | Keeps the design, fixes the reading |
| Drop the name from history labels | Fewer false positives | The content would still be judged with the same rule; and the frontier is told his name anyway | Treats the symptom |
| Tell the gate what the service already knows (chosen) | One instruction, measurable, reversible | A local 8B model may still over-withhold; this narrows, not removes, that | Smallest true fix |

## Evidence

- `dami.disclosure_decisions` since 2026-09-04 21:00: Withhold 44, Pass 14, Disguise 9;
  reasons and samples in `work-log.md` for the day.
- Turn latency in the same window: two streamed frontier turns, 16 s and 5 s. Too few to
  say where the seconds go; the instrumentation is there and the next measurement should
  be a full week.
- `The_Prompt_Should_Say_The_Name_Is_Known_And_History_Is_This_Chat` pins the three
  sentences; the fail-closed tests are unchanged.

## Consequences

- Nothing new can leave: pass, disguise, withhold are the same three actions with the
  same ledger and the same corrections. What changes is that the user's first name and
  his own chat are no longer treated as secrets from a service that already has both.
- Expect the withheld share to fall sharply and the disguised share to rise. Re-measure
  after a day; if health facts start passing undisguised, that is a correction in the
  ledger and a rule in `Disclosure:Rules`, not a revert.

## Reversal path

Restore the three-line instruction; the test pins the new wording and would need
deleting deliberately.
