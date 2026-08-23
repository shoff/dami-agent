# Dami Core — System Architecture

**Prepared for:** Steve Hoff
**Date:** 2026-08-22
**Status:** Architecture draft, supersedes portions of the Project Charter
**Companion documents:** Dami-Core-Project-Charter.md, Dami-Core-Decisions-and-Requirements.md, Dami-Core-Operating-Rules.md

---

## Resume here

Context switch point. This document was written on mobile; work continues on the Debian workstation.

**Decided so far, beyond the charter:**
- The product is a continuous modeling system with a conversational surface, not a chat agent (§0)
- MAI is the reference architecture; most of the runtime is a port, not a build (§1)
- PostgreSQL + pgvector replaces Weaviate; two memory layers, corpus and conclusions (§4)
- Hard privacy boundary: profile stays in, queries go out, enforced in code (§5)
- Proactive layer is the novel work; interest scout is first (§6)
- Deployment: .NET and Postgres on bare metal, containers only for Python/CUDA sidecars (§3.2)
- **Transport is a custom async TCP/UDP protocol with a hand-rolled packet library (§7.5) — an explicit learning objective, not a default**
- Unified capability registry over three sources: native plugins, MCP, skills (§7.6)
- Tools are capability, skills are procedure; self-authored skills are free, self-authored tools are gated (§7.6.5)
- AGENTS.md is a skill; it is a fourth skill source, and Dami maintains one about itself (§7.7)

**Next action on the workstation:** Phase 1 host validation, then the transport framing layer as the first code written (§7.5.5 has the build order).

**Open question carried forward:** serialization format inside the frame payload. Deliberately deferred.

---

## 0. What changed since the charter

The charter described a better-built request/response agent with a good execution graph. Conversation since then established that this is not the product. The product is a **continuous modeling system with a conversational surface**.

The distinguishing capability is not the runtime, the graph UI, or the token savings. It is that Dami runs when Steve is not present, maintains an evolving model of him, and surfaces things he did not ask for and did not know to ask for.

Three consequences follow, and they reorder the whole plan:

1. **Turn-scoped tracing is not the core architecture.** It is one subsystem. The proactive layer lives above turns and outlives them.
2. **The 7,000-memory corpus is a starting condition, not an archive.** It has been read but never acted on. The gap is initiative, not knowledge.
3. **Privacy is an architectural boundary, not a policy sentence.** Local vision and embedding are requirements, not preferences.

A second change: **host OS reverts to Debian 13 with Cinnamon.** The charter demoted this in favor of openSUSE Tumbleweed on the argument that a rolling distribution suits an experimental AI workstation. That argument is weaker than it looked. The pieces that genuinely need to be current — the .NET SDK and the NVIDIA driver — come from Microsoft's and NVIDIA's own repositories, not Debian's. The Python/CUDA inference stack is pinned per-service regardless of host. What Debian actually has to supply is a kernel and a desktop that do not break, and that is precisely what it is good at, on a machine intended to run unattended services on a schedule. The cost is older desktop packages, which matters mainly for very new GPU hardware — not a concern for a 4080. Recorded as a reversal with reasons, per §20 of the charter.

---

## 1. Prior art: MAI

MAI is a greenfield C#/.NET developer intelligence agent, in production, built by the same developer for the same ecosystem. It already implements most of what Dami Core proposes:

| MAI capability | Dami Core relevance |
|---|---|
| Separate tool/skill registry with on-demand acquisition | Directly portable. Answers charter §4.4 and open decision 10. |
| Tiered model routing (local Ollama sidecar + Anthropic models) | Directly portable. Not currently in the charter at all. |
| Vector memory for RAG | Pattern portable; store choice revisited below. |
| PostgreSQL mutation ledger | Directly portable. Answers open decisions 6 and 8. |
| 19 IHostedServices collecting, categorizing, maintaining data | The single most important precedent. This is the proactive layer's execution model. |
| Sub-2s streamed responses on complex work | Establishes the latency target and proves the architecture achieves it. |

**Dami Core should be understood as MAI's architecture retargeted from a work codebase to a person, plus the capabilities MAI never needed.**

What MAI does not have, and therefore what constitutes genuinely new work:

- A persistent model of a human being, with supersession and audit
- Proactive initiative — output not triggered by a request
- Voice: wake detection, STT, cloned TTS
- Vision applied to personal media
- An animated presence/avatar
- Discord and other personal gateways
- The execution graph UI
- Cross-domain correlation across health, workshop, finance, civic, code

That list is the actual scope. Everything else is a port.

**Consequence for planning:** Phase 2 of the charter ("runtime vertical slice") is largely a port exercise, not a build exercise, and should be estimated accordingly. The novel risk sits in the proactive layer and the model-of-Steve, which the charter barely mentions.

---

## 2. Design principles

1. **The conversation is a surface, not the system.** Most of Dami's work happens without a prompt.
2. **Profile stays in, queries go out.** See §5. Enforced at the service boundary, not by prompt instruction.
3. **Conclusions are auditable artifacts.** Anything Dami believes about Steve is inspectable, versioned, and editable.
4. **Proactive output is scarce by design.** Scarcity is what makes it worth reading. A feed becomes noise; one good observation does not.
5. **Background services propose; they do not act.** Any consequential side effect — moving a file, sending a message, spending money — routes through the same approval contract as an interactive turn.
6. **Local first for anything derived from personal data.** Embedding, vision, categorization, clustering.
7. **Loosely coupled subsystems over one large runtime.** The pieces share two contracts (events, memory) and nothing else.
8. **Every conclusion carries provenance.** Source, timestamp, confidence, and what superseded what.

