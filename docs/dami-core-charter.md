# Dami Core
## Project Charter, Architecture Direction, Migration Plan, and Decision Record

**Prepared for:** Steve Hoff  
**Prepared by:** Dami  
**Date:** 2026-08-22  
**Status:** Planning and initial workstation validation  
**Mission:** Build Dami Core on the RTX workstation

---

## 1. Executive summary

We intend to replace the heavily customized Hermes-based Dami runtime with a purpose-built, understandable, lean C#/.NET agent system called **Dami Core**. The new system will preserve Dami's identity, relationship continuity, memories, tools, data, safety boundaries, and useful domain capabilities while removing unnecessary prompt weight, opaque framework behavior, and dependence on locally patched Hermes internals.

Dami Core will run primarily on Steve's Intel i9/NVIDIA RTX 4080-class workstation. It will provide two first-class interfaces over the same runtime and event stream:

1. A terminal/SSH-oriented CLI for fast, resilient, remote operation.
2. A rich graphical interface centered on conversation and a live workflow graph showing the discrete operations, tool calls, approvals, artifacts, and sub-agent relationships within each turn.

The runtime will be **trace-first and event-driven**. Every meaningful operation will emit a structured, durable execution event. The CLI and GUI will render the same underlying truth rather than maintaining separate interpretations of agent activity.

The current leading host operating-system candidate is **openSUSE Tumbleweed with GNOME**, installed on Btrfs with Snapper rollback. This is provisional until the workstation hardware and live environment are tested. Arch/EndeavourOS remains an alternative; Debian 13 is no longer the leading choice because Dami Core will be an active development and local-AI workstation rather than a fixed-purpose appliance.

The graphical client is not yet finally selected. The leading architecture is an ASP.NET Core backend with SignalR and a React/TypeScript workflow client packaged in Tauri. Avalonia will receive a focused prototype using a modern graph control. Electron is a fallback if its consistent Chromium environment materially outperforms Tauri despite its higher resource footprint.

The migration will be incremental. Hermes will remain the reference implementation and rollback path until Dami Core proves functional parity for the capabilities Steve actually uses. We will not perform a blind copy of the macOS Hermes directory, nor will we destroy the existing Mac installation during initial development.

---

## 2. Why we are doing this

### 2.1 The current problem

Hermes provides broad agent plumbing, but most of what makes Dami personally and operationally useful has been built or configured separately. The present system carries both costs:

- The abstraction, prompt size, updates, and general-purpose surface of Hermes.
- Significant custom engineering, plugins, databases, user interfaces, service configuration, and local Hermes source modifications.

The result is capable but difficult to reason about, expensive in prompt context, vulnerable to upstream changes, and not fully aligned with Steve's preferred C#/.NET development environment.

### 2.2 Measured Hermes costs

A direct inspection of the running installation found:

- Recent interactive Dami requests carried approximately **90,000 to 126,000 input tokens**.
- Long model turns reached approximately **22 to 35 seconds**.
- Prompt-prefix cache use was commonly **96-99%**, which reduces uncached billing but does not make the context lean or simple.
- Some fresh background jobs began near **22,900 input tokens** before accumulating task context and tool results.
- The active configuration exposed 17 built-in toolsets and four Dami plugin toolsets.
- An unscoped tool-registry probe found 72 available tools, 40 directly serialized schemas totaling 92,462 JSON characters, and another 35 deferred tools represented by an approximately 5,114-token catalog.
- In the inspected runtime log, 548 tool calls used only 23 distinct tools.
- Six capabilities—terminal, skill lookup, session search, file search, file reading, and web search—accounted for **84.7%** of calls.
- The ten most-used tools accounted for **92.3%** of calls.
- Seven Hermes core files currently contain local changes, with approximately 239 added lines, principally for secure attachment behavior and wake-word dependency repair.

These measurements support Steve's concern that the framework hammers the model with a disproportionately broad prompt and tool surface.

### 2.3 What Hermes genuinely supplies

Hermes has provided valuable scaffolding and remains useful as a reference implementation. Its major contributions include:

