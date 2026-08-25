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

## Planning the retrieval, and why the order matters

`ContextBuilder` used to embed the question once and search on it. That retrieves whatever
sits nearest that phrasing, which for *"given my heart condition, what should I ask the
surgeon"* meant long conversation summaries that merely mention surgery. Two things were
wrong with it, and only one was the one first suspected.

Crude lexical redundancy among the eight retrieved items was **low** — one pair above 0.25
Jaccard — so near-duplicate suppression was not the win. What the measurement did show was
lengths of 55 to 725 characters, and that the health domain's own rows, which are short
dated clinical statements, were never searched at all.

So `LocalQueryPlanner` runs before retrieval, in two passes:

1. **Route and draft** — which domains bear on the question, plus a first set of searches.
2. **Ground and redraft** — the named domains hand over their facts, and the searches are
   rewritten in that vocabulary.

The order is the whole point. Asked cold to expand "my heart condition", the local model
returns *"heart condition treatment options"*, which matches nothing the corpus wrote —
it cannot expand a vague personal reference without knowing the person. Given the health
rows it returns *"severe aortic stenosis"* and *"mechanical AVR surgery"*, which is what
the notes actually say. Each pass costs about a second on qwen3:8b; a question naming no
domain pays for one.

The union of all searches is reranked against the **original** question, never against the
sub-query that found it, so expansion cannot reward drift. Domain facts lead the memories
into the budget: a domain row is a dated clinical statement, a memory is the conversation
that mentioned it, and if the budget runs out it is the prose that should be missing.

Planning **fails open**, unlike the gate below. A gate that cannot parse its answer must
withhold, because the cost of guessing is a privacy breach; a planner that cannot parse
its answer searches the request verbatim, which is what retrieval did before this existed.

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

Retrieval planning, same question, before and after — `GET /context`:

| | facts | memories | leading facts |
|---|---|---|---|
| before | 0 | 8 | *(the domain was never searched)* |
| after | 8 | 8 | Open-heart surgery · Mechanical AVR by Bernard Harrison · Pre-op appointment |

The end-to-end augmented turn then asked, unprompted, *"Given my chronic dizziness and the
recent brief, sharp positional chest pain, do you want any repeat ECG, echocardiogram,
labs…"* — both of those are structured health rows that the pre-change context did not
contain.

Two defects were found by looking at the live output rather than the tests, and both are
worth recording because the tests passed throughout. `DISTINCT ON` requires its `ORDER BY`
to lead with the dedupe key, so deduplicating and limiting in one query returned the
alphabetically first rows: *aortic stenosis, Autism spectrum disorder, average heart rate,
bowel obstruction* — an A-to-B slice of the timeline. And 25 of 84 health rows carry
`1970-01-01`, because the column is `not null` and extraction had no date; rendered
verbatim that tells the frontier a procedure happened in 1970. Facts now dedupe in a
subquery and order by recency outside it, and an undated fact says "date unknown".

## What remains open

Fact-level near-duplicate suppression closed the last visible gap: domains deduplicate by
exact text, which let one episode written twice hold two of the eight slots. Containment
against the shorter of the two catches a restatement that adds a detail while leaving a
diagnosis and the operation for it both standing. Live, the fact set went from eight to
six. Prose is deliberately untouched — measured redundancy there was one pair above 0.25.

The corpus is largely written in the third person already ("the user…"), so much of it is
de-identified by luck rather than design; the gate should not be credited for that.
Turning the gate off (`AugmentedTurn:Gate=false`) sends everything retrieved — available,
deliberately not the default, and Steve's decision alone.