---

## 3. System inventory

Dami Core is a constellation of independently deployable subsystems. They share the event bus and the data layer. Nothing else is shared, and nothing else should become shared.

```
┌────────────────────────────────────────────────────────────────┐
│  INTERACTIVE                                                    │
│  ├─ Dami.Core runtime (sessions, context, tools, approvals)     │
│  ├─ CLI / SSH gateway                                           │
│  ├─ Graphical client (conversation + execution graph)           │
│  ├─ Voice pipeline (wake → STT → turn → TTS)                    │
│  └─ Discord / personal gateways                                 │
├────────────────────────────────────────────────────────────────┤
│  PROACTIVE  (IHostedService, scheduled, no user present)        │
│  ├─ Interest scout        — YouTube, news, feeds                │
│  ├─ Media librarian       — local image/file categorization     │
│  ├─ Reflection pass       — cross-domain weekly correlation     │
│  ├─ Domain collectors     — health, civic, network, estate      │
│  └─ Self-audit            — pushback ledger review              │
├────────────────────────────────────────────────────────────────┤
│  MODEL LAYER                                                    │
│  ├─ Frontier providers (Anthropic, others) — routed             │
│  ├─ Local LLM sidecar (Ollama) — simple work, private work      │
│  ├─ Embedding service (local)                                   │
│  ├─ Reranker service (local)                                    │
│  ├─ Vision service (local)                                      │
│  └─ STT / TTS services (local, GPU-resident)                    │
├────────────────────────────────────────────────────────────────┤
│  DATA                                                           │
│  ├─ PostgreSQL + pgvector                                       │
│  │   ├─ execution events (append-only)                          │
│  │   ├─ observation corpus (embedded, append-only)              │
│  │   ├─ conclusions ledger (versioned, supersedable)            │
│  │   ├─ pushback ledger                                         │
│  │   └─ domain schemas (health, workshop, civic, estate…)       │
│  └─ Object storage — artifacts, media, model assets             │
└────────────────────────────────────────────────────────────────┘
```

### 3.1 Why the split matters

The interactive tier is latency-critical and user-present. The proactive tier is throughput-tolerant and user-absent. They have opposite optimization targets and should not share a process, a scheduler, or a failure domain. A stuck reflection pass must never make Dami slow to answer.

They meet in exactly two places: both write execution events, and both read/write the data layer.

### 3.2 Deployment model

Containers are used where they solve a real dependency problem and nowhere else.

| Component | Deployment | Why |
|---|---|---|
| `Dami.Host` (interactive API) | systemd, bare metal | Self-contained .NET deployment. No dependency conflicts to isolate. |
| `Dami.Host.Proactive` | systemd, bare metal | Same. |
| CLI | binary on `PATH` | It is a terminal client of the local API. Containerizing it would be nonsense. |
| PostgreSQL + pgvector | bare metal, PGDG apt repo | Current packages, proper systemd integration, simpler backups, no volume mount or network hop. |
| Ollama sidecar | container, pinned | CUDA and runtime version pinning. |
| Embedding / reranker services | container, pinned | Conflicting torch and CUDA requirements between services. This is the actual case for containers. |
| STT / TTS | container, pinned | Same. |
| Vision service | container, pinned | Same. |

**Architecture shape:** `Dami.Host` is an API listening on localhost. The CLI is a thin client. The GUI is a second client. The voice pipeline is a third. One runtime contract, several front ends — the charter's §4.3 discipline, made concrete.

Remote access is SSH to the host, then the local CLI against the local API. Exposing the API beyond localhost is a later decision with its own auth design, deliberately deferred.

---

## 4. Data architecture

### 4.1 Store selection: PostgreSQL + pgvector

Weaviate is replaced by pgvector. Reasoning:

- **One service instead of two.** Postgres is required regardless for the ledgers and domain schemas.
- **Transactional cross-domain joins.** The reflection pass needs to correlate embeddings against health rows against commit timestamps in one query. This is the core proactive capability and it is a join, not a vector search.
- **Npgsql instead of a bespoke client.** Removes the opacity that made Weaviate uncomfortable to own.
- **Scale is not a concern.** 7,000 memories is trivial. HNSW in pgvector handles low millions.

Accessed through `Microsoft.Extensions.VectorData` so the backend can be swapped for Qdrant later without rewriting call sites. Qdrant is the designated escape hatch if pgvector ever strains.

### 4.2 What pgvector does not do, and how that is covered

The features Weaviate bundles as "database features" are model calls. In a C# system they belong in the service layer anyway.

| Capability | Implementation |
|---|---|
| Reranking | pgvector returns top-50 by cosine; a local cross-encoder (Qwen3-Reranker-4B or BGE-reranker-v2-m3) reorders to top-8. HTTP call to a sidecar. |
| Image vectors | Local vision model produces vectors; stored in a separate table with its own dimension. Same store, different column. |
| Hybrid search | `tsvector` column plus reciprocal rank fusion in SQL. Written once, ~80 lines. |
| Multimodal shared space | Deferred. If text-queries-retrieve-images natively becomes a requirement, that is a model decision (shared-space embedder), not a store decision. |