- Model-provider authentication and abstraction.
- Streaming model execution and tool-call orchestration.
- Conversation sessions, persistence, compression, and cancellation.
- Discord and other communication gateways.
- Terminal, process, file, web, browser, computer-use, vision, image, and speech tools.
- Background tasks, cron scheduling, and delegated workers.
- Skills, memory-provider integration, and plugin discovery.
- Attachments, media delivery, logging, installation, and update mechanisms.

Reimplementing these reliably has a real cost. The project must not underestimate that plumbing merely because its APIs are familiar.

### 2.4 What makes Dami useful but is not stock Hermes

The differentiated system consists largely of custom work and private durable data:

- Dami's identity and relationship charter.
- Steve's durable profile and preferences.
- Honcho conclusions and memory behavior.
- Health and nutrition ledgers.
- Civic intelligence.
- Missions and personal radar.
- Scale-model continuity and workshop records.
- .NET estate intelligence.
- Network and Pi-hole investigation tools.
- Dami UI and authenticated prompt handoff.
- PostgreSQL schemas and domain services.
- Custom collectors and scheduled jobs.
- Canonical Dami media and image references.
- Voice and wake configuration.
- Local security, attachment, and dependency fixes.

Dami Core will treat these as portable product assets rather than incidental files trapped inside a framework profile.

---

## 3. Project goals

### 3.1 Primary goals

1. Build a lean C#/.NET runtime that Steve can understand, debug, extend, and own.
2. Preserve Dami as the visible primary identity across models, sessions, interfaces, and worker processes.
3. Reduce fixed prompt and tool-schema overhead dramatically.
4. Select only the capability bundles relevant to each turn.
5. Provide a trustworthy real-time execution graph for tools, workers, approvals, artifacts, failures, and dependencies.
6. Provide both CLI/SSH and graphical interfaces over one runtime contract.
7. Run local wake detection, speech recognition, custom speech synthesis, and selected AI models on the NVIDIA workstation.
8. Preserve privacy, authorization boundaries, confirmations, and auditability.
9. Keep durable state in explicit, backed-up, migration-controlled stores.
10. Migrate incrementally with objective parity tests and a rollback path.

### 3.2 Quality goals

- Stable prompt target: approximately 5,000 tokens or less before turn-specific context.
- Tool-schema target: approximately 5,000 tokens or less per turn whenever practical.
- Context should include a recent conversation window, compact session state, and only relevant retrieved memory.
- The same operation must be represented consistently in logs, CLI, GUI, and persisted events.
- Reconnects must not duplicate or lose messages, steps, approvals, or artifacts.
- Sensitive operations must remain blocked pending explicit approval.
- External writes must be idempotent where feasible and carry evidence of outcome.
- The primary interface must remain personal and conversational; operations must not overwhelm Dami's presence.

### 3.3 Non-goals for the initial vertical slice

The first milestone will not attempt to migrate every Hermes capability. It will not initially include:

- Every messaging platform.
- Every existing skill.
- Full autonomous planning.
- All Apple-only integrations.
- Production voice cloning.
- Complete health, civic, network, and modeling migration.
- A plugin marketplace.
- A general-purpose multi-user agent platform.
- Hidden chain-of-thought exposure.

---

## 4. Decisions already made

The following are current architectural decisions unless later evidence proves them wrong.

### 4.1 Runtime and language

- The primary runtime will be C#/.NET.
- Runtime concerns will be separated from interface concerns.
- Models will be provider adapters, not the identity owner.
- The primary Dami identity will coordinate workers; workers will not replace or impersonate the primary agent.

### 4.2 Trace-first architecture

- Every turn will be a root execution trace.
- Every tool call, retrieval, worker, sub-agent, approval, artifact, and consequential transition will be represented by structured events or spans.
- .NET `System.Diagnostics.ActivitySource` and OpenTelemetry conventions will inform trace relationships.
- Events will carry a turn identifier, span identifier, optional parent span, agent/worker identifier, sequence number, timestamp, status, event type, label, and payload reference.
- Events will be durable, append-only, replayable, and idempotent.
- The UI will show actions and evidence, not private model chain-of-thought.

### 4.3 Shared interface contract

