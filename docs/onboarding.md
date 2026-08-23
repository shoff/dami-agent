# Dami Core — Agent Onboarding

**Read this first.** It is the fast path to being useful in this repository. The
authoritative long-form document is `docs/dami-core-charter.md`; this file is the
orientation layer on top of it.

- **Last updated:** 2026-08-22
- **Repo:** `/home/steve/dev/dami-agent`
- **Owner:** Steve Hoff
- **Status:** Phase 0/1 — planning and workstation validation. No source code exists yet.

---

## 1. What we are building

**Dami Core** is a purpose-built C#/.NET agent runtime that replaces a heavily
customized Hermes-based system. Dami is a personal assistant with a persistent
identity, durable memory, and domain capabilities that Steve actually uses.

The goal is *ownership and legibility*, not feature count. The current Hermes
system works but is opaque, prompt-heavy, and depends on locally patched
framework internals. Dami Core exists so Steve can understand, debug, and extend
every layer.

Two first-class interfaces sit over one runtime and one event stream:

1. A terminal/SSH CLI — fully usable with no graphical session.
2. A graphical client — conversation plus a live workflow graph of the turn.

The runtime is **trace-first and event-driven**. Every meaningful operation emits
a structured, durable, replayable execution event. CLI and GUI render the same
underlying truth; neither invents its own interpretation of agent activity.

### The measurements that motivated this

These are real numbers from inspecting the running Hermes installation, not
estimates. They are why "lean prompt" is a hard requirement rather than a
preference:

| Observation | Value |
|---|---|
| Input tokens per interactive request | ~90,000–126,000 |
| Long model turn latency | ~22–35 s |
| Fresh background job starting size | ~22,900 input tokens |
| Tools available in registry | 72 |
| Directly serialized tool schemas | 40, totaling 92,462 JSON characters |
| Deferred tool catalog | ~5,114 tokens |
| Distinct tools actually used across 548 logged calls | 23 |
| Share of calls from the top 6 capabilities | 84.7% |
| Share of calls from the top 10 tools | 92.3% |
| Locally patched Hermes core files | 7 (~239 added lines) |

Six capabilities — terminal, skill lookup, session search, file search, file
reading, web search — account for almost all real usage. The framework was
paying for 72 tools to use 23.

**Targets for Dami Core:** stable prompt ≤ ~5,000 tokens before turn-specific
context; tool schemas ≤ ~5,000 tokens per turn where practical.

---

## 2. How Steve works — read this before you write anything

These are working agreements, not style suggestions. Violating them wastes his
time.

- **Never prefix variable names with an underscore.** This applies especially to
  C# private fields. No `_logger`, no `_repository`. Use plain names.
- **Never bullshit him.** If you do not know, say you do not know. If a doc,
  a tool, or a feature does not exist, say so plainly instead of inventing a
  plausible workflow.
- **Do not inflate his ego.** Skip the praise preamble. Answer the question.
- **Challenge assumptions.** If the premise of a request is wrong, say so.
- **Bring receipts.** A correction without evidence is just an opinion. Cite the
  file, the command output, the doc URL, or the measurement.
- Keep responses reasonably concise.

If you disagree with a decision in the charter, argue it with evidence and
propose a decision record. Do not silently implement something different.

---

## 3. Architecture in one screen

```text
                             +-------------------------+
                             |      Dami Identity      |
                             | charter, style, policy  |
                             +------------+------------+
                                          |
+-------------+     +---------------------v---------------------+
| CLI / SSH   |<--->|                   Dami Core                |
| interface   |     | sessions | context | tools | approvals     |
+-------------+     | providers | orchestration | cancellation    |
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
              | events/data|   | live event transport |
              +------------+   +-----------+----------+
                                           |
                              +------------v-------------+
                              | Graphical Dami Interface |
                              | conversation + live graph|
                              +--------------------------+
```

### Planned solution boundaries

```text
Dami.sln
  Dami.Contracts      Dami.Persistence
  Dami.Core           Dami.Automation
  Dami.Orchestration  Dami.Voice
  Dami.Providers      Dami.Gateway.Cli
  Dami.Tools          Dami.Gateway.SignalR
  Dami.Memory         Dami.Worker
                      Dami.Tests
```

The graphical client lives in a separate repository or a clearly separated
application boundary if it uses React/Tauri.

> **Naming note:** this repository is `dami-agent`; the charter names the
> solution `Dami.sln` and the product "Dami Core." That inconsistency is
> unresolved. Ask before renaming either.

### The execution event contract

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

Event types cover the turn lifecycle (`TurnQueued` → `TurnCompleted`/`TurnFailed`/
`TurnCancelled`), context retrieval, capability selection, agent spawn/progress/
completion, tool request/start/complete/fail, approvals, clarifications,
artifacts, and response streaming. Full list is in the charter, §7.2.

Events are durable, append-only, replayable, and idempotent. `ActivitySource`
and OpenTelemetry conventions inform span relationships.

---

## 4. Decisions that are settled

Treat these as fixed unless new evidence overturns them. If evidence overturns
one, write a decision record (see §8).

- Primary runtime is **C#/.NET**. Runtime concerns stay separate from interface concerns.
- Models are **provider adapters**, not the identity owner. The primary Dami
  identity coordinates workers; workers never impersonate the primary agent.