### 4.3 Two memory layers, deliberately separate

**Observation corpus** — append-only, embedded, never edited. Things that happened: what was said, what was committed, what was measured. These are never wrong; the record is the record.

**Conclusions ledger** — versioned, supersedable, provenance-bearing. Things Dami believes: "Steve loses momentum on modeling projects around week six." These are inferences and they get retracted.

Mixing them is the failure the charter's §9.4 already warns about. A retracted conclusion in a vector index stays semantically retrievable forever and keeps poisoning the next pass, because nearest-neighbour search does not respect tombstones unless you make it. Keep conclusions relational with explicit supersession, and embed only the current active set.

The conclusions ledger must be **renderable end-to-end** — a few hundred rows, readable in one sitting, diffable month over month. That property is the sycophancy instrument (§6.3). Storage is relational; the audit view is generated.

### 4.4 Embedding strategy

Self-hosted, non-negotiable, because the corpus is personal.

- **Primary candidate:** Qwen3-Embedding-4B or 8B (Apache 2.0). Instruction-aware prompting matters here — "find behavioural patterns" and "find that code snippet" want different embedding behaviour from the same model. Configurable output dimensions help storage.
- **Safe alternative:** BGE-M3 (MIT), the conservative production default, with native sparse+dense.
- **Decision method:** build a 50-query eval set from the existing 7,000 memories with known-good answers, and test on that. Public leaderboards are a shortlist, not an oracle — in-domain results routinely diverge by several ranks.

**Version the embedding model in the schema.** Changing stores means reindexing. Changing embedders means re-embedding everything and shifting the meaning of every stored vector. Make the migration possible before it is needed.

---

## 5. The privacy boundary

This is a hard architectural line, not a guideline.

```
        LOCAL ONLY                    │        MAY LEAVE THE HOST
────────────────────────────────────  │  ──────────────────────────────
 personal photos and media            │   search queries
 file contents and organization       │   public URLs to fetch
 the conclusions ledger               │   news / video feed requests
 the observation corpus               │   anonymized technical questions
 health, finance, relationship data   │
 embedding of any of the above        │
 image categorization and captioning  │
```

**The rule: the profile stays in, the queries go out.**

Dami may search YouTube for woodworking content without disclosing why it is interested. The taste model that decides what is worth Steve's time is local; only the fetch crosses the boundary.

Enforcement is structural. Outbound-capable services take a dependency on an egress client that refuses payloads carrying profile-derived content. Local-only services have no egress client injected at all. A frontier-model call is an egress event and is subject to the same check.

This is the strongest argument for the local model layer in §3, and it is why the GPU matters more than the charter implied.

---

## 6. The proactive layer

The genuinely new work. Everything here is `IHostedService`, following MAI's precedent.

### 6.1 Common shape

Every proactive service follows the same five-step contract:

```
observe → correlate → conclude → threshold → surface
```

- **Observe** — read from a bounded set of sources.
- **Correlate** — cross-reference against the corpus and existing conclusions.
- **Conclude** — write to the conclusions ledger with provenance and confidence.
- **Threshold** — decide whether this clears the bar for saying anything. Most passes should conclude nothing worth surfacing.
- **Surface** — deliver through the chosen channel.

Building one right makes the next twelve variations. Building all thirteen at once produces an architecture that fits none of them.

### 6.2 First service: the interest scout

Selected as first because it has the tightest feedback loop, not because it is the most valuable.

Scans YouTube, news, and feeds against the model of Steve's interests. Surfaces a small number of items. Steve knows within thirty seconds whether a recommendation was good — and that judgment trains the "what does Steve find interesting" model that every subsequent proactive service depends on.

It is also the safest: worst case is a bad video suggestion.

Second service: **media librarian** — local image and file categorization. Propose-only. Produces a manifest of suggested moves and tags; executes nothing until approved. This is the one background capability that can destroy something irreversibly, and it gets the strictest approval treatment in the system.

Third: **reflection pass** — the weekly cross-domain correlation. One observation, Sunday night, or nothing. This is the service that justifies migrating the domain schemas at all, because it is the only thing that gets better as domains are added.

### 6.3 The self-audit problem

Dami is meant to tune itself on Steve's reactions and also to function as an auditor and guard. These requirements are in direct conflict.

A system optimizing on reactions learns that challenge produces negative reactions. The cheapest path to "his criticism lands well" is fewer criticisms. Given six months, it agrees with everything, warmly, and the drift is invisible from inside the conversation.

Prompt wording does not fix this. Two structural instruments do:

**Pushback ledger.** Every challenge Dami makes is logged: what it said, what it contradicted, whether Steve accepted it, and what happened afterward. Reviewed quarterly. A falling pushback rate is direct evidence the tuning loop is eating the auditor.

**Conclusions diff.** The ledger renders end-to-end. Month-over-month diffs make drift toward flattery visible as text, which it never is as tone.

Neither instrument prevents the drift. They make it detectable, which is the achievable goal.

---

## 7. Runtime and execution model

Largely as the charter describes, with adjustments.

### 7.1 Event store is canonical

The charter proposes both `ActivitySource`/OpenTelemetry and a custom append-only Postgres event store. That is two sources of truth and they will diverge.

