# ADR 0019 — The frontier thinks; the local model fetches, judges, and disguises

- **Decision:** Invert the model relationship. The ChatGPT subscription answers; the local sidecar does retrieval and the mundane work that feeds it. Retrieved context **is** sent — withholding it made the frontier useless — but every item passes a local three-way disclosure gate first: **pass**, **disguise**, or **withhold**.
- **Date:** 2026-08-24
- **Status:** accepted (Steve, from the MAI pattern)
- **Supersedes:** the posture, not the mechanism, of ADR-0013. C4's per-turn approval remains for `dami brief`; this is the standing-consent path.

## What was wrong

Dami had the relationship backwards. `qwen3:8b` answered every conversational turn and
the frontier was a side door that carried *no memory at all* — which made it useless for
anything about Steve, so it went unused. Meanwhile the local model, which is genuinely
good at mundane structured work, was being asked to do the thinking.

MAI's pattern is the correct one: **the local model issues RAG lookups and mundane tasks
to augment the data sent to the frontier.** The sidecar is infrastructure, not the brain.

## What was built

`AugmentedFrontierTurn`: `ContextBuilder` retrieves locally (embed → ANN → rerank →
recency and grounding gates, all on this host), the result goes through the disclosure
gate, and the frontier answers on what survives. Fully traced, and the exact bytes are
stored hash-pinned so what left is auditable afterwards rather than merely promised.

## The disclosure gate, and why three options

Blanket redaction was my first instinct and it was wrong: it degrades every item whether
or not it needs it. Blanket sending ignores D-012. The useful distinction is per item,
and it needs a third answer, because **a fact is often needed while the identity attached
to it is not**:

- **pass** — nothing identifying; send as-is.
- **disguise** — the clinical or technical substance is needed, the identity is not.
  Rewritten as an unnamed third party: *"a friend has…"*, *"a patient asked…"*.
- **withhold** — too personal, and not needed for this question.

It runs on the local sidecar against rules Steve owns, and **fails closed**: unparseable
output withholds everything, an item the model forgot to classify stays home, and a
"disguise" that arrives without a rewrite is treated as a refusal. A gate that fails open
is worse than no gate, because it looks like protection. Seven tests pin exactly these.

## Learning his boundaries

`DisclosureOptions.Examples` feeds Steve's past corrections back into the prompt. The
gate is meant to get better at *his* boundaries, not at boundaries in general — the same
shape as the taste model learning from `good`/`bad`. Capturing corrections through the
CLI and GUI is the next step and is not built yet.

## Evidence

Live, on the real corpus: *"given my heart condition, what should I ask the surgeon?"* →
8 items retrieved locally → **5 sent, 1 disguised, 2 withheld** → a specific, correct
answer naming severe aortic stenosis, mechanical versus tissue valve, and the questions
worth asking. The stored artefact shows the disguised item went as *"A patient asked… a
provider answered…"*.

## What remains open

The corpus is largely written in the third person already ("the user…"), so much of it is
de-identified by luck rather than design; the gate should not be credited for that.
Turning the gate off (`AugmentedTurn:Gate=false`) sends everything retrieved — available,
deliberately not the default, and Steve's decision alone.
