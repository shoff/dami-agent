# Health domain — privacy review (K2)

**Classification: LocalOnly, maximally sensitive.** Health is the most sensitive
domain the system holds. This review records why the health domain cannot leak,
by construction rather than by policy.

## What the domain contains

`dami.health_events` — structured facts (diagnosis, appointment, medication,
vital, procedure, symptom) with a date, a description, and a provenance link to
the observation each was extracted from. `dami.health_examined` — a high-water
marker of which observations the collector has read.

## The egress argument, path by path

1. **The collector never sends.** `HealthCollectorService` reads observations and
   the *loopback* chat model only (`IChatClient` -> Ollama on 127.0.0.1). It has no
   `IEgressClient`, no `IFrontierChat`, no HTTP client of its own. There is no code
   path from the collector to the network.
2. **The store never sends.** `IHealthEventStore` exposes record/read over Postgres
   on localhost. No method constructs a request or touches egress.
3. **The API serves only loopback.** `/health-log` is mapped on `dami-host`, which
   binds `127.0.0.1:5810` — a privacy boundary, not a deployment detail. It is
   unreachable from off-host without an explicit, separately-decided auth step.
4. **The consent door does not read health rows.** The C4 brief flow
   (`/briefs`, `BriefExecutor`) assembles its draft from the *context builder's*
   memories and beliefs, not from `health_events`. A health fact can only reach a
   frontier if it is already an observation the redactor surfaced AND Steve approved
   the exact redacted bytes — the same gate every other memory passes, with the
   redactor instructed to strip identifiers and Steve reviewing verbatim. The
   structured health table adds no new egress path.

## Provenance and correction

Every health event names the observation it came from. A wrong extraction is
traceable to its source and correctable there — the same discipline as the belief
ledger. The collector is idempotent on (observation, description): re-running it
cannot duplicate or drift what it already extracted.

## Residual risk

The extraction quality is only as good as the local model. A mis-categorized or
hallucinated fact is a *correctness* risk, not a *privacy* one — it stays
LocalOnly regardless. Mitigation for correctness is the provenance link and, when
K3 lands, the reflection pass cross-checking health rows against their source.

## Verdict

Approved as LocalOnly. The domain has no egress path; leakage would require new
code that deliberately wires one, which this review exists to make conspicuous.