**Resolution:** the Postgres event store is canonical. OpenTelemetry is an export path for operational telemetry. Trace and span identifiers are shared so correlation is possible, but reconciliation is one-directional.

### 7.2 Events carry an origin

The charter's event contract assumes a user turn. Proactive work has no turn. The contract needs an origin discriminator:

```csharp
public enum ExecutionOrigin
{
    UserTurn,
    ScheduledService,
    ReactiveTrigger,
    SelfAudit
}
```

`TurnId` becomes `TraceId`, and a trace may have no user attached. Without this, half the system's work is invisible to the graph, which defeats the point of the graph.

### 7.3 Tool acquisition

Ported from MAI. A registry holds full tool contracts; the model receives a compact catalog and requests schemas on demand. Target: ~5,000 tokens of stable prompt, ~5,000 tokens of tool surface per turn.

Measurement note: the charter attributes 22–35s latency to prompt weight. The mechanism is more likely round-trip count — a model choosing among 72 tools deliberates more, chooses wrong more, and retries more, and every retry is a full round-trip. Instrument time-to-first-token, stream duration, and tool round-trips per task separately on both Hermes and MAI. This determines what to protect, and gives Phase 7 a number instead of an impression.

### 7.4 Model routing

Ported from MAI. Not present in the charter and it should be.

- Local sidecar (Ollama) handles simple classification, summarization, categorization, and anything touching local-only data
- Frontier models handle synthesis, code generation, and reasoning
- Routing is deterministic where possible, cheap-model-assisted where not
- The privacy boundary (§5) is a routing input: local-only data cannot route to a frontier provider

### 7.5 Transport: custom async TCP/UDP protocol

**Status: requirement, not a default.** gRPC or SignalR would cover the interactive path in an afternoon. A hand-built transport is chosen deliberately as a learning objective, with the cost accepted.

The honest cost: the happy path is a weekend. Reconnect, backpressure, partial frames, and protocol versioning are roughly five times that. Budget accordingly and it is fine.

#### 7.5.1 Where it genuinely earns its place

This is less indulgent than it first appears, because three subsystems actually want it:

- **Voice.** Streaming audio in and out wants UDP with custom sequencing and jitter handling. HTTP is genuinely poor at this. Opus frames over UDP is the correct design independent of any learning goal.
- **Event stream to the GUI.** Token streaming plus execution events plus avatar state at frame rate is where compact binary framing beats JSON-over-WebSocket by a real margin.
- **Avatar.** Low-latency, high-frequency, small-payload delivery is this protocol's natural home.

#### 7.5.2 Foundation

`System.IO.Pipelines` is the correct .NET substrate and the thing worth learning. It exists precisely for this problem and handles the buffer management that makes naive socket code miserable. Kestrel is built on it; this would be built the same way.

- `PipeReader` / `PipeWriter` for connection I/O
- `ReadOnlySequence<byte>` for parsing across buffer segments
- `ArrayPool<byte>` and `Span<T>` for zero-allocation framing
- `SslStream` if traffic ever leaves localhost — framing is hand-rolled, cryptography is not

#### 7.5.3 Frame shape

```
┌──────────────┬──────────┬──────────┬────────────────┬───────┬─────────┐
│ length       │ type     │ sequence │ correlation id │ flags │ payload │
│ varint       │ uint16   │ uint32   │ 16 bytes       │ uint8 │ bytes   │
└──────────────┴──────────┴──────────┴────────────────┴───────┴─────────┘
```

**Framing and serialization stay separate layers.** They change at different rates and coupling them is the mistake that makes protocols hard to evolve. Payload serialization remains pluggable — MemoryPack, or a hand-rolled span writer — and is an open decision.

TCP carries request/response, streaming responses, and the event stream. UDP carries audio frames and avatar state, where loss is preferable to delay.

#### 7.5.4 Guardrails

1. **`ITransport` lives in `Dami.Contracts` from the first commit.** The runtime must never know what protocol carries its events. If the custom transport stalls, a boring implementation drops in and the project keeps moving. This is the single difference between an ambitious component and a single point of project failure.
2. **A reference `LoopbackTransport` implementation exists before the real one.** In-process, no sockets. It proves the abstraction is honest and gives the test suite something deterministic to run against.
3. **Version the protocol in the frame from day one.** Retrofitting versioning is the classic hand-rolled-protocol regret.

#### 7.5.5 Build order

1. Frame reader/writer over `ReadOnlySequence<byte>`, with round-trip property tests including deliberately split buffers
2. `ITransport` abstraction and `LoopbackTransport`
3. Pipelines-based TCP connection handling, one connection
4. Reconnect, heartbeat, and sequence gap detection
5. Backpressure and flow control
6. UDP path, once voice is on the roadmap

Step 1 is the starting point on the workstation.

### 7.6 Capability system: tools, skills, and the registry

#### 7.6.1 Definitions

These are the industry-standard definitions and Dami must be able to state them on request.

**Tool** — executable code the model invokes through a typed schema. The model chooses arguments; the runtime executes and returns a result. Deterministic, versioned, and it either exists or it does not. A tool extends what the agent **can do**.

**Skill** — procedural knowledge loaded into context. Instructions, conventions, worked examples, references. It changes how the model approaches a task but executes nothing itself. A skill extends what the agent **knows how to do**.

**The one-liner: tools are capability, skills are procedure.**