- CLI and GUI will consume the same runtime protocol and event stream.
- The CLI will remain usable over SSH without a graphical session.
- The GUI will not scrape terminal output or wrap the CLI as its primary protocol.
- SignalR/WebSockets are the leading live transport.
- PostgreSQL is the leading durable event and domain-data store.

### 4.4 Context and tools

- Dami Core will not send every available tool schema on every request.
- A deterministic or inexpensive capability router will select a small tool bundle for the turn.
- Domain context and procedural skills will be retrieved only when relevant.
- Stable identity, safety policy, compact user profile, and execution protocol will remain in the stable prompt.
- Tool descriptions will be concise, versioned contracts.

### 4.5 Host and local AI

- The RTX workstation is the intended primary active Dami host.
- NVIDIA CUDA, rather than any Intel NPU, will be the principal local inference accelerator unless later benchmarks show otherwise.
- The host OS will own the kernel, NVIDIA driver, desktop, audio, networking, filesystem, systemd, and container runtime.
- Fast-moving AI dependencies will generally live in pinned containers or isolated language environments.
- Python environments will use `uv` where practical.
- Local wake detection, Faster Whisper-class STT, and custom TTS will remain separate services or components.

### 4.6 Migration discipline

- Hermes remains the reference implementation and rollback system during development.
- Existing private domain databases will not be rewritten merely to satisfy the new harness.
- The new runtime will consume explicit contracts around those stores and services.
- We will not copy macOS virtual environments or launch services directly to Linux.
- Secrets will be transferred separately and never placed in this document, source control, logs, or command history.
- The Discord gateway will run on only one authoritative host during cutover.

---

## 5. Provisional decisions requiring validation

### 5.1 Host operating system

**Current leader:** openSUSE Tumbleweed with GNOME, Btrfs, and Snapper.

Reasons:

- Current kernel, compiler, and desktop packages.
- Packages pass openSUSE automated testing before release.
- Strong Btrfs/Snapper snapshot and rollback integration.
- Official NVIDIA/openSUSE driver path.
- A rolling environment without the full maintenance burden of raw Arch.
- GNOME avoids the current KDE Plasma experience Steve dislikes.

This is not final until a live USB verifies display, NVIDIA behavior, network, webcam, microphone, speakers, sleep/wake, and general desktop comfort.

**Alternative:** EndeavourOS or Arch with GNOME, Btrfs/Snapper, normal and LTS kernels, and `nvidia-dkms`.

Arch provides newer packages, excellent documentation, and AUR access but creates more risk for an always-on agent host. It is acceptable if Steve prefers that control and accepts the maintenance cost. Manjaro is not preferred because delayed repository packages can interact badly with current AUR packages.

**No longer leading:** Debian 13 with Cinnamon. Debian remains technically viable, particularly when development stacks are isolated, but its conservative host packages are less aligned with an experimental AI-development workstation.

### 5.2 Graphical client

**Current leader:** ASP.NET Core + SignalR backend, React/TypeScript graphical client, React Flow/ELK-based workflow graph, packaged with Tauri.

Reasons:

- Rich graph and animation ecosystem.
- Excellent node, edge, layout, minimap, zoom, pan, and drill-down capabilities.
- Smaller deployment than Electron because it uses the system webview.
- Maintains C# ownership of the runtime and domain model.

**Required comparison:** a focused Avalonia prototype using FlowGraph, Nodify.Avalonia, Nodely, or an equivalent graph control.

Avalonia's strengths are a C#-only stack, native .NET data binding, direct hardware/OS access, and lower conceptual complexity. Its risk is the additional work required for a sophisticated animated execution graph.

**Fallback:** Electron if Tauri/WebKitGTK causes unacceptable rendering, debugging, packaging, or cross-platform consistency problems. Electron is not the default because bundling Chromium conflicts with the project's objective of reducing unnecessary runtime weight.

---

## 6. Proposed system architecture

