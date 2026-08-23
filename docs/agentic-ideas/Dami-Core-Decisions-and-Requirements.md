# Dami Core — Decision Record and Requirements Register

**Prepared for:** Steve Hoff
**Date:** 2026-08-22
**Companion documents:** Dami-Core-Project-Charter.md, Dami-Core-Architecture.md, Dami-Core-Operating-Rules.md

This document records *why* the architecture looks the way it does. The architecture document says what to build; this one preserves the reasoning, the alternatives considered, and the reversal path — the discipline the charter's §20 asked for.

---

## Part I — Decision record

Each decision follows the charter's change-control format: decision, context, alternatives, evidence, consequences, reversal path.

---

### D-001 — The product is a continuous modeling system, not a chat agent

**Decision.** Dami Core's primary loop runs without the user present. Conversation is a surface over a system that continuously observes, models, and surfaces.

**Context.** The charter described a better-built request/response agent with a strong execution graph. Discussion established that the actual goal is a coach, confidant, auditor, and muse — something that identifies patterns in behavior, thought, and request, and surfaces things not yet known to be wanted.

**Alternatives considered.** Improved reactive agent with background jobs bolted on. Rejected: makes the novel capability an afterthought and the well-understood capability the centerpiece.

**Evidence.** The existing Hermes agent has read all 7,000 memories and has never once acted on them unprompted. The corpus is not unexplored; it is unused. The gap is initiative, not knowledge.

**Consequences.** Turn-scoped tracing is demoted to a subsystem. The proactive tier gets its own process, its own contracts, and its own phases. The event model needs an origin discriminator because half the system's work has no user attached.

**Reversal path.** None needed; reactive operation remains fully supported. Proactive services can be disabled individually.

---

### D-002 — MAI is the reference architecture

**Decision.** Dami Core is MAI's architecture retargeted from a work codebase to a person, plus genuinely new capability. Porting is the default; rebuilding requires written justification.

**Context.** MAI is a production greenfield C#/.NET developer intelligence agent by the same developer: separate tool/skill registry with on-demand acquisition, tiered routing with a local Ollama sidecar, vector memory, PostgreSQL mutation ledger, 19 `IHostedService` collectors, sub-2s streamed responses on complex RAG-backed work including one-shot code generation.

**Evidence.** Same developer, same language, same providers, comparable task complexity — MAI at sub-2s, Hermes at 22–35s. The delta is the harness. This is a stronger argument for the project than any token count.

**Consequences.** Charter risk "rebuilding too much infrastructure" drops from speculative to largely solved. Several open decisions close by precedent. Phase estimates for the runtime should reflect porting, not building.

**New work, i.e. the actual scope:** persistent model of a person with supersession and audit; proactive initiative; voice with cloned TTS; vision on personal media; animated presence; personal gateways; execution graph UI; cross-domain correlation.

**Reversal path.** Per-component. Any port can be replaced by a rebuild if the port proves ill-fitting.

---

### D-003 — Host OS reverts to Debian 13 with Cinnamon

**Decision.** Debian 13, Cinnamon desktop. Reverses the charter's preference for openSUSE Tumbleweed.

**Context.** The charter demoted Debian on the grounds that a rolling distribution suits an experimental AI-development workstation.

**Why that argument fails.** The components that must be current — .NET SDK, NVIDIA driver — come from Microsoft's and NVIDIA's own repositories. The Python/CUDA inference stack is pinned per-service regardless of host. What the host must supply is a kernel and desktop that do not break, on a machine running unattended scheduled services.

