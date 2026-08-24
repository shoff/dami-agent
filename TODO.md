# Dami Core — Blueprint & Board

**This file is the kanban.** It is the accurate picture of the end-goal system, what
exists, what does not, and who is working on what. Both agents check this file **before
asking Steve for work**, claim a task by editing it, and update it in the same commit as
the work. It supersedes `docs/ownership.md`'s in-flight table as the claim board.

- Authority on *what to build*: `docs/dami-core-system-architecture.md` + `docs/dami-core-decisions-and-requirements.md` (D-001…D-022) > `docs/dami-core-charter.md`
- Authority on *how*: `AGENTS.md` (TDD, no push without being asked — Codex) · `CLAUDE.md` (build/test gate, no AI attribution)
- History: `docs/work-log.md` · State detail: `docs/status.md` · Ops: `docs/workstation-runbook.md`

## Protocol

- Task states: `[ ]` open · `[~ OWNER since DATE]` claimed/in progress · `[x]` done · `[STEVE]` needs Steve's key/decision · `[BLOCKED: reason]`
- **Claim before you code**: edit this file, set `[~ Codex 2026-08-24]` or `[~ Claude 2026-08-24]`, commit that edit (alone or with the first slice), push.
- **Done means demonstrated**: tests green *and* the behavior observed (`docs/work-log.md` entry with evidence). Then flip to `[x]` in the same commit.
- One task can be split: add sub-bullets with their own ids rather than silently widening scope.
- Shared files (`Dami.sln`, contracts another owner is mid-flight on): pull, stage by path, never `git add -A`.
- If you believe a task is wrong or mis-scoped, don't silently change it — note it under the task and raise it in `work-log.md`.

## The end state (what "finished" means)

One person's assistant, owned end to end: a **continuous modeling system** that runs
when Steve is absent, holds a corrected, provenance-bearing model of him, surfaces
scarce and valuable things unprompted, converses through CLI/GUI/voice/Discord over one
runtime and one durable event stream, does everything personal **locally** on this
workstation, uses the subscription frontier only for egress-safe work, and can be
audited — every belief, every egress, every action — from a single command. Cutover
happens when the fourteen acceptance items (charter §14, scoreboard in
`docs/status.md` §5b) all hold and Hermes can be switched off.

---

## A · Host & infrastructure