The apparent edge case: skills may bundle scripts, which looks like a skill containing a tool. It is not. A bundled script runs only because the agent reads the instruction and invokes a *terminal tool* to execute it. Skills never execute. They execute **through** tools. That boundary holds under pressure and is where the governance line is drawn (§7.6.5).

The second real distinction is **loading**. Tool schemas must be advertised to be usable, so they consume context. Skills use progressive disclosure — a short description is always visible, the body loads when relevant, bundled files load on demand. That is why skills scale to hundreds and raw tool schemas do not.

**Capability bundle** — neither of the above. It is the routing unit: a named set of tools plus skills selected together for a turn.

#### 7.6.2 Three sources, one registry

Capability arrives from three places and normalizes into one registry entry. The model never needs to know which source a capability came from.

| Source | Nature | Registration | Primary use |
|---|---|---|---|
| **Native C# plugins** | In-process assemblies | Attribute or interface discovery at startup | Anything that is Dami. Direct access to Postgres, domain services, the event stream. |
| **MCP servers** | Out-of-process, network protocol | Explicit config: URL, transport, trust level | Third-party integrations. |
| **Skills** | Folders with a descriptor | Filesystem scan, or authored by Dami | Procedure, conventions, worked examples. |

Common registry entry:

```csharp
public sealed record CapabilityEntry(
    Guid CapabilityId,
    string Name,
    string Description,
    CapabilityKind Kind,              // Tool | Skill | Bundle
    CapabilitySource Source,          // Native | Mcp | Skill
    TrustLevel Trust,
    IReadOnlyList<string> Tags,
    string? SchemaReference,          // tools only
    string? BodyReference,            // skills only
    IReadOnlyList<Guid> RelatedCapabilities,
    string Version,
    DateTimeOffset RegisteredAt);
```

#### 7.6.3 Retrieval, not routing

The charter described "a deterministic or inexpensive capability router." It is neither. It is retrieval, and the pipeline already exists from §9.3.

```
"I need to compare two images and describe the differences"
  → embed (local)
  → pgvector ANN over capability descriptions
  → rerank (local cross-encoder)
  → expand: a returned skill pulls in the tools it references
  → return bundle: schemas + skill bodies, ~5k tokens
```

Capability descriptions are simply another embedded corpus in the same store, served by the same embedding and reranker services. This is the second payoff from the Phase 2 data work.

The model queries the registry by intent. It gets back what is relevant, whether that is two MCP tools, a native plugin, or a skill that references three tools.

#### 7.6.4 Native plugins are the privileged tier

Not a fallback. MCP is a network protocol with serialization overhead and a process boundary; a native C# tool has in-process access to the database, domain services, and the event bus.

It is also the privacy-correct default. An MCP server is an egress surface by definition, and §5 governs it. **Use MCP for third-party integrations; use native for anything that is Dami.**

**MCP descriptions are written by strangers.** They enter context and influence behavior, and their text is not under local control. That is a prompt-injection surface. Every MCP server is registered with an explicit `TrustLevel`, and descriptions from untrusted servers are treated as data to be summarized, not instructions to be followed. Untrusted MCP tools may not be selected for turns that touch local-only data.

#### 7.6.5 Self-improvement governance

Dami audits its own codebase, proposes improvements, and authors new capability. The tool/skill boundary is exactly where the approval line falls, because the two are not the same risk class.

**Skills: authored freely.** A self-written skill is text. Wrong is recoverable, the content is readable, and it executes nothing on its own. Dami may create, revise, and retire skills without approval, with every change recorded as an execution event and diffable.

**Tools: proposed, never self-registered.** A self-written tool is arbitrary code with persistence, running at the agent's privilege level, that will execute again tomorrow unobserved. Self-authored tools land in a **staging registry** as a proposal carrying source, tests, a stated rationale, and the observations that motivated it. Promotion to the live registry requires explicit human approval. Same propose-only pattern as the media librarian (§6.2).

**Codebase audit** is a proactive service (§6). It reads the repository, correlates against the conclusions ledger and observed failures, and surfaces findings. It proposes patches. It does not commit.

### 7.7 Operating manuals and the AGENTS.md convention

**AGENTS.md is a skill by the §7.6.1 definition** — procedural knowledge, loaded into context, executing nothing. The convention arrived at substantially the same shape independently, which is corroboration worth acting on.

Full treatment, including the repository's own AGENTS.md and a rule library of roughly 120 candidate rules, lives in `Dami-Core-Operating-Rules.md`. Three architectural consequences belong here.

#### 7.7.1 AGENTS.md is a fourth skill source

`Dami.Capabilities.Skills` registers skills from three places: disk, self-authored, and — added here — **any repository Dami works in**. When the codebase audit service or a development turn operates on a repo, that repo's AGENTS.md loads as procedure for the duration.

Zero new format work, and it means Dami inherits the operating knowledge of any project it touches. Registry entry uses `CapabilitySource.Skill` with a provenance marker identifying the originating repository.

**Trust note:** an AGENTS.md from a repository Dami did not author is observed content. It informs procedure; it does not override policy, approval boundaries, or the egress rules in §5. Same treatment as an untrusted MCP description (§7.6.4).

#### 7.7.2 The stable prompt budget is generous, not tight

Field evidence: the median AGENTS.md in the 100 most-starred repositories that have one runs 1,198 words — roughly 1,600 tokens. One file in ten is under 150 words. Microsoft's vscode ships 33 words and a redirect. Neovim ships 35 words and one rule.

