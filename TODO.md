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
- [ ] A5 `apt-mark hold` / controlled-update windows for the NVIDIA driver stack
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
- [ ] B9 Observation retention/compaction policy for `chat`-source growth (register open item)
- [ ] B10 Repair/flag the 267 epoch-zero timestamps (dates sometimes recoverable from body text)

## C · Privacy & egress (D-012)

- [x] C1 `Dami.Privacy`: allowlist egress client, refusal-is-loud, every send/refusal a trace event; composition-root audit point
- [x] C2 Frontier gate ADR-0010: `IFrontierChat`, prompt-never-in-labels, LocalOnly unreachable-by-construction for context-bearing turns
- [x] C3 Subscription frontier ADR-0011: `CodexChatClient` via `codex exec` (browser login, zero API cost), read-only sandboxed, live-verified
- [ ] C4 **Redaction/consent step** so memory-informed prompts can become Egressable deliberately (unlocks frontier `chat`; needs its own ADR — the highest-leverage open design in the suite)
- [~ Claude 2026-08-23] C5 Egress budget/rate alarm (a runaway proactive loop calling frontier nightly should trip something)

## D · Model layer (§7.4)

- [x] D1 Local clients behind contracts: `IEmbeddingClient`, `IRerankClient`, `IChatClient` (Ollama, thinking-mode default documented), `IVisionClient` (qwen2.5vl, 1.4 s warm)
- [x] D2 `IModelRouter` with D-012 as the unconditional first rule; degrade-to-local when no frontier
- [x] D3 Frontier adapters: Codex-subscription (live) + Anthropic (built, dormant)
- [x] D4 Streaming completion contract — `IChatClient.StreamAsync`, Ollama JSONL impl, thinking excluded (tests + live)
- [ ] D5 Cheap-model-assisted routing (replace the static work-kind table when it misroutes in practice)
- [ ] D6 VRAM budget plan for simultaneous residents (embed+rerank+LLM+vision+TTS vs 16 GB) — measure, then pin `[BLOCKED: needs L-phase TTS choice]`

## E · Transport (§7.5) — Codex's lane

- [x] E1 Frame codec (versioned, split-buffer property tests) · `ITransport` · `LoopbackTransport` (ADR-0004/0005)
- [x] E2 `PipeTransport`, `TcpDuplexPipe`, heartbeat (ADR-0006), reconnect/lifetime (ADR-0007), backpressure/failed-flush (ADR-0008)
- [ ] E3 UDP path for voice/avatar frames `[BLOCKED: L-phase]`
- [ ] E4 Payload serialization decision inside frames (MemoryPack vs span-writer — register open item)
- [ ] E5 TLS (`SslStream`) if traffic ever leaves localhost `[BLOCKED: remote-access decision]`

## F · Capability system (D-014/D-015/D-016) — Codex's lane (in progress)