**Alternatives.** openSUSE Tumbleweed + GNOME (charter's leader), Arch/EndeavourOS.

**Consequences.** Older desktop packages, mainly relevant for very new GPU hardware — not a concern for a 4080. Snapshot strategy still required.

**Reversal path.** Snapshot before install; the machine can be reinstalled. No application code depends on the distribution.

---

### D-004 — Bare metal by default; containers only for inference sidecars

**Decision.** .NET services run as systemd units on bare metal. PostgreSQL runs on bare metal from the PGDG repository. Containers are used only for Python/CUDA inference services.

**Context.** An earlier draft overstated the role of containers.

**Rationale.** Containers solve dependency conflict. .NET self-contained deployments have no conflict to solve. Postgres in a container adds a volume mount, a network hop, and a worse backup story on a single-host system. The genuine case is Ollama, embedding, reranker, STT, TTS, and vision — services with conflicting torch and CUDA requirements where per-service pinning is the difference between a routine upgrade and a lost Saturday.

**Consequences.** Simpler debugging, fewer layers, direct systemd integration.

**Reversal path.** Any component can be containerized later without code changes.

---

### D-005 — Interactive runtime is an API; CLI is a thin client

**Decision.** `Dami.Host` is an API on localhost. The CLI is a thin binary on `PATH`. The GUI and voice pipeline are additional clients of the same API.

**Context.** Confirms and sharpens the charter's §4.3 shared-contract discipline.

**Consequences.** Remote use is SSH to the host, then the local CLI against the local API. Exposing the API beyond localhost is deferred as a separate auth decision.

---

### D-006 — Interactive and proactive tiers are separate processes

**Decision.** `Dami.Host` and `Dami.Host.Proactive` are distinct processes with independent lifecycles.

**Rationale.** Opposite optimization targets. Interactive work is latency-critical and user-present; proactive work is throughput-tolerant and user-absent. A stuck reflection pass must never make Dami slow to answer.

**Consequences.** They share only the event store and data layer. No shared scheduler, no shared failure domain.

---

### D-007 — PostgreSQL + pgvector replaces Weaviate

**Decision.** Single PostgreSQL instance with pgvector, accessed through `Microsoft.Extensions.VectorData`.

**Context.** Weaviate is in use but was described as opaque and heavily AI-assisted to operate.

**Rationale.** One service instead of two, since Postgres is required regardless. Transactional cross-domain joins — the reflection pass needs to correlate embeddings against health rows against commit timestamps in one query, which is a join, not a vector search. Npgsql instead of a bespoke client, removing the opacity. Scale is not a concern: 7,000 memories is trivial and HNSW handles low millions.

**Alternatives.** Qdrant (designated escape hatch; strong .NET client, good filtered search). LanceDB, Chroma (weak .NET story). Weaviate (bundles multi-tenancy and module infrastructure that goes unused).

**Consequences.** Reranking, image vectors, and hybrid search move to the service layer — see D-008.

**Reversal path.** The MS abstraction makes a swap to Qdrant roughly an afternoon.

---

### D-008 — Reranking, image vectors, and hybrid search are service-layer concerns

**Decision.** These are model calls, not database features.

**Context.** Weaviate bundles them as modules, making them feel like storage capabilities. That bundling is precisely the opacity being removed.

**Implementation.** Reranking: pgvector returns top-50, a local cross-encoder reorders to top-8, ~40 lines against a sidecar. Image vectors: local vision model produces vectors, stored in their own table with their own dimension. Hybrid search: `tsvector` plus reciprocal rank fusion in SQL, ~80 lines, written once.

**Deferred.** A shared multimodal embedding space, where text queries retrieve images natively, is a model decision rather than a store decision.

---

### D-009 — Two memory layers: observation corpus and conclusions ledger

**Decision.** Observations are append-only, embedded, never edited. Conclusions are relational, versioned, supersedable, provenance-bearing. Only currently-active conclusions are embedded.

**Rationale.** Observations record what happened and are never wrong. Conclusions are inferences and get retracted. Mixing them means a retracted conclusion stays semantically retrievable forever and keeps influencing the next pass, because nearest-neighbour search does not respect tombstones unless made to. The charter's §9.4 already required supersession over silent coexistence; this is the structure that delivers it.

**Consequences.** The conclusions ledger must render end-to-end — a few hundred rows, readable in one sitting, diffable month over month. That property is a safety instrument, see D-011.

---

### D-010 — Embedding models are self-hosted and chosen by evaluation

**Decision.** Local embedding. Model selected by a 50-query eval set built from the existing corpus, not by leaderboard rank.

**Candidates.** Qwen3-Embedding-4B/8B (Apache 2.0, instruction-aware prompting, configurable dimensions). BGE-M3 (MIT, conservative default, native sparse+dense).

**Rationale for self-hosting.** The corpus is personal. It does not leave the machine.

**Rationale for evaluation.** Public leaderboard rank routinely diverges from in-domain performance by several places. The corpus provides a natural eval set.

**Consequences.** The embedding model is versioned in the schema. Changing stores means reindexing; changing embedders means re-embedding everything and shifting the meaning of every stored vector. The migration path must exist before it is needed.

---

### D-011 — Structural instruments against auditor decay

**Decision.** A pushback ledger and an end-to-end-renderable conclusions diff.

**Context.** Two stated requirements are in direct conflict: Dami must tune itself on observed reactions, and Dami must function as an auditor and guard. Blunt rebuke from someone not liked does not land, so personality is instrumental to critique landing at all.

**The failure mode.** A system optimizing on reactions learns that challenge produces negative reactions. The cheapest path to "his criticism lands well" is fewer criticisms. Given six months it agrees with everything, warmly, and the drift is invisible from inside the conversation.

**Why prompt wording is insufficient.** The gradient runs one direction regardless of instruction, and the artifact of the drift is tone, which is not diffable.

**Instruments.** Pushback ledger: every challenge logged with what it contradicted, whether it was accepted, and what followed. Reviewed quarterly; a falling rate is direct evidence of decay. Conclusions diff: month-over-month text comparison makes drift toward flattery visible as text, which it never is as tone.

**Honest limitation.** These detect drift. They do not prevent it. Detection is the achievable goal.

---

### D-012 — Privacy is an enforced architectural boundary

**Decision.** Profile stays in, queries go out. Enforced at the service boundary in code, not by prompt instruction.

**Local only.** Personal photos and media, file contents and organization, the conclusions ledger, the observation corpus, health/finance/relationship data, embedding of any of the above, image categorization and captioning.

**May leave the host.** Search queries, public URLs to fetch, feed requests, anonymized technical questions.

**Mechanism.** Outbound-capable services take a dependency on an egress client that refuses profile-derived payloads. Local-only services receive no egress client at all. Frontier-model calls are egress events subject to the same check. Enforcement is auditable in the composition root.

**Consequences.** Local vision and embedding become requirements rather than preferences. This is the strongest argument for the local model layer and makes the GPU more central than the charter implied.

---

### D-013 — Custom async TCP/UDP transport with a hand-rolled packet library

**Decision.** Build it. Explicit learning objective, cost accepted.

**Honest accounting.** gRPC or SignalR would cover the interactive path in an afternoon. The happy path of a custom protocol is a weekend; reconnect, backpressure, partial frames, and versioning are roughly five times that.

**Where it genuinely earns its place.** Voice streaming wants UDP with custom sequencing and jitter handling, and HTTP is poor at this. The GUI event stream at token rate plus execution events plus avatar state benefits materially from compact binary framing. An animated avatar responding to speech needs low-latency high-frequency small-payload delivery.

**Foundation.** `System.IO.Pipelines`, `ReadOnlySequence<byte>`, `ArrayPool<byte>`, `Span<T>`. `SslStream` if traffic ever leaves localhost — framing is hand-rolled, cryptography is not.

**Guardrails.** `ITransport` in `Dami.Contracts` from the first commit. A working `LoopbackTransport` before the real one. Protocol version in the frame from day one.

**Reversal path.** The abstraction is the reversal path. If the custom transport is not ready when the runtime is, a boring implementation drops in and the project continues.

---

### D-014 — Tool and skill definitions

**Decision.** Industry-standard definitions, stated crisply, and Dami must be able to answer the question on demand.

- **Tool** — executable code invoked through a typed schema. Extends what the agent **can do**.
- **Skill** — procedural knowledge loaded into context. Extends what the agent **knows how to do**.
- **One-liner** — tools are capability, skills are procedure.
- **Capability bundle** — the routing unit: a named set of tools plus skills selected together.

**The edge case and its resolution.** Skills may bundle scripts. That is not a skill containing a tool: the script runs only because the agent reads the instruction and invokes a terminal tool. Skills never execute; they execute *through* tools.

**The second distinction.** Loading. Tool schemas must be advertised and therefore cost context. Skills use progressive disclosure. That is why skills scale to hundreds and raw tool schemas do not.

---

### D-015 — Unified capability registry over three sources

**Decision.** Native C# plugins, MCP servers, and skills normalize into one registry with one lookup surface. The model queries by intent and never needs to know the source.

**Retrieval, not routing.** The charter called this "a deterministic or inexpensive capability router." It is neither — it is retrieval over embedded capability descriptions, using the same pgvector store, embedding service, and reranker built in Phase 2. A returned skill expands to pull in the tools it references.

**Native plugins are the privileged tier, not a fallback.** MCP carries serialization overhead and a process boundary; native tools have in-process access to Postgres, domain services, and the event bus. MCP is also an egress surface by definition. **Use MCP for third-party integrations; use native for anything that is Dami.**

**MCP trust.** Third-party tool descriptions are written by strangers, enter context, and shape behavior — a prompt-injection surface. Every server registers with an explicit trust level. Untrusted descriptions are summarized rather than followed, and untrusted MCP tools are excluded from turns touching local-only data.

---

### D-016 — Self-improvement: skills free, tools gated

**Decision.** Dami may author, revise, and retire skills without approval. Self-authored tools land in a staging registry and require explicit human promotion.

**Rationale.** These are not the same risk class. A self-written skill is readable text that executes nothing and is recoverable when wrong. A self-written tool is arbitrary code with persistence, running at agent privilege, that will execute again tomorrow unobserved. The tool/skill boundary from D-014 is exactly where the approval line belongs.

**Mechanism.** Tool proposals carry source, tests, rationale, and the observations that motivated them. No self-authored tool holds write or delete capability in v1. Codebase audit runs as a proactive service: it reads the repository, correlates against the conclusions ledger and observed failures, surfaces findings, and proposes patches. It does not commit.

---

### D-017 — Event store is canonical; OpenTelemetry is an export path

**Decision.** The append-only PostgreSQL event store is the source of truth. OTel receives an export for operational telemetry. Trace and span identifiers are shared; reconciliation is one-directional.

**Context.** The charter proposed both without saying which wins, which is two sources of truth that will diverge.

---

### D-018 — Events carry an origin; TurnId becomes TraceId

**Decision.** Add an `ExecutionOrigin` discriminator: `UserTurn`, `ScheduledService`, `ReactiveTrigger`, `SelfAudit`. Rename `TurnId` to `TraceId`; a trace may have no user attached.

**Rationale.** The charter's event contract assumes a user turn. Proactive work has none. Without this, most of the system's work is invisible to the graph, which defeats the graph.

---

### D-019 — First proactive service is the interest scout

**Decision.** YouTube, news, and feed discovery ships first. Media librarian second. Reflection pass third.

**Rationale.** Tightest feedback loop, not highest value. The quality of a recommendation is knowable in thirty seconds, and that judgment trains the "what does Steve find interesting" model every later proactive service depends on. It is also the safest: worst case is a bad video suggestion.

**Rejected as first.** Nightly code review — MAI already does it, it is a solved problem, and it is not engaging. Cross-domain reflection — highest value but slowest signal and the hardest to tune blind.

---

### D-020 — Background services propose; they do not act

**Decision.** Any consequential side effect from proactive work routes through the same approval contract as an interactive turn.

**Specific application.** File organization is propose-only: Dami produces a manifest of suggested moves and tags and executes nothing until approved. No delete capability in v1. This is the one background capability that can destroy something irreversibly.

---

### D-021 — Proactive output is scarce by design

**Decision.** Scarcity is enforced in the type system. `ProactiveResult` separates `Conclusion` from `Surfacing`; most passes produce conclusions and no surfacings.

**Rationale.** A muse that speaks constantly is an infestation. One good observation is worth more than a feed. The reflection pass produces one observation per week, or none.

---

### D-022 — Phases reordered

**Decision.** Data foundation and the first proactive service precede the GUI, which moves to Phase 7.

**Rationale.** The charter front-loads platform and runtime, which are the best-understood parts. This ordering front-loads what is novel and uncertain. The GUI is the least risky component and historically the most likely to consume the schedule.

---

### D-023 — AGENTS.md is a skill, and a fourth skill source

**Decision.** Register AGENTS.md files as skills. Sources become four: disk, self-authored, MCP-adjacent, and any repository Dami works in.

**Context.** A field study of the AGENTS.md files in the 100 most-starred GitHub repositories that ship one (Coldtea, 21 August 2026; 11.4M combined stars) established the convention's shape. Independently verified: neovim's file is exactly as reported at 6 lines and 189 bytes, and the star figures are consistent with current public rankings.

**Rationale.** AGENTS.md matches D-014's skill definition precisely — procedural knowledge, loaded into context, executing nothing. The convention converged on the same shape independently, which is corroboration. Registering it costs no new format work and means Dami inherits the operating knowledge of any project it touches.

**Trust treatment.** An AGENTS.md Dami did not author is observed content. It informs procedure; it does not override policy, approval boundaries, or egress rules. Same handling as an untrusted MCP description under D-015.

**Consequences.** `CapabilitySource.Skill` gains a provenance marker for the originating repository. Phase 3 deliverable.

---

### D-024 — Dami maintains an operating manual about itself

**Decision.** Corrections about Dami's *behaviour* produce proposed lines in a self-maintained operating manual, written in must/always/never voice and annotated with the incident that caused them.

**Context.** The field study's most transferable finding is a mechanism rather than a style. The corpus carries 784 explicit prohibition bullets; 56 of 99 files stack three or more; 90% write in must/always/never. They read as scar tissue — each records a specific mistake an agent already made, written down at the moment of correction.

**Rationale.** The accumulation *is* the value, and it is a loop Dami can run on itself. This is skill authoring, which D-016 already permits without approval, so it requires no new governance.

**Constraints.** The manual is capped; when it exceeds budget, least-invoked rules are retired and the retirement recorded. Reviewed quarterly alongside the pushback ledger per D-011. Registry validation rejects any self-authored rule that weakens an approval boundary, a privacy boundary, or the pushback obligation. Steve may edit or delete any line directly.

**Sequencing.** Seeded in Phase 5 from real pushback-ledger entries, not written speculatively. A manual of imagined mistakes is abstract advice and defeats the mechanism.

---

### D-025 — Verification protocol with seven levels

**Decision.** Define format, unit, contract, round-trip, boundary, replay, and chaos verification levels, with an explicit mapping from change type to required levels. Enforce at the staging registry.

**Context.** D-016 required tests in a tool proposal but never specified the procedure. The field study exposed this as the gap: testing and validation is 17.2% of corpus word count and appears in 74% of files. The dominant thing humans write down for agents is how to prove nothing broke.

**Rules.** A tool proposal without tests is rejected at the registry, not reviewed and then rejected. A patch states which levels it ran and which it did not, and why. "Tests pass" is not a claim; the output is attached. A patch touching `Dami.Privacy` requires the boundary level with no exception path.

**Converged rule.** *Do not claim that an interrupted or timed-out test passed* appears verbatim in the corpus and is the same rule as charter §10.1's requirement that reported success be backed by a verifiable result. Two unrelated sources arriving at one rule is the strongest signal in the study. It belongs in the repository AGENTS.md, in Dami's operating manual, and in registry validation.

---

### D-026 — The stable prompt budget is confirmed as generous

**Decision.** No change to the ~5,000-token stable prompt target, now with external evidence that it is comfortable rather than aggressive.

**Evidence.** The median AGENTS.md across the sample runs 1,198 words, roughly 1,600 tokens. One file in ten is under 150 words. Microsoft's vscode ships 33 words and a redirect to separate instructions; Hugging Face's transformers ships a single file path; neovim ships 35 words and one rule. Meanwhile 37% run past 1,500 words — a barbell, with the short end populated by projects that clearly thought about it.

**Consequence.** Progressive disclosure is the pattern large projects converge on independently. Dami's identity charter should follow the same shape: a small always-loaded core with detail deferred, rather than a constitution.

**Counter-note.** Only 27% of the top 1,000 repositories ship an AGENTS.md at all. The convention is not yet settled practice, and the corpus should be read as evidence rather than as authority.

---

## Part II — Requirements register

### Functional requirements

**Identity and relationship**

- F-01 Dami presents a stable identity across models, sessions, interfaces, and worker processes
- F-02 Personality is instrumental: it exists so that challenge, correction, and counsel are actually heard
- F-03 Dami functions as coach, confidant, developer partner, sounding board, auditor, and guard
- F-04 Continuity does not depend on carrying an enormous inherited transcript

**Continuous modeling**

- F-05 Interactions are continuously recorded, including mood and reaction to questions
- F-06 Dami maintains an evolving model of the user and tailors behavior to it
- F-07 Dami identifies patterns across behavior, thought, and request over time
- F-08 Dami surfaces things the user did not know to ask for
- F-09 Conclusions about the user are inspectable, editable, versioned, and provenance-bearing
- F-10 Corrections supersede prior conclusions rather than coexisting with them

**Proactive operation**

- F-11 Interest discovery: relevant videos, news, and reading, summarized
- F-12 Local media and file categorization, propose-only
- F-13 Cross-domain correlation across health, workshop, code, civic, finance, relationships
- F-14 Codebase audit with proposed improvements
- F-15 Self-audit of pushback rate and conclusion drift
- F-16 Proactive output is rate-limited and thresholded

**Capability**

- F-17 Unified registry over native plugins, MCP servers, and skills
- F-18 Semantic capability lookup by stated intent
- F-19 On-demand schema acquisition rather than advertising every tool
- F-20 MCP server registration with explicit trust levels
- F-21 Native C# plugin system with attribute-based discovery
- F-22 Dami authors and revises skills autonomously
- F-23 Dami proposes tools into a staging registry for human promotion
- F-24 Dami can state the difference between a tool and a skill on request
- F-36 AGENTS.md files register as skills, from any repository Dami works in
- F-37 Dami maintains a capped, reviewable operating manual about its own behavior
- F-38 Behavioral corrections propose durable rules; factual corrections do not
- F-39 Tool proposals declare which verification levels ran and attach the output

**Interfaces**

- F-25 CLI fully usable over SSH without a graphical session
- F-26 Graphical client with conversation view and live execution graph
- F-27 Voice: wake detection ("Hey Dami", DAH-mee), STT, cloned TTS, barge-in
- F-28 Animated avatar (candidate; serves presence, subject to D-030 below)
- F-29 Discord and personal messaging gateways
- F-30 All interfaces are clients of one runtime API

**Safety and approval**

- F-31 Consequential actions require explicit approval, from interactive and proactive work alike
- F-32 Voice-originated commands receive identical approval treatment
- F-33 Approvals are first-class trace nodes, not transient dialogs
- F-34 External writes carry idempotency keys
- F-35 Secrets never appear in prompts, traces, logs, screenshots, or source control

### Non-functional requirements

- N-01 Sub-2s streamed response on complex work, matching MAI
- N-02 Stable prompt ≈5k tokens; per-turn tool surface ≈5k tokens
- N-03 Personal data never leaves the host — enforced in code
- N-04 Local inference for embedding, reranking, vision, STT, TTS
- N-05 Single-host operation; no cloud dependency for core function
- N-06 Unattended reliability; scheduled services run without supervision
- N-07 Durable, replayable, append-only event history
- N-08 Rollback available at host, database, and component level
- N-09 The system is understandable and ownable by one developer
- N-10 No dependence on a patched general-purpose framework

### Coding conventions

- C-01 No underscore prefixes on fields, ever
- C-02 `sealed` by default on concrete types
- C-03 Records for contracts and events; classes for services
- C-04 `IAsyncEnumerable<T>` for streaming, not callbacks
- C-05 Nullable reference types enabled and enforced as errors
- C-06 Cancellation tokens threaded through everything, including proactive work
- C-07 Framing and serialization remain separate layers

---

## Part III — Explicitly rejected

| Rejected | Reason |
|---|---|
| Adopting an existing agent framework | Off the table. Ownership and capability-building are project goals. |
| Weaviate | Opaque client surface, second service, no transactional joins with domain data. |
| openSUSE Tumbleweed | Rolling host buys little once inference is pinned and vendor repos supply .NET and NVIDIA. |
| Containerizing .NET services or Postgres | Solves a dependency problem that does not exist here; adds layers and worsens backups. |
| gRPC / SignalR as primary transport | Correct by default, deliberately declined as a learning objective. Remains the fallback. |
| Nightly code review as first proactive service | Solved in MAI, not engaging, weak learning signal. |
| Honcho as memory provider | Custom Postgres design already proven in MAI. |
| Prompt-based mitigation of sycophancy | The gradient runs one way regardless of instruction; needs structural instruments. |
| Self-registering tools | Arbitrary persistent code at agent privilege; requires human promotion. |
| Plugin marketplace, multi-user platform | Out of scope, per charter. |
| CI/PR etiquette rules from the AGENTS.md corpus | 79% presence and 10.9% of corpus words, but they solve multi-contributor problems. One developer, one repo. Keep only "never commit or push unasked." |
| Prose-only security rules | The corpus states security in 54% of files at 1.2% of word count — stated, not enforced. Direct evidence for D-012's code-enforced boundary. |
| A constitution-length identity prompt | Median corpus file is 1,198 words; the short end is populated by projects that clearly thought about it. |

---

## Part IV — Open questions

1. **Embedding model** — decided by the Phase 2 eval, not by argument
2. **Payload serialization** inside the transport frame — MemoryPack, hand-rolled span writer, other. Framing must not depend on it.
3. **GUI framework** — Tauri/React vs Avalonia
4. **Local sidecar model** and VRAM budget alongside resident TTS
5. **Surfacing channel behavior** — queue, notification, or held-until-adjacent-opening. This single decision shapes the muse more than model choice does.
6. **Confidence threshold** for surfacing, and how it self-tunes without gaming itself
7. **TTS engine** and a legally clean voice source with documented consent
8. **Avatar** — whether it serves presence or distracts from it. Decide after voice proves itself.
9. **Event retention and compaction** policy
10. **Backup destinations**, encryption, retention schedule
11. **Repository visibility** and licensing
12. **Which Hermes sessions and skills** are worth migrating at all
13. **Whether the Mac remains** permanently as an Apple-services bridge
14. **Remote API exposure** beyond localhost, and its auth design
15. **Instrumentation results** — TTFT vs stream duration vs tool round-trips on Hermes and MAI. Determines whether the latency win comes from prompt weight or round-trip count.

---

## Part V — Success definition

Dami Core succeeds when, on an ordinary Tuesday, Dami says something the user did not ask for, did not know, and is glad to have heard — and the user can open the ledger, see exactly why Dami thought it, and correct it if it is wrong.

Everything else is infrastructure for that sentence.