- [x] A1 Host validated: Mint 22.3, GPU/CUDA proven in containers, .NET 10, PGDG Postgres 16 + pgvector 0.8.6, least-privilege roles (`dami_ddl`/`dami_app`)
- [x] A2 Inference sidecars pinned & GPU-resident: TEI embed (8080), TEI rerank (8081), Ollama (11434) + `dami-llm-guard` timer for the CPU-fallback failure
- [x] A3 Timeshift snapshots (ADR-0002) · nightly verified pg dumps (ADR-0003) · systemd `dami-proactive` service · `dami` CLI on PATH
- [ ] A4 Off-host, encrypted backup destination + stated RPO (closes the register's backup decision; ADR-0003 is interim-only) `[STEVE: destination choice]`
- [x] A5 `apt-mark hold` / controlled-update windows for the NVIDIA driver stack (29 pkgs held @ 595.84; procedure in runbook §4)
- [ ] A6 PostgreSQL 16 vs 17/18 decision while databases are still small `[STEVE]`
- [ ] A7 ADR-0001 (host OS = Mint) accept/reject `[STEVE]`

## B · Data foundation & memory (D-007/D-009/D-010)

- [x] B1 Schema + checksummed DDL runner: event store (append-only, trigger-enforced), observations, conclusions (supersession), pushbacks, surfacings, runs, embeddings
- [x] B2 All Postgres stores in C# with DB-enforced invariants; 85+ integration tests against real DDL
- [x] B3 Corpus home: 6,995 Hermes memories exported (read-only, checksummed, vectors incl.) + imported idempotently + fully indexed; full 17-class Weaviate preservation (156 MB)
- [x] B4 Semantic pipeline proven end to end: embed → ANN → rerank (§9.3), nightly `EmbedderService`, `dami recall`
- [x] B5 Interim embedder ADR-0009 (bge-m3, versioned per row, re-embed path exercised)
- [ ] B6 **Close D-010**: Steve reviews `tools/eval/REVIEW.md` (37 draft pairs, 13 top-3 misses annotated) → re-run 3-model eval → record decision `[STEVE: review]`
- [ ] B7 Kokoro classes (772 memories / 3,811 concepts / 718 entities): import into corpus or leave preserved? `[STEVE: whose memories are they]`
- [x] B8 Belief embedding: only-active-conclusions embedded for retrieval (D-009 second half; currently beliefs enter context by subject, not similarity)
- [STEVE] B9 Observation retention/compaction policy — ADR-0012 proposed (keep words, reclaim vectors, exclude-never-erase); needs Steve's approval
- [x] B10 Repair/flag the 267 epoch-zero timestamps (278 by repair day; 74 recovered, 204 flagged) 

## C · Privacy & egress (D-012)

- [x] C1 `Dami.Privacy`: allowlist egress client, refusal-is-loud, every send/refusal a trace event; composition-root audit point
- [x] C2 Frontier gate ADR-0010: `IFrontierChat`, prompt-never-in-labels, LocalOnly unreachable-by-construction for context-bearing turns
- [x] C3 Subscription frontier ADR-0011: `CodexChatClient` via `codex exec` (browser login, zero API cost), read-only sandboxed, live-verified
- [x] C4 **Redaction/consent step** (ADR-0013) so memory-informed prompts can become Egressable deliberately (unlocks frontier `chat`; needs its own ADR — the highest-leverage open design in the suite)
- [x] C5 Egress budget/rate alarm (a runaway proactive loop calling frontier nightly should trip something)

## D · Model layer (§7.4)

- [x] D1 Local clients behind contracts: `IEmbeddingClient`, `IRerankClient`, `IChatClient` (Ollama, thinking-mode default documented), `IVisionClient` (qwen2.5vl, 1.4 s warm)
- [x] D2 `IModelRouter` with D-012 as the unconditional first rule; degrade-to-local when no frontier
- [x] D3 Frontier adapters: Codex-subscription (live) + Anthropic (built, dormant)
- [x] D4 Streaming completion contract — `IChatClient.StreamAsync`, Ollama JSONL impl, thinking excluded (tests + live)
- [DEFERRED: correct as-is] D5 Cheap-model-assisted routing — deliberately not built: every interactive turn currently routes LocalOnly (TurnRunner passes LocalOnly), and frontier routing is a C4 *consent* decision, not an automatic route. A cheap-model classifier that auto-picked frontier would fight C4. Revisit only when real misrouting is observed AND frontier turns are routine (G9).
- [ ] D6 VRAM budget plan for simultaneous residents (embed+rerank+LLM+vision+TTS vs 16 GB) — measure, then pin `[BLOCKED: needs L-phase TTS choice]`

## E · Transport (§7.5) — Codex's lane

- [x] E1 Frame codec (versioned, split-buffer property tests) · `ITransport` · `LoopbackTransport` (ADR-0004/0005)
- [x] E2 `PipeTransport`, `TcpDuplexPipe`, heartbeat (ADR-0006), reconnect/lifetime (ADR-0007), backpressure/failed-flush (ADR-0008)
- [ ] E3 UDP path for voice/avatar frames `[BLOCKED: L-phase]`
- [ ] E4 Payload serialization decision inside frames (MemoryPack vs span-writer — register open item)
- [ ] E5 TLS (`SslStream`) if traffic ever leaves localhost `[BLOCKED: remote-access decision]`

## F · Capability system (D-014/D-015/D-016) — Codex's lane (in progress)

- [x] F1 Unified registry: entries, native plugin discovery, bundle expansion (recent commits: discovery, safe expansion, hardening)
- [x] F2 Semantic capability retrieval over the registry (embed descriptions into the existing pgvector store; reuse B4's pipeline)
  - [x] F2a Deterministic registry inventory snapshot for embedding synchronization
  - [x] F2b Derived capability-vector persistence in pgvector, separate from personal observations
  - [x] F2c Intent embed → ANN candidates → rerank → bundle expansion
    - [x] F2c1 Registry snapshot → version-aware capability-vector synchronization
    - [x] F2c2 Intent embed → ANN candidates → rerank → bundle expansion
- [x] F3 MCP client + explicit trust levels; untrusted descriptions summarized-not-followed; untrusted excluded from LocalOnly turns
  - [x] F3a `Dami.Capabilities.Mcp` client boundary: explicit server registration/trust, owned connection lifecycle, tool discovery, and schema cache
  - [x] F3b Secure registry ingestion: stable normalized tools; trusted descriptions admitted verbatim; untrusted descriptions locally summarized with raw text unable to enter retrieval context
  - [x] F3c Privacy-aware selection and execution: untrusted MCP excluded before LocalOnly reranking/expansion; source-neutral MCP invocation dispatch
    - [x] F3c1 Thread privacy classification through capability resolution; exclude untrusted MCP before LocalOnly reranking and related-capability expansion
    - [x] F3c2 Source-neutral MCP invocation registry/dispatcher with cancellation and result/error translation
    - [x] F3c3 D-012 remote Streamable HTTP boundary: ADR plus request-body-capable, event-metered egress transport; default remains loopback-only
      - [x] F3c3a ADR + fail-closed request-body HTTP gate: Egressable context, allowlist, budget, redirects, bounded responses, durable events
      - [x] F3c3b Thread privacy/trace provenance into MCP execution and construct the SDK transport only from the authorized HTTP gate
  - [x] F3d Host composition and local fake-server integration demonstration
    - [x] F3d1 Compose shared native/MCP catalogs, execution dispatch, scoped egress, and owned startup/shutdown lifecycle
    - [x] F3d2 Exercise discovery and invocation end to end against a local Streamable HTTP fake server
- [~ Codex 2026-08-24] F4 Skills: loading, progressive disclosure, self-authoring (free) with every change an event
  - [x] F4a `Dami.Capabilities.Skills` bounded filesystem loading, stable versioning, references, and unified-registry publication
  - [x] F4b Progressive disclosure: selected skill bodies in the bounded turn prompt; bundled files loaded only on demand
    - [x] F4b1 Bounded on-demand skill content reader + one-pass tool/skill selection contract
    - [x] F4b2 TurnRunner prompt budget + Host composition and behavioral demonstration
  - [~ Codex 2026-08-24] F4c Atomic author/revise/retire lifecycle with every diff recorded in the durable execution stream
    - [x] F4c1 Version-pinned lifecycle contract + atomic skill-source snapshot replacement
    - [x] F4c2 Transactional durable diff ledger + execution event write-ahead (migration and least privilege)
    - [~ Codex 2026-08-24] F4c3 Crash-recoverable filesystem materialization + Host/native lifecycle demonstration
      - [x] F4c3a Version-consistent staged filesystem materialization + terminal-event recovery
      - [~ Codex 2026-08-24] F4c3b Native author/revise/retire capability + Host/live lifecycle demonstration
- [ ] F5 Tool staging registry: self-authored tools proposed with source+tests+rationale, human promotion gate (D-016)

## G · Interactive runtime

- [x] G1 `TurnRunner`: context → route → local model → traced `UserTurn` answer; `dami chat` live (**the charter's Phase 2 exit, demonstrated**)
- [x] G2 Context assembly (`ContextBuilder`): hard token budget (~2.5k vs Hermes's 90–126k), recency-reserved slots, grounding gate (distance ceiling + explicit emptiness), beliefs-beat-memories under pressure; turns feed the corpus (F-05)
- [x] G3 Streaming turns end to end — `BeginStreamingAsync`/`TurnStream`, trace completes and corpus records when drained, one coalesced ResponseStreaming event, `dami chat` streams live — acceptance item 2 (CLI half)
- [x] G4 **Sessions**: multi-turn conversation with a recent window in context; start/resume/interrupt/reconnect without duplication — acceptance item 1
  - [x] G4a Durable session/turn contracts + PostgreSQL store with request-id idempotency
  - [x] G4b Session-aware turn runner + bounded recent conversation window in model context
  - [x] G4c Host/CLI start/list/resume/interrupt/reconnect surfaces + live acceptance demonstration
    - [x] G4c1 Session application boundary + Host lifecycle/turn/reconnect API
    - [x] G4c2 Thin CLI session start/list/resume/interrupt/turn/reconnect commands
    - [x] G4c3 Deploy and demonstrate multi-turn context, interruption, resume, and retry convergence live
      - [x] G4c3a Propagate durable session interruption into active turn/model cancellation
- [x] G5 Runtime API on localhost (D-005): `dami-host` service on 127.0.0.1:5810 — turns/SSE, surfacings, beliefs, approvals, traces, `/events`; CLI rework stays I2
- [x] G6 Tool execution in turns: bounded terminal/file ops through the capability registry — acceptance item 4
  - [x] G6a Source-neutral invocation/result contract + native implementation registry and timeout boundary
  - [x] G6b Root-confined file operations + allowlisted no-shell process execution
    - [x] G6b1 Canonical-path/symlink-safe bounded file reading
    - [x] G6b2 Allowlisted executable + `ArgumentList` process execution with bounded output and no shell
  - [x] G6c Model/turn tool loop with truthful events, cancellation, and approval handoff
    - [x] G6c1 Provider-neutral bounded tool-loop state machine + truthful events
    - [x] G6c2 Ollama tool-call adapter with only semantically selected schemas
      - [x] G6c2a Typed source-neutral advertised-tool schema + stable-ID mapping
      - [x] G6c2b Ollama `/api/chat` request/history/parser adapter; send only the supplied selected set
    - [x] G6c3 Approval-gated write/patch handoff through the G7 contract
      - [x] G6c3a Trace-aware capability execution request for approval provenance
      - [x] G6c3b Durable hash-pinned root-confined file-patch proposal + native capability
        - [x] G6c3b1 Immutable file-patch proposal contract + PostgreSQL persistence
        - [x] G6c3b2 Root-confined propose-only native capability + G7 request
      - [x] G6c3c Approved patch executor + open/closed runtime approval dispatch
  - [x] G6d Live bounded terminal/file demonstration + acceptance scoreboard evidence
    - [x] G6d1 Native schema/activation composition + tool-enabled whole-turn runtime
    - [x] G6d2 Deploy and demonstrate bounded read/process/propose/approve behavior live
- [x] G7 Approval contract — durable single-resolution approvals (denial cannot become approval, SQL-guarded), `dami approvals/approve/deny`, librarian files an approval per manifest, `ManifestExecutor` runs ONLY Approved manifests (move-only, no overwrite, no delete) — **acceptance item 5 demonstrated live**: 10 real files proposed, approved, organized
  - [x] G7a Atomically persist `ApprovalRequested`/`ApprovalResolved` trace events with approval transitions (live G6d audit found both enum values unused)
    - [x] G7a1 Add explicit approval origin + optional parent-span provenance through contract, PostgreSQL, and migration 018
    - [x] G7a2 Insert request/resolution events atomically and demonstrate them live
- [x] G8 Workers/sub-agents with child traces and returned evidence — acceptance item 6 (WorkerRunner; vision caption is the first live worker)
- [STEVE] G9 Frontier-informed turns — the mechanism shipped (C4 briefs + server-side execution); what remains is the posture ADR-0013 deferred: should `dami chat` ever offer a brief unprompted? Steve's call
- [x] G10 Identity/prompt: charter reconstructed from migrated identity data (docs/identity/); §9.1 stable block installed at /opt/dami/identity-prompt.md; identity demonstrated across qwen3 + codex — acceptance item 9. SOUL.md reconciles at M4.

## H · Proactive tier (D-001/D-019/D-020/D-021) — running unattended

- [x] H1 Runner/scheduler/durable run log; failures contained; hourly systemd tick
- [x] H2 Interest scout (nightly): egress-fetched feeds, locally-scored, **learns from `dami good/bad`** (demonstrated 0.520→0.670)
- [x] H3 Reflection (weekly): corpus → at most one provenance-bearing belief via local LLM; belief-aware (no restatements); RAG across months
- [x] H4 Pushback audit (quarterly, D-011) · media librarian (propose-only, vision-enriched, holds no move/delete code) · embedder (nightly indexing)
- [x] H5 Capped surfacing queue (suppressions stored, not dropped) + feedback capture
- [x] H6 Scout: interests + feeds derived from the corpus (local-LLM/.NET/vector-search); FeedDelaySeconds fixes hnrss 429 on multi-feed passes
- [STEVE] H7 Surfacing channel decision — ADR-0014 proposed (queue canonical + once-daily presence line on adjacent opening; push rejected as default); needs Steve's accept/reject
- [x] H8 Confidence threshold self-tuning from recorded reactions, without gaming itself (register open item)
- [ ] H9 Domain collectors (health, civic, network, estate) — needs K1 first
- [x] H10 Codebase-audit proactive service (reads repo, proposes patches, commits nothing — D-016)

## I · CLI (18 verbs, on PATH)

- [x] I1 inbox/read/good-bad-meh · beliefs/diff/correct/retract/note · recall/ask/chat/context · frontier · trace/stats/health · caption
- [x] I2 Rework onto the runtime API — every verb through dami-host; approval execution server-side; health/caption stay direct by design
- [x] I3 Trace tree rendering with span nesting (charter §8.1; `dami trace` also resolves 8-char short ids now)
- [x] I4 Shell completion + man page (installed: /etc/bash_completion.d/dami, man 1 dami; sources in tools/cli/)

## J · GUI (Phase 7)

- [x] J1 Recorded-events spike: `tools/gui-spike/trace-viewer.html` over a real exported trace
- [ ] J2 Framework decision: Tauri/React vs Avalonia — comparative spike per charter §8.3, driven by recorded events `[STEVE: preference input]`
- [x] J3 (first cut) Conversation view + live execution graph — web view at http://127.0.0.1:5810/ (SSE chat, /events graph with span nesting, inbox reactions, beliefs); J2's rich client remains open
- [x] J4 Ledger/audit UI: belief correct/retract/diff + health timeline in the web view (the CLI verbs, visual)

## K · Domains (Phase 5/8)

- [ ] K1 Domain inventory with Steve: which sources exist for health / civic / network / estate / workshop, and where `[STEVE]`
- [x] K2 Health domain end to end — schema (014/015) + HealthCollectorService + IHealthEventStore + /health-log + `dami health-log` + privacy review (LocalOnly, no egress path)
- [x] K3 Reflection consumes domain rows — health timeline joins the reflection prompt (D-007 cross-domain correlation); adding a domain now makes reflection strictly better
- [ ] K4 Remaining domains, one at a time

## L · Voice & presence (Phase 9 — after runtime streaming)

- [ ] L1 PipeWire validation: mic/speaker enumeration + capture/playback test `[STEVE: present for audio test]`
- [ ] L2 Wake word "Hey Dami" (DAH-mee) + utterance capture; suppression during playback
- [ ] L3 Local STT (Faster-Whisper-class, pinned container, GPU budget vs D6)
- [ ] L4 TTS engine + **legally clean voice source with documented consent** `[STEVE: voice choice]`
- [ ] L5 End-to-end spoken cycle — acceptance item 14
- [ ] L6 Avatar: decide after voice proves itself (register: may distract from presence)

## M · Gateways, shadow, cutover (Phase 10)

- [ ] M1 Discord gateway (single authoritative instance rule) — acceptance item 11
- [ ] M2 Hermes instrumentation for the §7.3 comparison (TTFT/round-trips) `[BLOCKED: Mac access]`
- [ ] M3 Shadow mode: identical inputs to Hermes and Dami Core, compare
- [ ] M4 Remaining Phase 0: non-Weaviate Mac backups (config, plugins, scripts, launchd inventory) `[BLOCKED: Mac access]`
- [ ] M5 Cutover: Hermes stopped, Dami authoritative, rollback held ≥1 week — acceptance suite complete

## N · Quality & enforcement

- [x] N1 `.editorconfig` + `Directory.Build.props` + banned APIs + `Dami.Analyzers` (6 rules) + architecture tests (layering/leaky-surfaces/async) — all build errors, all verified firing
- [x] N2 12 test suites, ~250 tests, integration against real DDL; build/test gate in `CLAUDE.md`
- [x] N3 Enable `CA2254` (structured logging) and decide on `CS1591` (XML docs) — deliberate not-yets in standards §12
- [ ] N4 Scheduler concurrency test flake (Codex's b27f638) — deflake or redesign `[~ Codex implied]`
- [x] N5 (stores half) Property tests: corpus byte-exact round-trip, append idempotency, replay order, ledger as-of reconstruction — fixed seeds; codec half stays transport-lane
- [ ] N6 Persistence integration fixture isolation: concurrent solution runs currently drop the shared `dami_test` objects and create false cascading failures

---

## Steve's queue (nothing moves these but you)

Decisions with a written proposal attached — read, then accept/reject:
1. **B9** ADR-0012 observation retention (keep words, reclaim vectors, exclude-never-erase)
2. **H7** ADR-0014 surfacing channel (queue canonical, once-daily presence line, no push)
3. **G9** posture: should `dami chat` ever offer a redacted brief unprompted? (mechanism is live: `dami brief`)
4. **A7** ADR-0001 host OS · **A6** Postgres major · **A4** backup destination

Inputs only you have:
5. **B6** review `tools/eval/REVIEW.md` → D-010 closes on a table
6. **B7** Kokoro classes: import or leave preserved (whose memories are they?)
7. ~~**H6** scout feeds/interests~~ — done 2026-08-24: derived from your corpus (6 interests, 2 HN feeds); adjust with `systemctl edit dami-proactive`
8. **G10** the Dami identity charter file is on the Mac and this host has no key —
   `ssh-copy-id steve@192.168.4.23` or copy it into `docs/identity/`, and the port unblocks
9. **J2** GUI framework preference · **L4** voice source · **K1** domain inventory

Daily: `dami inbox` has pending surfacings — including one from the new codebase
auditor lane when it finds something. Every `good`/`bad` trains the taste model
AND now tunes the surfacing threshold (H8).
