# ADR 0012 — Observation retention: keep the words, treat vectors as reclaimable

- **Decision (proposed):** Observations are never deleted. The retention lever is the derived data: embeddings may be dropped and rebuilt, and old `chat`-source observations may be *excluded from retrieval* via an append-only sidecar once a reflection pass has distilled them — the raw text stays queryable forever. No action until measured thresholds trip.
- **Date:** 2026-08-23
- **Status:** proposed — deleting or excluding anything from Steve's memory record is Steve's call, not an agent's; nothing here executes until approved
- **Supersedes:** none. Closes the B9 board item; the *event* retention question in the register stays open (different table, different tradeoffs).

## Context

The board flagged `chat`-source growth: every conversational turn appends
observations, and unlike the one-time Hermes import this stream compounds daily.
Measured today (2026-08-23):

| Table | Size | Note |
|---|---|---|
| `observations` (7,051 rows) | 3.2 MB | text is effectively free — ~180 bytes/row |
| `observation_embeddings` | 93 MB | **29× the text it indexes** — ~13.5 KB/row with the HNSW index |
| `execution_events` | 128 kB | out of scope here (register item stays open) |

`chat` has 3 rows as of today. At a plausible 50 observations/day the text grows
~3 MB/year; the embeddings grow ~250 MB/year. The storage problem is not the
memory — it is the index *of* the memory.

## Alternatives considered

| Option | Why not |
|---|---|
| Delete old chat observations | Violates the append-only foundation (D-009) and the charter's premise that the corpus is the identity record. 3 MB/year does not justify amputating memory. |
| Summarize-then-delete (compaction by destruction) | Same violation with extra steps; a summary written by a model replaces primary evidence with an interpretation. B10 just spent a day recovering information the Hermes migration lost — do not build a system that loses information on purpose. |
| Do nothing, forever | Embeddings at ~250 MB/year is fine for years on this host, but "we never wrote the policy" is how 93 MB quietly becomes the reason retrieval slows. The policy should exist before the pressure does. |

## Proposal

1. **Observations: permanent.** All sources. The append-only trigger stays. At
   measured growth the text costs less per year than one photograph.
2. **Embeddings: derived, therefore reclaimable.** Already versioned per row
   (ADR-0009) and rebuildable at ~107 docs/s. If `observation_embeddings`
   exceeds **10 GB**, drop vectors for observations older than 24 months that
   have never been retrieved into a turn's context, oldest first; the embedder
   re-indexes on demand if they are ever needed. Retrieval provenance for this
   exists in the event stream.
3. **Chat chatter: exclude, never erase.** After the weekly reflection pass has
   consumed a window of `chat` observations into conclusions (provenance rows
   exist), observations older than **12 months** may gain a row in an
   append-only `observation_retrievals_excluded` sidecar (mirroring the B10
   repair pattern): retrieval joins exclude them from ANN candidates, `dami
   recall` and the corpus reads still return them. Reversal = delete the
   sidecar row; the observation itself was never touched.
4. **Review trigger, not schedule:** act when `observations` passes 500 k rows
   or `observation_embeddings` passes 10 GB, whichever first. `dami stats`
   already reports both inputs; the llm-guard timer pattern can alarm on the
   thresholds when they get close.

## Evidence

Sizes measured above (`pg_total_relation_size`, 2026-08-23). Re-embed rate
measured in ADR-0009 (~107 docs/s ⇒ full corpus rebuild in about a minute; even
1 M rows ≈ 2.6 h). The sidecar-exclusion pattern is running today for date
repairs (migration 012) and costs one `left join` on reads.

## Consequences

Nothing changes now — this ADR is a tripwire with a plan attached. When it
trips, the actions are reversible (vectors rebuild; exclusions un-exclude) and
none of them delete a word Steve's history wrote.

## Reversal path

Rescind the ADR; drop the (empty until triggered) sidecar; re-run the embedder
to restore any dropped vectors. The observations themselves were never at risk
— that is the point.