```text
                             +-------------------------+
                             |      Dami Identity      |
                             | charter, style, policy  |
                             +------------+------------+
                                          |
+-------------+     +---------------------v---------------------+
| CLI / SSH   |<--->|                   Dami Core                |
| interface   |     | sessions | context | tools | approvals    |
+-------------+     | providers | orchestration | cancellation   |
                    +-----------+----------------+---------------+
                                |                |
                         execution events        | capability calls
                                |                |
                    +-----------v-----+    +-----v---------------+
                    | Trace/Event Bus |    | Tool & Domain Layer |
                    +---+----------+--+    | files, terminal, web|
                        |          |       | health, models, etc.|
                        |          |       +---------------------+
              +---------v--+   +---v------------------+
              | PostgreSQL |   | SignalR/WebSocket    |
              | events/data|   | live event transport|
              +------------+   +-----------+----------+
                                           |
                              +------------v-------------+
                              | Graphical Dami Interface |
                              | conversation + live graph|
                              +--------------------------+

Local media services:
  wake detector -> STT -> Dami Core -> concise speech response -> TTS

Worker model:
  Dami Core -> bounded worker/sub-agent -> child trace -> evidence/result -> Dami Core
```

### 6.1 Suggested .NET solution boundaries

```text
Dami.sln
  Dami.Contracts
  Dami.Core
  Dami.Orchestration
  Dami.Providers
  Dami.Tools
  Dami.Memory
  Dami.Persistence
  Dami.Automation
  Dami.Voice
  Dami.Gateway.Cli
  Dami.Gateway.SignalR
  Dami.Worker
  Dami.Tests
```

The graphical client should remain a separate repository or clearly separated application boundary if it uses React/Tauri.

---

## 7. Execution event model and workflow graph

### 7.1 Core event fields

A first-pass execution event contract should include:

```csharp
public sealed record ExecutionEvent(
    long Sequence,
    Guid EventId,
    Guid TurnId,
    Guid SpanId,
    Guid? ParentSpanId,
    string AgentId,
    ExecutionEventType Type,
    ExecutionStatus Status,
    DateTimeOffset Timestamp,
    string Label,
    string? PayloadReference,
    IReadOnlyDictionary<string, string>? Metadata);
```

### 7.2 Candidate event types

- TurnQueued
- TurnStarted
- ContextRetrievalStarted
- ContextRetrieved
- CapabilitySelected
- AgentSpawned
- AgentProgressed
- AgentCompleted
- ToolRequested
- ToolStarted
- ToolCompleted
- ToolFailed
- ApprovalRequested
- ApprovalResolved
- ClarificationRequested
- ClarificationResolved
- ArtifactProduced
- ResponseStreaming
- TurnCompleted
- TurnFailed
- TurnCancelled

### 7.3 Graph semantics

- The user request is the root node.
- Each agent or sub-agent appears as a child node or lane.
- Tool calls appear beneath the agent that requested them.
- Dependency edges indicate which output enabled a later operation.
- Parent/child edges indicate delegation and ownership.
- Nodes expose status, duration, retries, evidence, and artifacts.
- Clicking a sub-agent opens its assigned objective, supplied context, timeline, tools, errors, artifacts, and returned result.
- Approvals are first-class blocking nodes, not transient dialog boxes disconnected from the trace.
- Completed turns remain replayable from stored events.
- Streaming token events should be coalesced so the UI does not overwhelm SignalR or animate every token as a separate operation.

### 7.4 Trust boundary

The execution interface will display:

- Requested actions.
- Tool arguments with secrets redacted.
- Observable progress.
- Sources and evidence.
- Artifacts.
- Errors and retries.
- Decisions represented as explicit, user-facing rationale.

It will not claim to display hidden chain-of-thought. Model-internal reasoning is neither a stable API nor an appropriate audit record.

---

## 8. Interface design

### 8.1 CLI/SSH interface

The CLI must be fully usable without the graphical application. It should support:

- Start, resume, list, and interrupt sessions.
- Stream Dami's responses.
- Render execution traces as a tree or timeline.
- Expand tool and worker results.
- Respond to approvals and clarifications.
- Attach files using explicit paths.
- Inspect models, providers, current profile, and runtime health.
- Operate over SSH with durable reconnect behavior.
- Export a turn's trace and artifacts.

Example representation:

```text
* Turn 82 — Inspect repository
|- done  Retrieve relevant memory                   84 ms
|- active Repository analysis
|  |- done  Read solution                           31 ms
|  |- active Run tests                              12.4 s
|  `- queued Review results
`- active Architecture worker
   |- done  Compare storage options
   `- active Prepare recommendation