- **Trace-first**: every turn is a root trace; every consequential operation is a span.
- CLI and GUI consume the **same protocol and event stream**. The GUI does not
  scrape terminal output.
- **SignalR/WebSockets** is the leading live transport. **PostgreSQL** is the
  leading durable event and domain store.
- **Never send every tool schema.** A cheap, deterministic capability router
  picks a small bundle per turn.
- The **RTX workstation** is the primary host. **CUDA**, not any Intel NPU, is
  the local inference accelerator unless benchmarks say otherwise.
- Fast-moving AI dependencies live in **pinned containers** or isolated language
  environments. Python uses `uv` where practical.
- **Hermes stays as the reference implementation and rollback path** until Dami
  Core proves parity on what Steve actually uses.
- Existing private domain databases are **not rewritten** to satisfy the new
  harness; the runtime consumes explicit contracts around them.
- Only **one authoritative Discord gateway** runs at a time during cutover.

### Still open — do not assume an answer

Host distro (openSUSE Tumbleweed GNOME leads; Arch/EndeavourOS alternative;
Debian 13 no longer leading) · Avalonia vs. Tauri/React for the GUI · event-store
schema and retention · PostgreSQL topology · memory provider (custom PostgreSQL,
Honcho adapter, or hybrid) · capability-routing mechanism · worker sandboxing ·
remote-access architecture · TTS engine and voice source · licensing and repo
visibility. Full list in the charter, §16.

---

## 5. Hard rules

- **No secrets in this repository**, in prompts, traces, screenshots, logs, or
  command history. Secrets transfer out of band, always.
- **No production data in the first vertical slice.** Do not migrate private
  domain databases to prove a prototype works.
- **Do not copy from macOS directly**: no Python virtualenvs, no Homebrew paths,
  no launchd plists as Linux service definitions, no CoreAudio device IDs.
- **Do not destroy the Mac installation.** It is the rollback.
- **Never format or repartition a disk** without explicit per-disk confirmation
  from Steve.
- **Approvals are real.** Public, destructive, financial, credential,
  permission-changing, and security-sensitive actions require explicit approval.
  Voice-originated commands get the same treatment as typed ones.
- **A reported success must be backed by a verifiable result.** Do not claim an
  external write succeeded without evidence.
- **Do not claim to display chain-of-thought.** The UI shows actions, arguments
  (redacted), progress, sources, artifacts, and errors — not model internals.

---

## 6. Where things currently live

The reference system runs on the Mac. These paths are macOS-side, for inventory
and contract-definition purposes — not copy targets.

| Thing | Path |
|---|---|
| Hermes install/state root | `/Users/steve/.hermes` |
| Hermes source checkout | `/Users/steve/.hermes/hermes-agent` |
| Dami profile root | `/Users/steve/.hermes/profiles/dami` |
| Effective Dami config | `/Users/steve/.hermes/profiles/dami/config.yaml` |
| Dami UI repository | `/Users/steve/dev/dami-ui` |
| macOS wake launch agent | `/Users/steve/Library/LaunchAgents/ai.hermes.dami-wake-desktop.plist` |
| Pre-update Git bundle | `/Users/steve/.hermes/backups/manual-wake-word-20260821/hermes-pre-update.bundle` |
| Pre-update working-tree patch | `/Users/steve/.hermes/backups/manual-wake-word-20260821/hermes-working-tree.patch` |

The Mac may persist as a bounded Apple-services bridge (Pi-hole at
`192.168.4.23` during transition, iMessage/SMS, Notes, Reminders, Find My). It
must not run a second authoritative Discord gateway.

---

## 7. Where we are and what comes next

| Phase | Scope | State |
|---|---|---|
| 0 | Preserve and measure — inventory, verified backups, latency/token baseline | in progress |
| 1 | Workstation platform — live-boot validation, install, NVIDIA, CUDA, .NET SDK | in progress |
| 2 | Runtime vertical slice — one provider, events, CLI streaming, one live graph node | not started |
| 3 | Tool and approval foundation | not started |
| 4 | Identity, memory, continuity | not started |
| 5 | Domain capabilities, one at a time | not started |
| 6 | Voice and local inference | not started |
| 7 | Gateway shadow mode | not started |
| 8 | Controlled cutover | not started |

**The Phase 2 exit condition, verbatim:** one prompt travels through
CLI → runtime → model and appears as a truthful live workflow trace and a final
answer. Nothing else counts as done.

Voice is explicitly *not* in the first vertical slice. It follows verified CLI
and runtime execution.

---

## 8. Working in this repository

- Read `docs/dami-core-charter.md` before proposing architecture. It is long, but
  the open-decisions list (§16) will save you from re-litigating settled ground.
- Material architectural changes get a **decision record** in
  `docs/decisions/`, containing: decision, date, context, alternatives
  considered, evidence, consequences, reversal path.
- Update this onboarding file when a phase advances or an open decision closes.
  A stale orientation doc is worse than none.
- Prefer vertical slices over horizontal layers. The charter's stated risk is
  that Dami Core becomes another large framework before delivering value.
- Build the CLI before the GUI. Drive GUI prototypes from synthetic and recorded
  events, not from a live runtime you have not finished.

---

## 9. Acceptance suite (the real definition of done)

Dami Core is not ready for cutover until it can demonstrate all fourteen:

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
14. Verify one complete spoken wake → STT → agent → TTS cycle before calling
    voice complete.