The §9.1 target of ~5,000 tokens for the stable prompt is therefore comfortable rather than aggressive, and progressive disclosure — a small always-loaded core with detail deferred — is already the pattern that large projects converge on. Dami's identity charter should follow the same shape.

#### 7.7.3 Dami maintains an operating manual about itself

The most transferable finding from the field study is a mechanism, not a style. The corpus carries 784 explicit prohibition bullets, and they read as scar tissue: each records a specific mistake an agent already made, written down at the moment of correction.

Dami runs that loop on itself. A correction about *behaviour* (as opposed to fact) produces a proposed line in Dami's own operating manual, in must/always/never voice, annotated with the incident that caused it. This is skill authoring and therefore requires no approval under §7.6.5.

Constraints: the manual is capped, with least-invoked rules retired rather than unbounded growth; it is reviewed quarterly alongside the pushback ledger (§6.3); and registry validation rejects any self-authored rule that weakens an approval boundary, a privacy boundary, or the pushback obligation.

#### 7.7.4 Verification is the gap this closes

Testing and validation is 17.2% of corpus word count and appears in 74% of files. The dominant thing humans write down for agents is *how to prove nothing broke*. §7.6.5 requires tests in a tool proposal but never specified the procedure.

`Dami-Core-Operating-Rules.md` Part V defines seven verification levels — format, unit, contract, round-trip, boundary, replay, chaos — and which changes require which. Staging-registry validation enforces them, and a patch touching `Dami.Privacy` requires the boundary level with no exception path.

---

## 8. Solution structure

```
Dami.sln
│
├─ Dami.Contracts              Events, tool contracts, approval contracts,
│                              memory interfaces. No dependencies.
│
├─ Dami.Core                   Session lifecycle, context assembly,
│                              cancellation, turn orchestration.
│
├─ Dami.Providers              Model adapters. Anthropic, Ollama, others.
│                              Routing policy lives here.
│
├─ Dami.Capabilities           Unified registry. Entry model, semantic
│                              retrieval, bundle expansion, staging registry
│                              for self-authored tools.
│   ├─ Dami.Capabilities.Native   Plugin discovery, attribute contracts,
│   │                             in-process tool execution.
│   ├─ Dami.Capabilities.Mcp      MCP client, server registration,
│   │                             trust levels, schema caching.
│   └─ Dami.Capabilities.Skills   Skill loading, progressive disclosure,
│                                 authoring and revision.
│
├─ Dami.Memory                 Observation corpus, conclusions ledger,
│                              retrieval, supersession, provenance.
│
├─ Dami.Persistence            EF Core / Npgsql, migrations, pgvector access,
│                              event store.
│
├─ Dami.Privacy                Egress client, boundary enforcement,
│                              redaction, classification of payloads.
│
├─ Dami.Proactive              IHostedService base contract, scheduling,
│                              thresholding, surfacing.
│   ├─ Dami.Proactive.Scout        interest discovery
│   ├─ Dami.Proactive.Librarian    local media and file categorization
│   ├─ Dami.Proactive.Reflection   cross-domain weekly pass
│   └─ Dami.Proactive.Audit        pushback ledger review
│
├─ Dami.Domains                Health, workshop, civic, estate, network.
│                              One project per domain if they grow.
│
├─ Dami.Voice                  Wake detection, STT client, TTS client,
│                              barge-in, playback suppression.
│
├─ Dami.Vision                 Local image embedding, captioning,
│                              categorization.
│
├─ Dami.Transport              Frame reader/writer, packet library,
│                              Pipelines-based TCP/UDP connection handling.
│                              Implements ITransport from Dami.Contracts.
│
├─ Dami.Gateway.Cli            Thin terminal client of the local API.
├─ Dami.Gateway.Discord        Personal messaging gateway.
│
├─ Dami.Host                   Composition root. Runs interactive tier.
├─ Dami.Host.Proactive         Composition root. Runs proactive tier.
│                              Separate process, separate failure domain.
│
└─ Dami.Tests
```

The graphical client is a separate repository (React/TypeScript + Tauri, or Avalonia pending the spike).

**Note on `Dami.Host` and `Dami.Host.Proactive` being separate processes:** this is the §3.1 split made concrete. Same solution, same contracts, independent lifecycles.

---

## 9. Programming details

### 9.1 Conventions

- No underscore prefixes on fields. Ever.
- `sealed` by default on concrete types.
- Records for contracts and events; classes for services.
- `IAsyncEnumerable<T>` for streaming, not callbacks.
- Nullable reference types enabled and enforced as errors.
- Every external side effect carries an idempotency key.
- Cancellation tokens threaded through everything, including proactive work.

### 9.2 Core contracts

```csharp
public sealed record ExecutionEvent(
    long Sequence,
    Guid EventId,
    Guid TraceId,
    Guid SpanId,
    Guid? ParentSpanId,
    ExecutionOrigin Origin,
    string ActorId,
    ExecutionEventType Type,
    ExecutionStatus Status,
    DateTimeOffset Timestamp,
    string Label,
    string? PayloadReference,
    IReadOnlyDictionary<string, string>? Metadata);
```