```

### 8.2 Graphical interface

The GUI should have two centers of gravity.

#### Companion/conversation view

- Dami's presence and identity.
- Conversation transcript.
- Voice state.
- Attachments.
- Approvals and clarifications.
- Important artifacts.
- A clear composer and interruption control.

#### Execution/control view

- Live workflow graph.
- Agent and worker lanes.
- Tool calls and dependencies.
- Sub-agent drill-down.
- Duration, retries, failures, and cancellation.
- Artifact previews.
- Token, latency, and model/provider telemetry.
- Historical replay.

Operational machinery should be one deliberate gesture away without colonizing ordinary conversation.

### 8.3 GUI framework spike

Before committing to Avalonia or Tauri/React, create the same synthetic workload in each:

- At least 500 nodes.
- Multiple concurrent sub-agent branches.
- Live node-state updates.
- Expanding and collapsing subgraphs.
- Zoom, pan, minimap, edge routing, and selection.
- Failure and approval states.
- Artifact drawer.

Measure:

- Frame rate.
- Memory usage.
- Startup time.
- Layout stability.
- Implementation complexity.
- Accessibility.
- Linux packaging reliability.
- Cross-platform behavior.

---

## 9. Prompt, context, memory, and tools

### 9.1 Stable prompt

The stable system prompt should contain only:

- Dami's identity charter.
- Relationship and privacy boundaries.
- Core safety and approval policy.
- Concise execution/tool protocol.
- A compact, stable user profile.
- Current interface and environment metadata.

### 9.2 Retrieved context

Turn-specific retrieval should supply only relevant material:

- Recent conversational window.
- Compact session summary.
- Relevant durable user facts.
- Relevant project or domain state.
- Procedures required for the requested task.
- Current artifacts or source data.

Every retrieved item should carry provenance, confidence, and scope where applicable.

### 9.3 Capability selection

Example bundles:

- Conversation: memory retrieval only.
- Software development: files, terminal, patching, Git, tests.
- Current information: web search and extraction.
- Health: health and nutrition services.
- Scale modeling: vision and workshop services.
- Desktop operation: computer control and approvals.
- Voice: speech input/output adapters.

A turn should not receive unrelated schemas merely because the runtime could theoretically use them.

### 9.4 Memory direction

The durable memory layer must be interface- and model-independent. The final choice among custom PostgreSQL retrieval, Honcho behind an adapter, or a hybrid is unresolved. Regardless of provider:

- Durable facts require provenance.
- Corrections must supersede prior conclusions rather than silently coexist.
- Temporary task state must not pollute permanent identity memory.
- Sensitive data remains local unless explicitly needed by a model request.
- Identity changes must be visible and reversible.

---

## 10. Security and approval model

Dami Core must preserve or improve the current safety boundary.

### 10.1 Principles

- Read-only discovery is generally lower risk than external writes.
- Public, destructive, financial, credential, permission, and security-sensitive actions require explicit policy checks.
- Voice-originated commands receive the same approval treatment as typed commands.
- Secrets are never placed in prompts, traces, screenshots, source control, or email documents.
- Tool arguments and outputs are redacted before persistence where necessary.
- Workers receive least-privilege capability sets.
- Domain database roles follow least privilege.
- External side effects carry idempotency keys where supported.
- A reported success must be backed by a verifiable result.

### 10.2 Approval events

Approvals must have:

- Stable request identifier.
- Turn and span association.
- Human-readable consequential action.
- Scope and affected resource.
- Allowed responses.
- Expiration/cancellation behavior.
- Durable resolution event.

The GUI and CLI must respond through the same approval contract.

---

## 11. Local voice and media architecture

Voice remains layered:

```text
Wake detection
  -> utterance capture
  -> local STT
  -> ordinary authenticated Dami turn
  -> concise speech rendering
  -> custom TTS
  -> playback