- [x] F1 Unified registry: entries, native plugin discovery, bundle expansion (recent commits: discovery, safe expansion, hardening)
- [~ Codex 2026-08-23] F2 Semantic capability retrieval over the registry (embed descriptions into the existing pgvector store; reuse B4's pipeline)
  - [x] F2a Deterministic registry inventory snapshot for embedding synchronization
  - [x] F2b Derived capability-vector persistence in pgvector, separate from personal observations
  - [~ Codex 2026-08-23] F2c Intent embed → ANN candidates → rerank → bundle expansion
- [ ] F3 MCP client + explicit trust levels; untrusted descriptions summarized-not-followed; untrusted excluded from LocalOnly turns
- [ ] F4 Skills: loading, progressive disclosure, self-authoring (free) with every change an event
- [ ] F5 Tool staging registry: self-authored tools proposed with source+tests+rationale, human promotion gate (D-016)

## G · Interactive runtime

- [x] G1 `TurnRunner`: context → route → local model → traced `UserTurn` answer; `dami chat` live (**the charter's Phase 2 exit, demonstrated**)
- [x] G2 Context assembly (`ContextBuilder`): hard token budget (~2.5k vs Hermes's 90–126k), recency-reserved slots, grounding gate (distance ceiling + explicit emptiness), beliefs-beat-memories under pressure; turns feed the corpus (F-05)
- [x] G3 Streaming turns end to end — `BeginStreamingAsync`/`TurnStream`, trace completes and corpus records when drained, one coalesced ResponseStreaming event, `dami chat` streams live — acceptance item 2 (CLI half)
- [ ] G4 **Sessions**: multi-turn conversation with a recent window in context; start/resume/interrupt/reconnect without duplication — acceptance item 1 (natural Codex continuation from transport; unclaimed)
- [ ] G5 Runtime API on localhost (D-005) so CLI/GUI/voice become thin clients; retire the CLI's direct-store deviation
- [ ] G6 Tool execution in turns: bounded terminal/file ops through the capability registry — acceptance item 4 `[BLOCKED: F1-F2]`
- [x] G7 Approval contract — durable single-resolution approvals (denial cannot become approval, SQL-guarded), `dami approvals/approve/deny`, librarian files an approval per manifest, `ManifestExecutor` runs ONLY Approved manifests (move-only, no overwrite, no delete) — **acceptance item 5 demonstrated live**: 10 real files proposed, approved, organized
- [ ] G8 Workers/sub-agents with child traces and returned evidence — acceptance item 6
- [ ] G9 Frontier-informed turns once C4 exists (redacted context → Egressable)
- [ ] G10 Identity/prompt: port the Dami identity charter into the stable prompt (§9.1); verify identity across local + frontier — acceptance item 9

## H · Proactive tier (D-001/D-019/D-020/D-021) — running unattended

- [x] H1 Runner/scheduler/durable run log; failures contained; hourly systemd tick
- [x] H2 Interest scout (nightly): egress-fetched feeds, locally-scored, **learns from `dami good/bad`** (demonstrated 0.520→0.670)
- [x] H3 Reflection (weekly): corpus → at most one provenance-bearing belief via local LLM; belief-aware (no restatements); RAG across months
- [x] H4 Pushback audit (quarterly, D-011) · media librarian (propose-only, vision-enriched, holds no move/delete code) · embedder (nightly indexing)
- [x] H5 Capped surfacing queue (suppressions stored, not dropped) + feedback capture
- [ ] H6 Scout: feed list & interests are starter guesses — curate `[STEVE: systemctl edit dami-proactive]`
- [ ] H7 Surfacing channel decision: queue vs notification vs held-until-adjacent-opening (register: "shapes the muse more than model choice")
- [ ] H8 Confidence threshold self-tuning from recorded reactions, without gaming itself (register open item)
- [ ] H9 Domain collectors (health, civic, network, estate) — needs K1 first
- [ ] H10 Codebase-audit proactive service (reads repo, proposes patches, commits nothing — D-016)

## I · CLI (18 verbs, on PATH)

- [x] I1 inbox/read/good-bad-meh · beliefs/diff/correct/retract/note · recall/ask/chat/context · frontier · trace/stats/health · caption
- [ ] I2 Rework onto the runtime API when G5 lands (verbs survive; transport changes)
- [ ] I3 Trace tree rendering with span nesting once workers exist (charter §8.1 format)
- [ ] I4 Shell completion + man page (polish, low priority)

## J · GUI (Phase 7)

- [x] J1 Recorded-events spike: `tools/gui-spike/trace-viewer.html` over a real exported trace
- [ ] J2 Framework decision: Tauri/React vs Avalonia — comparative spike per charter §8.3, driven by recorded events `[STEVE: preference input]`
- [ ] J3 Conversation view + live execution graph (needs G5 + a live event feed; proactive traces alongside interactive)
- [ ] J4 Ledger/audit UI: beliefs, diffs, corrections (the CLI verbs, visual)

## K · Domains (Phase 5/8)

- [ ] K1 Domain inventory with Steve: which sources exist for health / civic / network / estate / workshop, and where `[STEVE]`
- [ ] K2 One domain end to end (schema + collector + contract tests + privacy review) — pick the one with the best data
- [ ] K3 Reflection consumes domain rows (the cross-domain join that justifies D-007)
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
- [ ] N3 Enable `CA2254` (structured logging) and decide on `CS1591` (XML docs) — deliberate not-yets in standards §12
- [ ] N4 Scheduler concurrency test flake (Codex's b27f638) — deflake or redesign `[~ Codex implied]`
- [ ] N5 Mutation/property tests for the frame codec and stores (stretch)

---

## Steve's queue (nothing moves these but you)

1. **B6** review the eval sheet → D-010 closes on a table
2. **H6** real interests + feeds for the scout
3. **A7/A6/A4** host-OS ADR · Postgres major · backup destination
4. **B7** Kokoro: import or not
5. **J2** GUI preference input · **L4** voice source · **K1** domain inventory
6. `dami inbox` has pending surfacings; every `good`/`bad` trains the taste model