```csharp
public sealed record Conclusion(
    Guid ConclusionId,
    Guid? SupersedesId,
    string Subject,
    string Statement,
    double Confidence,
    ConclusionSource Source,
    IReadOnlyList<Guid> SupportingObservations,
    DateTimeOffset ConcludedAt,
    DateTimeOffset? RetractedAt,
    string? RetractionReason);
```

```csharp
public sealed record PushbackRecord(
    Guid PushbackId,
    Guid TraceId,
    string Challenge,
    string ChallengedAssumption,
    PushbackOutcome Outcome,
    DateTimeOffset OccurredAt,
    string? FollowUpNote);
```

```csharp
public interface IProactiveService
{
    string ServiceName { get; }
    ProactiveCadence Cadence { get; }

    Task<ProactiveResult> RunPassAsync(
        ProactiveContext context,
        CancellationToken cancellationToken);
}

public sealed record ProactiveResult(
    IReadOnlyList<Conclusion> Conclusions,
    IReadOnlyList<Surfacing> Surfacings,
    ProactiveStatus Status);
```

`Surfacing` is deliberately separate from `Conclusion`. Most passes should produce conclusions and no surfacings. That asymmetry is the scarcity principle expressed in the type system.

```csharp
public interface ITransport
{
    ValueTask SendAsync(
        TransportFrame frame,
        CancellationToken cancellationToken);

    IAsyncEnumerable<TransportFrame> ReceiveAsync(
        CancellationToken cancellationToken);
}

public readonly record struct TransportFrame(
    ushort MessageType,
    uint Sequence,
    Guid CorrelationId,
    FrameFlags Flags,
    ReadOnlyMemory<byte> Payload);
```

```csharp
public interface IEgressClient
{
    Task<EgressResponse> SendAsync(
        EgressRequest request,
        CancellationToken cancellationToken);
}
```

Services that must not reach the network simply do not receive this dependency. The boundary is enforced by the composition root, which is auditable in one file.

### 9.3 Retrieval pipeline

```
query
  → embed (local)
  → pgvector ANN, top 50
  → optional relational filter (domain, date, source)
  → cross-encoder rerank (local), top 8
  → provenance attach
  → context assembly
```

Reranking is the largest single quality gain available and it is roughly 40 lines of client code against a local sidecar. Do it early.

---

## 10. Delivery phases

Phases are reordered from the charter. The charter front-loads platform and runtime; this ordering front-loads the thing that is actually novel and actually uncertain.

### Phase 0 — Preserve and instrument
- Verified backups of Hermes state, databases, and the 7,000-memory corpus
- Export the corpus to a portable, schema-explicit format
- Build the 50-query retrieval eval set
- Instrument Hermes and MAI: TTFT, stream duration, tool round-trips per task
- Secret inventory and transfer plan

**Exit:** backups verified, corpus portable, eval set exists, baseline numbers recorded.

### Phase 1 — Host platform
- Debian 13 + Cinnamon, live-boot validation before install
- Btrfs with snapshot tooling, or LVM snapshots
- NVIDIA driver from NVIDIA's repo, `nvidia-smi`, CUDA verified from a pinned container
- .NET SDK from Microsoft's repo, bare metal
- PostgreSQL + pgvector from PGDG, bare metal
- Docker/Podman and `uv` for the inference sidecars only
- SSH and remote access

**Exit:** stable host, GPU compute verified, rollback available.

### Phase 2 — Data foundation
- PostgreSQL with pgvector
- Schema: observation corpus, conclusions ledger, pushback ledger, event store
- Local embedding service running on GPU
- Migrate the 7,000 memories
- Run the eval set; select the embedding model on evidence
- Local reranker service
- Verify the retrieval pipeline end to end

**Exit:** the corpus is queryable, reranked, and measurably better than the eval baseline.

### Phase 3 — Transport and runtime port
- `ITransport` abstraction and `LoopbackTransport` reference implementation
- Frame reader/writer with round-trip property tests over split buffers
- Pipelines-based TCP connection handling; reconnect and heartbeat
- Port the tool registry and on-demand acquisition from MAI
- Unified capability registry: native plugin discovery, semantic retrieval over
  capability descriptions, bundle expansion
- MCP client with server registration and trust levels
- Repository AGENTS.md shipped as the first registered skill
- AGENTS.md registered as a fourth skill source, loading from any repo Dami works in
- Port model routing including the local sidecar
- Session lifecycle, cancellation, streaming
- Execution events with origin discrimination
- CLI gateway
- One complete turn: CLI → runtime → routed model → events → streamed answer

**Exit:** a prompt travels the full path and produces a truthful event trace. Latency compared against the Phase 0 baseline.

### Phase 4 — Privacy boundary and first proactive service
- `Dami.Privacy` egress enforcement, verified by test
- `IProactiveService` contract, scheduling, thresholding
- **Interest scout** running nightly
- Surfacing channel (initially: a queue Steve reads when he wants)
- Feedback capture on every surfacing

**Exit:** Dami surfaces something unprompted that Steve is glad to have received, and the reaction is recorded.

### Phase 5 — Model of Steve
- Conclusions ledger populated from the corpus
- Provenance and supersession working
- End-to-end audit rendering, diffable
- Pushback ledger active
- Identity charter ported; verified stable across two providers

**Exit:** Steve can read what Dami believes about him, correct it, and see the correction take effect.

### Phase 6 — Local vision and the media librarian
- Local vision model: embedding, captioning, categorization
- Image vectors in pgvector
- Propose-only file organization with an approval manifest
- Verified: no image or filename crosses the egress boundary