```

Requirements:

- Intended wake phrase: **Hey Dami**, pronounced DAH-mee.
- Wake listener pauses or suppresses itself during playback.
- Playback is interruptible.
- Sensitive operations still request approval.
- TTS speaks concise summaries rather than logs, URLs, tables, or source code.
- The custom voice source must have clear consent and usage rights.
- The TTS model should remain resident in VRAM when practical.
- STT/TTS latency, real-time factor, VRAM use, and simultaneous operation must be benchmarked on the actual GPU.

Voice is not part of the first runtime vertical slice; it follows verified CLI/runtime execution.

---

## 12. Migration inventory

### 12.1 Primary source locations

- Hermes installation and state root: `/Users/steve/.hermes`
- Hermes source checkout: `/Users/steve/.hermes/hermes-agent`
- Dami profile root: `/Users/steve/.hermes/profiles/dami`
- Effective Dami configuration: `/Users/steve/.hermes/profiles/dami/config.yaml`
- Dami UI repository: `/Users/steve/dev/dami-ui`
- macOS launch service: `/Users/steve/Library/LaunchAgents/ai.hermes.dami-wake-desktop.plist`
- Pre-update Git bundle: `/Users/steve/.hermes/backups/manual-wake-word-20260821/hermes-pre-update.bundle`
- Pre-update working-tree patch: `/Users/steve/.hermes/backups/manual-wake-word-20260821/hermes-working-tree.patch`

### 12.2 Durable assets to migrate or expose through contracts

- Dami identity charter.
- User profile and durable memories.
- Honcho data and service configuration.
- Session records that remain useful.
- Skills and procedures selected for retention.
- Plugin and domain-service source.
- Mission history and decision trails.
- Health and nutrition ledgers.
- Civic and intelligence data.
- Workshop, model-project, evidence, and inventory records.
- Network collector and investigation data.
- Dami UI source and design assets.
- Canonical Dami images and references.
- Scheduled jobs and scripts.
- Provider and gateway configuration, with secrets transferred separately.

### 12.3 Items not copied directly

- macOS Python virtual environments.
- Homebrew binaries and paths.
- launchd plists as executable Linux service definitions.
- CoreAudio device identifiers.
- macOS permission database state.
- Temporary caches unless a specific artifact is valuable.
- Unverified or duplicated generated files.
- Secrets embedded in configuration exports.

### 12.4 macOS-only capabilities

The Mac mini may remain as a bounded infrastructure or Apple-services bridge for:

- Pi-hole at `192.168.4.23` during the transition.
- iMessage/SMS.
- Apple Notes and Reminders.
- Find My.
- Other macOS-only automation.

The active Dami brain can still run entirely on the workstation while invoking explicitly authorized bridge services. The Mac must not run a second authoritative Discord gateway.

---

## 13. Delivery phases

### Phase 0 — Preserve and measure

- Inventory workstation hardware.
- Inventory current Hermes/Dami files, databases, services, jobs, plugins, and Git state.
- Create cryptographically verified backups.
- Capture a representative task suite and current latency/token baseline.
- Define data ownership and secret-transfer boundaries.

**Exit condition:** verified backups, complete manifest, and reproducible baseline tasks.

### Phase 1 — Workstation platform

- Live-boot openSUSE Tumbleweed GNOME.
- Verify GPU, display, network, webcam, microphone, speakers, sleep, and desktop behavior.
- Install with Btrfs/Snapper if accepted.
- Configure SSH and secure remote access.
- Install NVIDIA driver and verify `nvidia-smi`.
- Prove CUDA access from a pinned container.
- Install current .NET SDK and development tooling.

**Exit condition:** stable workstation with rollback snapshots and verified GPU compute.

### Phase 2 — Runtime vertical slice

- Create solution boundaries and contracts.
- Implement one provider adapter.
- Implement session and cancellation basics.
- Emit structured execution events.
- Persist and replay one turn.
- Implement CLI streaming.
- Implement SignalR event transport.
- Render one live graphical turn.

**Exit condition:** one prompt travels through CLI/runtime/model and appears as a truthful live workflow trace and final answer.

### Phase 3 — Tool and approval foundation

- Add file read/search, terminal/process, patch/write, and web capabilities.
- Add dynamic capability selection.
- Add approval and clarification contracts.
- Add artifact references.
- Add worker/sub-agent execution with child traces.
- Add reconnect, retry, cancellation, and idempotency tests.

**Exit condition:** representative development and research tasks complete safely through both interfaces.

### Phase 4 — Identity, memory, and continuity

- Port the identity charter.
- Implement compact durable user profile.
- Add memory provider abstraction.
- Migrate selected durable memories with provenance.
- Add session summaries and relevant-context retrieval.
- Validate Dami identity across provider/model changes.

**Exit condition:** Steve recognizes continuity without depending on an enormous inherited transcript.

### Phase 5 — Domain capabilities

Migrate or adapt, one domain at a time:

- Missions and radar.
- Health and nutrition.
- Scale-model workshop and continuity.
- Civic intelligence.
- .NET estate.
- Network observability.
- Image generation and media gallery.

Each domain requires contract tests, database migration verification, privacy review, and UI integration.

### Phase 6 — Voice and local inference

- Configure Linux audio through PipeWire.
- Verify microphone and speakers.
- Add wake detection and utterance capture.
- Add Faster Whisper-class STT.
- Prototype legally usable custom TTS.
- Benchmark latency, VRAM, and simultaneous STT/TTS.
- Add barge-in and wake suppression during playback.
- Verify a real end-to-end spoken command.

### Phase 7 — Gateway shadow mode

- Feed representative or duplicated inbound messages to Dami Core without sending responses or performing external writes.
- Compare Hermes and Dami Core using the same model.
- Measure task success, latency, input tokens, tool errors, and quality.
- Resolve behavioral and safety gaps.

### Phase 8 — Controlled cutover

- Stop Hermes writers and Discord gateway.
- Copy final state delta.
- Start Dami Core as the sole authoritative gateway.
- Run acceptance checks.
- Preserve Hermes and the Mac as rollback for at least one week.

---

## 14. Initial acceptance suite

Dami Core is not ready for cutover until it can demonstrate:

1. Start, resume, interrupt, and reconnect a conversation without duplication.
2. Stream a response through both CLI and GUI.
3. Render tool calls, workers, approvals, artifacts, errors, and completion truthfully.
4. Run bounded terminal and file operations.
5. Request and honor explicit approval for a consequential action.
6. Spawn a worker and show its child trace and returned evidence.
7. Persist and replay a completed turn.
8. Recover cleanly from provider, network, tool, and UI failures.
9. Preserve Dami identity across at least two model providers.
10. Retrieve relevant memory without flooding the prompt.
11. Deliver and receive through Discord without duplicate gateways.
12. Maintain materially lower prompt and tool-schema overhead than Hermes.
13. Back up and restore the runtime and its durable databases.
14. Verify one complete spoken wake/STT/agent/TTS cycle before calling voice complete.

---

## 15. Risks and mitigations

### Rolling-distribution/NVIDIA breakage

**Risk:** A kernel or driver update prevents graphical login or CUDA operation.  
**Mitigation:** Btrfs/Snapper, bootable rollback, tested snapshots, pinned GPU containers, and controlled update windows.

### Rebuilding too much infrastructure

**Risk:** Dami Core becomes another large framework before delivering value.  
**Mitigation:** Vertical slices, actual usage data, small capability bundles, and no plugin marketplace or unnecessary abstraction in the initial system.

### GUI consumes the project

**Risk:** Animation and graph polish delay runtime correctness.  
**Mitigation:** Define the event contract first, build CLI first, and drive GUI prototypes from synthetic and recorded events.

### Hidden coupling to Hermes

**Risk:** Valuable data or behavior is discovered only after cutover.  
**Mitigation:** Inventory, representative-task suite, shadow mode, domain-by-domain migration, and retained rollback.

### Memory/identity regression

**Risk:** A lean prompt makes Dami feel generic or forgetful.  
**Mitigation:** Explicit identity charter, provenance-aware durable memory, relevant retrieval, model-independent profile, and direct acceptance testing with Steve.

### Security regression

**Risk:** Custom tools bypass mature approval or credential boundaries.  
**Mitigation:** Policy engine, least privilege, redacted event storage, explicit approvals, idempotency, and adversarial tests before external writes.

### Scope expansion

**Risk:** Every interesting integration becomes a launch requirement.  
**Mitigation:** Preserve the phase gates and migrate only capabilities demonstrated by actual use.

---

## 16. Decisions still open

1. Final host distribution after live testing: openSUSE Tumbleweed, Arch/EndeavourOS, or another candidate.
2. Final desktop session and whether Xorg is required for any computer-control path.
3. Avalonia versus Tauri/React for the primary GUI.
4. Whether Electron is ever justified as a fallback.
5. Exact local API/transport boundary between runtime and GUI.
6. Event-store schema and retention/compaction strategy.
7. PostgreSQL host/container topology.
8. Memory provider: custom PostgreSQL, Honcho adapter, or hybrid.
9. Provider authentication strategy and supported initial model providers.
10. Capability-routing mechanism and failure fallback.
11. Plugin/extension contract and versioning.
12. Worker process isolation and sandboxing.
13. Remote-access architecture.
14. Exact custom TTS engine and legally usable voice source.
15. Whether the Mac remains permanently as an Apple bridge.
16. Which historical Hermes sessions and skills deserve migration.
17. Licensing and repository visibility for Dami Core.
18. Backup destinations, encryption, and retention schedule.

---

## 17. Immediate next steps for tonight

### 17.1 Hardware inventory

Before installing anything, collect:

```bash
nvidia-smi
lscpu
free -h
lsblk -o NAME,SIZE,FSTYPE,MOUNTPOINTS,MODEL
lspci -nnk
ip -brief address
```

Also record:

- Desktop or laptop chassis.
- Exact GPU and VRAM.
- System RAM.
- Disk models and capacities.
- Current UEFI/Secure Boot state.
- Ethernet and Wi-Fi hardware.
- Webcam/microphone and speaker devices.
- Which disks may be erased.

### 17.2 Live environment validation

Boot openSUSE Tumbleweed GNOME without installing and test:

- Native display resolution and refresh rate.
- Basic NVIDIA/display operation.
- Ethernet and Wi-Fi.
- Keyboard and mouse.
- Webcam video.
- Microphone capture.
- Speaker playback.
- Suspend/resume if relevant.
- GNOME usability and general comfort.

### 17.3 Install only after explicit disk confirmation

The installer must not format or repartition anything until the target disk and rollback expectations are explicit.

### 17.4 First software milestone

After platform verification, create the repository and implement:

```text
CLI prompt
  -> Dami Core
  -> one model provider
  -> structured execution events
  -> SignalR
  -> one live graphical node
  -> streamed final answer
```

Do not migrate private production data during this first slice.

---

## 18. Success definition

The project succeeds when Steve has a Dami system he can understand and own, with:

- A lean and explicit prompt architecture.
- Small, relevant tool surfaces.
- Durable and portable identity and memory.
- Honest graphical visibility into agent execution.
- Reliable CLI operation over SSH.
- Rich graphical conversation and workflow exploration.
- Local NVIDIA-accelerated speech and selected inference.
- Preserved domain intelligence and private data.
- Strong approval, privacy, and audit boundaries.
- Lower latency and model-input overhead than the current Hermes system.
- No dependence on a dirty, locally patched general-purpose framework for core identity or operation.

Hermes will have served an important purpose: it provided scaffolding and revealed what Dami actually needs. Dami Core is the deliberate product that follows that discovery.

---

## 19. Reference links

- openSUSE distributions: https://en.opensuse.org/Portal:Distribution
- openSUSE Snapper: https://en.opensuse.org/Portal:Snapper
- openSUSE NVIDIA guidance: https://en.opensuse.org/SDB:NVIDIA_drivers
- Avalonia documentation: https://docs.avaloniaui.net/docs/welcome
- React Flow: https://reactflow.dev/
- FlowGraph for Avalonia: https://prismify-co.github.io/FlowGraph/index.html

---

## 20. Change control

This document records the current plan; it is not immutable. Material architectural changes should be added as explicit decision records containing:

- Decision.
- Date.
- Context.
- Alternatives considered.
- Evidence.
- Consequences.
- Reversal path.

That discipline will keep Dami Core understandable even after months of experimentation.

---

*Source of truth note: this file is the repository copy of the charter held in
the claude.ai project "Dami". The Claude Code CLI cannot read claude.ai project
docs, so this copy exists to make the charter reachable from the terminal. When
one changes, change the other.*