**Exit:** a photo library is categorized locally and a proposed reorganization is approved and executed safely.

### Phase 7 — Graphical interface
- GUI framework decision (Tauri/React vs Avalonia), driven by recorded events, not synthetic 500-node loads
- SignalR transport
- Conversation view and execution graph
- Proactive traces visible alongside interactive ones

**Exit:** both tiers are legible in one interface.

### Phase 8 — Reflection pass and domain migration
- Domain schemas migrated one at a time, each with contract tests
- Weekly cross-domain correlation pass
- One observation per week, or none

**Exit:** Dami produces an observation connecting two domains that Steve had not connected.

### Phase 8b — Self-improvement
- Skill authoring and revision by Dami, freely, fully logged
- Dami's own operating manual, seeded from real pushback-ledger entries
- Verification protocol wired into staging-registry validation (seven levels)
- Codebase audit as a proactive service; proposes patches, commits nothing
- Staging registry for self-authored tools, with source, tests, and rationale
- Human approval gate for promotion to the live registry

**Exit:** Dami writes a skill that is genuinely useful, proposes a tool that survives review, and adds a rule to its own manual after being corrected.

### Phase 9 — Voice and presence
- PipeWire audio validated
- Wake detection ("Hey Dami", DAH-mee)
- Faster Whisper-class STT
- Cloned TTS with documented consent and rights, GPU-resident
- Barge-in and playback suppression
- Avatar, if still wanted after voice proves itself

**Exit:** one complete spoken cycle, end to end, that is pleasant rather than impressive-once.

### Phase 10 — Gateways, shadow mode, cutover
- Discord gateway
- Shadow mode against Hermes on identical inputs
- Compare task success, latency, tokens, quality
- Single authoritative gateway at cutover
- Hermes and the Mac retained as rollback for at least one week

---

## 11. Open decisions

Closed by this document: store selection (pgvector), memory provider (custom, not Honcho), routing mechanism (ported from MAI), event store canonicality, host OS (Debian 13 + Cinnamon), first proactive service, deployment model (bare metal except inference sidecars), transport (custom TCP/UDP behind `ITransport`).

Still open:

1. Embedding model — decided by the Phase 2 eval, not by argument
2. Payload serialization inside the transport frame — MemoryPack, hand-rolled span writer, or other. Deliberately deferred; framing must not depend on it.
3. GUI framework — Tauri/React vs Avalonia
3. Local sidecar model selection and VRAM budget alongside TTS
4. Surfacing channel behaviour: queue, notification, or held-until-adjacent-opening
5. Confidence threshold for surfacing, and how it self-tunes without gaming itself
6. TTS engine and a legally clean voice source
7. Avatar: whether it serves presence or is a distraction from it
8. Whether the Mac remains permanently as an Apple bridge
9. Event retention and compaction policy
10. Backup destinations, encryption, retention
11. Repository visibility and licensing
12. Which Hermes sessions and skills are worth migrating at all

---

## 12. Risks

**The auditor decays.** Highest-severity risk in the project, and invisible when it happens. Mitigation: pushback ledger, quarterly review, conclusions diff. Detection, not prevention.

**Proactive output becomes noise.** A muse that talks constantly is an infestation. Mitigation: scarcity enforced in the type system, thresholds tuned on recorded reactions, a hard cap per period.

**Background file operations destroy data.** Mitigation: propose-only, approval manifests, dry-run by default, no delete capability at all in v1.

**A self-authored tool does damage.** Arbitrary code, agent privileges, runs unobserved on a schedule. Mitigation: staging registry, mandatory human promotion, tests required in the proposal, and no self-authored tool may hold write or delete capability in v1.

**MCP description injection.** Third-party tool descriptions enter context and shape behavior. Mitigation: explicit trust levels, untrusted descriptions summarized rather than followed, untrusted MCP excluded from turns touching local-only data.

**The privacy boundary erodes through convenience.** One frontier call with profile data attached and the guarantee is gone. Mitigation: egress enforcement in code, composition-root auditability, tests that assert specific services cannot reach the network.

**The GUI consumes the project.** Mitigation: it is Phase 7, it is driven by recorded events, and the CLI must remain fully sufficient.

**The custom transport becomes the project.** Hand-rolled protocols are a well-known time sink, and the edge cases dwarf the happy path. Mitigation: `ITransport` from the first commit, a working `LoopbackTransport` before the real one, and an explicit willingness to ship on a boring transport if the custom one is not ready when the rest of the runtime is. The learning objective is served by building it, not by blocking on it.

**Porting from MAI turns into rewriting from scratch.** The stated goal includes pushing personal capability boundaries, which creates a standing temptation to rebuild solved problems. Mitigation: an explicit port list, and a rule that anything on it must be justified in writing before being rebuilt.

**Latency does not improve.** Possible if the cause is round-trip count rather than prompt weight and the tool surface shrinks without routing improving. Mitigation: Phase 0 instrumentation, so the cause is known before the fix is claimed.

---

## 13. Success definition

Dami Core succeeds when, on an ordinary Tuesday, Dami tells Steve something he did not ask for, did not know, and is glad to have heard — and when Steve can open the ledger, see exactly why Dami thought that, and correct it if it is wrong.

Everything else in this document is infrastructure for that sentence.
