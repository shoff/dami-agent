# Dami Core — Status

**The running record of what is done, what is not, and what is waiting on a decision.**
Orientation lives in `docs/onboarding.md`; plans live in the architecture and charter.
This file holds only observed state.

- **Last updated:** 2026-08-23 20:06 CDT (`2026-08-24T01:06Z`)
- **Updated from:** direct workstation inspection and solution test evidence
- **Current phases:** 0, 1, and 3 in progress

> **`docs/workstation-runbook.md` is the operational companion to this file** — service
> inventory, health checks, host-specific traps, and the protocol for working alongside
> another agent. Read its §7 before touching shared state.
>
> **Two warnings about reading this file.**
>
> A second agent is actively provisioning infrastructure on this machine. Anything in
> §3 can be stale within minutes — the PostgreSQL rows changed twice while this file
> was being written. Timestamps are load-bearing here; re-verify before acting.
>
> Every `done` in this file carries the command that proves it. A row without evidence
> is not done, it is believed to be done, and the two are recorded differently on
> purpose. Do not promote a row without running something.

---

## 1. State of play

The repository contains a .NET 10 solution with contracts, transport, analyzer, and
architecture-test foundations. The transport slice has versioned framing,
`ITransport`, loopback delivery, framed pipelines delivery, and one connected TCP
adapter. The workstation has a verified GPU with working container passthrough, local
inference sidecars, and PostgreSQL. Phase 2 application schemas have not started.

Two ADRs are open and both block Phase 2 in the sense that reversing either after data
lands is far more expensive than reversing it now. Neither is mine to accept.

---

## 2. Phase board

Phase order follows the architecture document §10, which supersedes the charter's.

### Phase 0 — Preserve and instrument · *in progress, Mac-side*

| Item | State | Evidence |
|---|---|---|
| Verified backups of Hermes state, databases, corpus | **Weaviate: done 2026-08-23** — all 17 classes, 144k objects with vectors, sha256 manifest, 156 MB at `/home/steve/Data/corpus-export/full`. Non-Weaviate Mac state still open. |
| Corpus exported to portable, schema-explicit format | **done 2026-08-23** | `tools/migration/import_corpus.py`, read-only against the Mac; JSONL + class schema on disk |
| 50-query retrieval eval set built | **37-pair draft generated** — `tools/eval/corpus-queries.draft.jsonl`, paraphrased by the local sidecar, awaiting Steve's review; first baseline run: bge-m3 MRR 0.69 reranked |
| Hermes + MAI instrumented: TTFT, stream duration, tool round-trips | not started | — |
| Secret inventory and transfer plan | not started | — |

Phase 0 runs on the Mac and cannot be advanced from this workstation. **It is not
optional and it is not started.** The eval set gates the D-010 embedding choice, and
the instrumentation gates the claim that Dami Core is faster than Hermes — architecture
§7.3 is explicit that the cause of the 22–35s latency is not yet known.

### Phase 1 — Host platform · *in progress*

| Item | State | Evidence |
|---|---|---|
| Host OS installed and usable | done, but unrecorded | Linux Mint 22.3 running; **no document names this host** — see ADR-0001 |
| Live-boot validation before install | unknown | Not observed by this session; ask Steve |
| GPU driver working on host | done | `nvidia-smi` → RTX 4080, driver 595.84, CUDA 13.2 |
| Snapshot / rollback configured | done | Timeshift RSYNC, daily+weekly+boot, retention 5/3/3; snapshot `2026-08-22_20-17-07`, 22 G, verified to hold `/etc` `/usr` `/var` with `/home` `/root` `/var/lib/docker` excluded |
| Rollback rehearsed | not done — **recommended, not required** | Steve's call, 2026-08-22: does not gate Phase 1. Still a cutover gate via acceptance-suite item 13. Needs a reboot to the live USB. |
| Database backup schedule | done — interim | `dami-pg-backup.timer` nightly at 02:30, `Persistent=true`; every archive verified with `pg_restore --list`, 14-day retention. **A restore was actually performed** and returned schema `dami` and `vector 0.8.6`. See ADR-0003. |
| Off-host / encrypted backup | **not started** | archives sit on the same disk as the database and are unencrypted; `globals-*.sql` holds SCRAM verifiers |
| Container runtime | done | `docker --version` → 29.1.3 |
| GPU passthrough into containers | done | `docker run --rm --gpus all ubuntu:24.04 nvidia-smi` → RTX 4080 visible |
| CUDA compute proven from a pinned container | done | `cuInit()` returns `CUDA_SUCCESS` with `deviceCount=1` inside a container; TEI runs FlashBert on `Cuda(CudaDevice(DeviceId(1)))` |
| .NET SDK | done | `dotnet --version` → 10.0.400 |
| PostgreSQL on bare metal | done | `pg_lsclusters` → `16 main 5432 online`; listening on `127.0.0.1:5432` |
| pgvector extension present | done | `select installed_version ... where name='vector'` → `0.6.0` |
| pgvector usable for the planned corpus | mostly | 0.8.6 + `halfvec` indexes Qwen3-4B (2560d) and BGE-M3; Qwen3-8B (4096d) exceeds the 4000d halfvec ceiling |
| PostgreSQL from PGDG (D-004) | done | `postgresql-16 16.15-1.pgdg24.04+2`, `postgresql-16-pgvector 0.8.6-1.pgdg24.04+1` |
| Least-privilege database roles | done | `dami_ddl` owns schema `dami`; `dami_app` DML only — both verified non-superuser and DDL-denied |
| `uv` for Python sidecars | done | `uv 0.12.5` at `/usr/local/bin`, checksum-verified from the GitHub release |
| Embedding service | done | TEI `89-1.9.0` on GPU, `BAAI/bge-m3`, 1024 dims, ~46 ms per embed |
| Reranker service | done | TEI `89-1.9.0` on GPU, `BAAI/bge-reranker-v2-m3`; both TEI services 3254 MiB |
| LLM sidecar (arch §7.4) | done | `ollama/ollama:0.32.15` on GPU, `qwen3:8b`, 87–128 tok/s warm |
| SSH and remote access | unknown | Not verified by this session |

**Phase 1's stated exit conditions are met:** stable host, GPU compute verified, rollback
available. Live-boot validation and SSH were never observed by this session — ask Steve
rather than assuming — but nothing else blocks the phase.

### Phase 2 — Data foundation · *not started*

| Item | State |
|---|---|
| Schemas: observation corpus, conclusions ledger, pushback ledger, event store | not started |
| Local embedding service on GPU | **done** — TEI `89-1.9.0`, `BAAI/bge-m3`, GPU-resident |
| Migrate the 7,000 memories | **done 2026-08-23** — 6,995 exported from Weaviate on the Mac (portable JSONL + schema in `/home/steve/Data/corpus-export`), imported idempotently, 6,998 vectors indexed under bge-m3 |
| Run the eval, select the embedder on evidence | **harness ready and corpus local** — only the 50-query eval set itself remains, and it is now buildable at this desk. `dami recall` already exposes the ambiguities worth testing (e.g. ML "model" vs scale model). |
| Local reranker service | **done** — TEI cross-encoder on `127.0.0.1:8081` |
| Retrieval pipeline verified end to end | **done** — embed → ANN top-5 → cross-encoder rerank → top-3, reordering confirmed |

### Phase 3 — Transport and runtime port · *in progress*

| Item | State | Evidence |
|---|---|---|
| Versioned frame reader/writer | done | `FrameCodec`; split-buffer round trip at every byte offset plus overflowing/noncanonical varint rejection |
| `ITransport` and `LoopbackTransport` | done | ADR-0005/0007: callers send `TransportMessage`; transports own version, sequence, and async-disposable connection lifetime; bounded snapshotting loopback mirrors wire ordering |
| Pipelines framed connection | done | concurrent sends serialized; cancellation, completion, single receiver, disposal, and backpressured-shutdown tests pass |
| TCP connection, one connection | done | outbound and accepted loopback sockets verified; accepted-socket seam owns the socket, enables `NoDelay`, and has exception-safe idempotent disposal |
| Reconnect, heartbeat, sequence-gap detection | in progress | ADR-0004 sequence checks pass; ADR-0006 heartbeat is complete; ADR-0007 `TcpTransportConnector` creates fresh owned TCP transports with reset per-connection sequence. Transparent replay/session resumption remains deferred pending acknowledgements. |
| Backpressure and flow control beyond bounded loopback | done for TCP v1 | ADR-0008: bounded loopback, awaited pipeline flush, pull-based receive, and TCP windows propagate pressure; failed post-write flush poisons outbound use and requires reconnect; queued cancellation remains safe |
| Capability registry | in progress | Core stable-ID lookup, immutable metadata, tool/skill invariants, and cycle-safe bundle expansion have 18 tests. `Dami.Capabilities.Native` discovers attribute-declared tools without activation (1 test). Semantic retrieval, native execution/host registration, MCP, and skill loading remain. |
| Model routing, sessions, events, CLI | partial | Routed and streaming turns exist. G6c1 adds a provider-neutral bounded model/tool state machine with correlated requested/started/completed/failed events and cancellation; Ollama adaptation, sessions, and the live tool demonstration remain. |

Verification on 2026-08-23 for G6c1, isolated from the concurrent uncommitted G5 host
slice: `dotnet test Dami.sln` executed 417 tests across twelve suites with 0 failures;
the preceding `dotnet build Dami.sln` completed with 0 warnings and 0 errors, and
`dotnet format Dami.sln --verify-no-changes --no-restore` exited 0.

### Phase 4 — Privacy boundary and first proactive service · **largely done**

| Item | State | Evidence |
|---|---|---|
| `Dami.Privacy` egress enforcement, verified by test | done | allowlist + tripwire; refused requests never reach the network (fake handler asserted empty); every send/refusal a durable event in the caller's trace |
| Egress budget/rate alarm (C5) | done | event stream is the meter (refused attempts count); both doors gated; edge-transition surfacing demonstrated live — `refused:` at bound 1, `Egress budget tripped` in the queue, normal traffic unaffected |
| Redaction/consent egress (C4, ADR-0013) | done | `dami brief` → hash-pinned bytes behind a G7 approval → `dami approve` sends byte-exactly through the ADR-0011 door; demonstrated live with the medical history — names stripped, 2,277-char answer recorded |
| `IProactiveService` contract, scheduling, thresholding | done | `ProactivePassRunner` + `ProactiveScheduler` over a durable run log; failures count as runs; one failing service does not stop the rest |
| Interest scout running | done | live pass against the real HN front page: egress → parse → loopback-TEI scoring → 3 surfacings, fully replayable trace |
| Surfacing channel (a queue Steve reads when he wants) | done | `dami inbox` / `read` / `recent`; D-021 cap observed live — a second pass's candidates all `Suppressed`, stored auditable |
| Feedback capture on every surfacing | done | `dami good\|bad\|meh`; **the taste model learns** — an item rated `good` scored 0.520 before feedback, 0.670 after, observed live; bad-penalty > good-boost deliberately |
| Threshold self-tuning without self-gaming (H8) | done | stateless bounded function of recorded reactions; silence moves nothing; minimum-evidence gated; clamp edges pinned by test; register open item closed |
| Codebase audit service (H10, D-016) | done | weekly read-only review of the week's patch via loopback model; ≤1 surfacing with suggested fix; quiet default demonstrated live; commits nothing |
| Pushback audit (D-011) | done | quarterly counter registered in the host; first `SelfAudit` conclusion recorded; quiet without a baseline |
| Ledger readable and correctable (F-09/F-10) | done | `dami beliefs [date]` / `beliefs diff` (as-of reconstruction) / `retract <id> <reason>` / `note`; retraction demonstrated live |
| Beliefs retrieved by similarity (D-009 second half) | done | migration 010 + trigger: retraction deletes the vector atomically; gate calibrated on live bge-m3 distances (relevant 0.40–0.43 vs irrelevant 0.63–0.72 → 0.60); demonstrated: unrelated query carries 0 beliefs, on-topic query exactly the 2 relevant |
| Epoch-zero timestamp repair (B10) | done | append-only-safe sidecar (migration 012) + idempotent scanner: 74/278 dates recovered from body text, 204 flagged; reads and range filters coalesce through repairs; still-undated rows say `undated`, never 1970 |
| D-005 deviation | recorded | the CLI talks to stores until a runtime API exists; noted in `Program.cs` |

**Phase 4 exit** ("Dami surfaces something unprompted that Steve is glad to have
received, and the reaction is recorded"): the machinery is proven — surfacing, reading,
and reaction recording all ran live. Whether Steve is *glad* awaits real use.

### Phases 5–10 · *not started*

Model of Steve at scale (needs the corpus) · vision and media librarian · GUI ·
reflection pass and domains · self-improvement · voice and presence · gateways and
cutover.

---

## 3. Verified host inventory

Captured 2026-08-22 19:49 CDT. Everything here was read off the machine.

### Hardware

| | |
|---|---|
| CPU | Intel Core Ultra 9 285K, 24 cores |
| RAM | 125 GiB |
| GPU | NVIDIA GeForce RTX 4080, **16376 MiB VRAM**, driver 595.84, CUDA 13.2 |
| Root filesystem | `/dev/nvme0n1p3`, ext4, 1.4 T total, 1.3 T free |
| Other OS on this machine | Windows (NTFS) and Fedora (Btrfs) on `nvme1n1`; **multiboot — per-disk confirmation rule is live** |

### Installed

| Component | Version | Source |
|---|---|---|
| Linux Mint | 22.3 "Zena" (Ubuntu 24.04 `noble`), kernel 6.14.0-37-generic | — |
| .NET SDK | 10.0.400 | `/usr/share/dotnet` |
| Docker | 29.1.3 | Ubuntu archive |
| NVIDIA Container Toolkit | **1.20.0-1** | NVIDIA repo; `nvidia` runtime registered in `/etc/docker/daemon.json` |
| git | 2.43.0 | Ubuntu archive |
| gh | 2.98.0 | GitHub's apt repo |
| psql client | 16.15 | Ubuntu archive — **not PGDG** |
| pgAdmin 4 desktop | 9.17 | pgAdmin apt repo (`.../apt/noble`) |
| Timeshift | configured, RSYNC, 1 snapshot (22 G) | Mint default; cron `timeshift-boot`, `timeshift-hourly` |

### Not installed

`uv` · Podman · Ollama · any embedding, reranker, vision, STT, or TTS service.

### Containers and images

**No containers exist.** Both were removed on 2026-08-22:

| Name | Image | Disposition |
|---|---|---|
| `dami-data` | `postgres:latest` | removed by the other agent; Postgres moved to bare metal |
| `dami-pgadmin` | `dpage/pgadmin4:latest` | removed at Steve's request; image deleted too, replaced by the native pgAdmin 4 desktop 9.17 |

Images remaining, all currently unused: `pgvector/pgvector:pg18`, `postgres:latest`,
`ubuntu:24.04`, `hello-world:latest`.

Orphaned pgAdmin state left in place under `/home/steve/Data`:
`pgadmin-dami/` (owned by uid 5050), `pgadmin-servers.json` (connection details only,
no credentials), and `pgadmin-dami.env`, **which does contain credentials** and is now
attached to nothing. Worth deleting.

### Inference sidecars

| | |
|---|---|
| Image (both) | `ghcr.io/huggingface/text-embeddings-inference:89-1.9.0` — **pinned**, digest `sha256:f6b08465…222d9338` |
| Arch | `89` = Ada / sm89, correct for the RTX 4080. `89-1.9.0` is newest; `89-1.10.0` returns 404. |
| `dami-embed` | `127.0.0.1:8080`, `BAAI/bge-m3`, fp16, CLS pooling, 1024 dims, 8192 max input |
| `dami-rerank` | `127.0.0.1:8081`, `BAAI/bge-reranker-v2-m3`, fp16, cross-encoder, 8192 max input |
| `dami-llm` | `127.0.0.1:11434`, `ollama/ollama:0.32.15`, `qwen3:8b`, unloads after `KEEP_ALIVE=5m` |
| VRAM | TEI pair resident **3254 MiB**; `qwen3:8b` adds ~5.6 GiB when loaded → **8865 MiB** with all three, leaving ~7.3 GiB for vision and a resident TTS |
| Latency | 5 sequential single embeds in 0.228 s wall, curl overhead included |
| Restart | both `unless-stopped`; `docker.service` is enabled at boot, so they survive reboot |
| Model cache | `/home/steve/Data/tei-models` — outside the Timeshift snapshot set, and re-downloadable |

**Reranker choice.** `bge-reranker-v2-m3` (~568M) over D-008's other candidate
`Qwen3-Reranker-4B` (~8 GB at fp16). On a 16 GiB card that still has to hold an LLM
sidecar, a vision model, and a resident TTS, the 4B reranker costs roughly half the card
for a stage that reorders 50 candidates. Revisit if the eval shows it earns the VRAM.

**The full §9.3 pipeline is verified end to end**, not merely started. Ten documents
embedded through TEI, written by `dami_ddl` into `vector(1024)`, HNSW-indexed, then for the
query *"which background job connects information from different areas of life?"*:

```
STAGE 1  pgvector ANN top-5 (as dami_app)
  1. reflection pass runs weekly and correlates signals across health, workshop…
  2. cross-domain correlation is the only proactive service that improves…
STAGE 2  cross-encoder rerank to top-3
  1. cross-domain correlation …            score -8.430
  2. reflection pass …                     score -9.008
```

**The reranker reordered the ANN result**, which is the point of having it. Retrieval is
semantic rather than lexical — an earlier probe matched *"weekly cross-domain pass"* to the
reflection-pass sentence with no shared keywords.

**Calibration caveat:** raw cross-encoder scores were around −8 to −9 logits on this
ten-sentence synthetic corpus, i.e. low absolute confidence. Ordering is trustworthy;
absolute values are not, and any surfacing threshold under D-021 must be tuned against the
real corpus rather than these numbers.

**Caveat on BGE-M3:** TEI serves the dense head only. D-010 notes BGE-M3's "native
sparse+dense"; the sparse and ColBERT heads are not available through this service, so
hybrid search must come from tsvector plus reciprocal rank fusion in SQL as D-008 already
specifies.

### The CUDA forward-compatibility trap

**This will recur with every NVIDIA CUDA-based image** — Ollama, the reranker, vision,
STT, and TTS. Recorded here so it is diagnosed once.

TEI started, reported `CUDA_ERROR_SYSTEM_DRIVER_MISMATCH`, and **silently fell back to
CPU** while still answering health checks. The cause:

```
LD_LIBRARY_PATH=/usr/local/cuda/compat:/usr/local/cuda/lib64
/usr/local/cuda/compat/libcuda.so.1 → libcuda.so.575.57.08     (bundled in the image)
host driver / injected libcuda                → libcuda.so.595.84
```

CUDA forward-compatibility libraries exist to run a **new** CUDA runtime on an **old**
driver. This host's driver is newer than the bundled compat lib, so userspace 575 talked
to kernel module 595 and CUDA refused. The image's own `NVIDIA_REQUIRE_CUDA` tops out at
`driver<571`.

**Fix:** `-e LD_LIBRARY_PATH=/usr/local/cuda/lib64`, dropping the compat directory so the
injected host driver is used. Proven with a `cuInit()` probe: `12090`/`cuInit=803` before,
`13020`/`cuInit=0` after.

**The lesson worth keeping:** the container reported healthy and served correct
embeddings the whole time it was on CPU. Every new inference sidecar must be checked for
which device it actually bound to, not merely whether it responds.

### apt repositories

Repaired 2026-08-22. `pgadmin4.list` pointed at suite `zena` — Mint's codename — which
pgAdmin does not publish, giving `404 Not Found` and breaking every `apt-get update`.
Corrected to `noble`. **Any vendor install script using `$(lsb_release -cs)` breaks the
same way on this host and must be given `noble` explicitly — PGDG included.** All seven
repositories now validate with 0 errors and all five referenced keyrings exist.

### PostgreSQL — configured, verified

Completed 2026-08-22 20:13 CDT. **D-004 is now fully met**: bare metal, from PGDG.

| | |
|---|---|
| Cluster | `16 / main`, online, `listen_addresses = localhost`, `127.0.0.1:5432` |
| Server | PostgreSQL **16.15** (`16.15-1.pgdg24.04+2`) — swapped from Ubuntu's build to PGDG's |
| pgvector | **0.8.6** (`0.8.6-1.pgdg24.04+1`), present in both `postgres` and `dami-data` |
| Databases | `postgres`, `dami-data` |
| Schemas in `dami-data` | `dami` (owner `dami_ddl`), `public` |
| Auth | `scram-sha-256`; `log_statement = none`, so role passwords never reach the server log |
| Pre-change dump | `/home/steve/Data/pg-backups/pre-pgdg-20260822.sql` |

**Roles.** Nothing connects as `postgres`. Charter §10.1 requires least privilege:

| Role | Grants | Verified by |
|---|---|---|
| `dami_ddl` | owns schema `dami`; creates tables and indexes | created a `halfvec(2560)` table and an HNSW index on it |
| `dami_app` | `CONNECT`, `USAGE` on `dami`, DML only via default privileges | `INSERT`/`SELECT` succeed; `CREATE TABLE` → `permission denied for schema dami`; `ALTER ROLE … SUPERUSER` → `permission denied to alter role` |

Neither role is superuser, `createdb`, or `createrole`. `PUBLIC` is revoked from the
database and from `CREATE` on `public`. Passwords are generated, never echoed, and live
only in `/home/steve/.pgpass` (mode 0600). **Once a .NET project exists the runtime
connection string belongs in user-secrets**, not a file in the working tree —
`csharpcodestandards.md` §9: configuration and environment variables only.

**Embedding dimension ceilings, measured on this cluster rather than quoted:**

```
vector(2560)   hnsw → ERROR: more than 2000 dimensions
vector(4096)   hnsw → ERROR: more than 2000 dimensions
halfvec(2560)  hnsw → CREATE INDEX                        Qwen3-Embedding-4B  ok
halfvec(4096)  hnsw → ERROR: more than 4000 dimensions    Qwen3-Embedding-8B  fails
```

`vector` stays capped at 2000 dimensions even in 0.8.6; `halfvec` raises it to 4000.
**Qwen3-Embedding-8B cannot be indexed at native 4096 dimensions** and needs Matryoshka
truncation to ≤4000, which D-010 notes the model supports. It is also ~16 GB at fp16 —
the entire card. The eval should treat 8B as requiring truncation, not as a drop-in.

Also gained: `sparsevec`, and iterative index scans, which fix the post-filter shortfall
in the architecture §9.3 retrieval pipeline.

## 4. Waiting on Steve

Nothing below can be settled by inspection. Each blocks work that is expensive to undo.

| # | Question | Blocks | Note |
|---|---|---|---|
| 1 | Accept or reject **ADR-0001** — Linux Mint 22.3 as host, reversing D-003's Debian 13 | Phase 1 close | Reversal is a reinstall now, a data migration after Phase 2 |
| 2 | Rehearse one restore from the live USB — **recommended, not blocking** | acceptance item 13 | Downgraded from a Phase 1 gate by Steve on 2026-08-22. Cheap now while the host carries nothing; the same work against real data in Phase 10. |
| 3 | **Stay on PostgreSQL 16, or move to 17/18?** | Phase 2 schema | PGDG is configured, so either is available. The removed container ran 18.6. Cheap now with two near-empty databases, expensive once the corpus lands. |
| 4 | ~~Which embedding container~~ | — | **Decided 2026-08-22: TEI.** Running on GPU with `bge-m3`; Ollama separate for the LLM sidecar, both now up. |
| 8 | **Local-sidecar accuracy needs thinking mode.** `qwen3:8b` misclassified with `think:false` and was correct with `think:true` (0.02 s vs 3.3 s). | arch §7.4 routing | The cheap path is not the accurate path on this model. Either budget seconds for classification, or few-shot prompt and re-measure. |
| 5 | Split `D-001`…`D-022` into individual ADR files, or leave them in the register | doc hygiene | `CLAUDE.md` says decisions live in `docs/decisions/`; the register is a parallel structure |
| 6 | ~~Retarget `csharpcodestandards.md`~~ | — | **Done 2026-08-22.** Retargeted, and §12 now separates what is a build error from the enforcement gap. |
| 9 | ~~How to enforce SOLID~~ | — | **Closed 2026-08-22.** `Dami.Analyzers` covers `#region`, `dynamic`, method length, loop nesting, optional constructor dependencies, and `NotImplementedException` on interface members; `Dami.Architecture.Tests` covers layering, leaky surfaces, and async contracts. What remains — SRP, OCP, ISP, hot-path LINQ — is not decidable from syntax and is review-only by decision, recorded in standards §12. |
| 7 | Add `apt-mark hold` on the NVIDIA toolkit and driver stack | host stability | ADR-0002 assumes controlled update windows; nothing enforces them yet |
| 11 | **Do the Kokoro classes belong in Dami's corpus?** KokoroMemory (772), KokoroConcept (3,811), KokoroEntity (718) et al. are preserved in the full export but not imported — they look like a different agent system's graph. | corpus completeness | Import needs a judgment about whose memories they are; not inferable from the data. |
| 12 | **Review the draft eval set** — `tools/eval/corpus-queries.draft.jsonl`, 37 pairs with source snippets. Delete bad pairs, add missed relevant ids; then D-010 closes on a re-run. | D-010 | The three-way baseline already ran; bge-m3 leads on every metric. |
| 10 | **Accept or revise ADR-0003.** A verified nightly local dump is in place as an interim measure. Off-host destination, encryption, and a stated recovery objective are still open. | data safety | 14-day retention is arbitrary — no RPO has been stated. Consider PITR before the corpus lands. |

---

## 5. Where the documents and the machine disagree

Kept explicit so nobody discovers these by surprise. Each needs a doc change, a machine
change, or an ADR — not silence.

| Documented | Actual | Status |
|---|---|---|
| Host is Debian 13 + Cinnamon (D-003) | Linux Mint 22.3 + Cinnamon | ADR-0001 proposed |
| Rollback via Btrfs/Snapper or LVM (charter, architecture §10) | ext4 with Timeshift rsync snapshots | **ADR-0002 accepted 2026-08-22**; restore unrehearsed by decision |
| Postgres on bare metal (D-004) | bare metal, cluster `16/main` online | **resolved 2026-08-22** |
| Postgres from the PGDG repository (D-004) | PGDG configured, packages swapped | **resolved 2026-08-22** |
| Retrieval is ANN top-50 then relational filter (arch §9.3) | iterative index scans available in 0.8.6 | **resolved 2026-08-22** |
| Embedding candidate Qwen3-8B (D-010) | 4096d exceeds halfvec's 4000d index ceiling; ~16 GB at fp16 | **open** — needs Matryoshka truncation to be a real candidate |
| Containers are pinned | no containers exist; the two that did used `:latest` | **moot for now** — applies again the moment an inference sidecar is created |
| Embedding candidates are Qwen3-Embedding-4B/8B, BGE-M3 (D-010) | — | 8B at fp16 ≈ 16 GB, which is the entire card. The eval should include smaller variants or 8B is undeployable alongside a reranker, vision, TTS, and an LLM sidecar. |
| Acceptance suite of 14 items (charter §14) | — | Predates the proactive layer and tests none of it: no entry for surfacing quality, scarcity, supersession, pushback rate, or egress enforcement |

---

## 5b. Acceptance suite scoreboard

The charter's fourteen cutover items, scored against what has actually been
demonstrated. "partial" means a real demonstration exists for part of the item's scope.

| # | Item | State | Evidence |
|---|---|---|---|
| 1 | Start/resume/interrupt/reconnect without duplication | partial | append idempotent on `event_id` (tested); no interactive sessions yet |
| 2 | Stream through CLI and GUI | partial | a full turn answers via CLI (`dami chat`), unstreamed; no GUI |
| 3 | Render tools/workers/approvals truthfully | partial | `dami trace` renders only persisted events, child spans indented (§8.1 tree); approvals and workers both exist and appear in traces; no GUI yet |
| 4 | Bounded terminal and file operations | partial | G6a/G6b execute root-confined bounded reads and allowlisted no-shell processes; G6c1 proves a bounded model/tool loop and truthful correlated terminal events in deterministic tests. Ollama integration and the G6d live run remain. |
| 5 | Explicit approval honored | **demonstrated** | G7: durable approval contract, single-resolution in SQL; librarian propose→approve→execute live; C4 egress briefs gated the same way |
| 6 | Worker with child trace and evidence | **demonstrated** | `WorkerRunner`: child span under the parent, hard time bound, failure recorded not thrown; `dami caption` runs vision as a worker — trace replayed with the child span nested |
| 7 | Persist and replay a completed turn | **demonstrated** | proactive passes AND an interactive `UserTurn` (`dami chat` → `dami trace`) persisted and replayed |
| 8 | Recover cleanly from failures | **partial** | provider failure → `TraceFailed`, contained, retried at cadence — demonstrated live twice |
| 9 | Identity across two providers | not yet | one local provider; router exists, frontier doesn't |
| 10 | Relevant memory without flooding the prompt | **demonstrated** | `ContextBuilder` hard budget (2.5k tokens) over embed→ANN→rerank, tested; vs Hermes's measured 90–126k |
| 11 | Discord without duplicate gateways | not yet | — |
| 12 | Materially lower prompt/tool overhead than Hermes | on track | budget enforced at assembly; final claim needs the interactive runtime |
| 13 | Back up and restore runtime + databases | partial | nightly verified pg dumps, one real restore performed; host restore unrehearsed by decision |
| 14 | Spoken wake→STT→agent→TTS cycle | not yet | Phase 9 |

## 6. Next actions, in order

1. **Accept or reject ADR-0001** (host OS). Reversal is a reinstall now and a data
   migration after Phase 2.
3. **Decide the off-host backup destination and encryption.** A verified local copy now
   exists (ADR-0003), but nothing on this host survives the loss of `nvme0n1` — neither
   the dumps nor the Timeshift snapshots. Settle before Phase 2 loads the corpus.
4. **Decide PostgreSQL 16 versus 17/18** while both databases are still near-empty.
5. **Design the Phase 2 schema** — event store, observation corpus, conclusions ledger,
   pushback ledger — as `dami_ddl`. First real use of the database.
6. **Phase 0 on the Mac** — backups, corpus export, eval set, instrumentation. Phase 2
   is blocked on all four regardless of what happens on this workstation.
7. *Recommended, not blocking:* rehearse a Timeshift restore from the live USB.

---

## 7. Keeping this file honest

- Update it in the same commit as the change it describes. A status file that lags is
  worse than none, which is the same failure mode the onboarding document had.
- **A row moves to `done` only with a command and its output.** If it cannot be
  demonstrated, it is `partial` or `unknown` — both are legitimate entries here.
- Record what was observed, not what was intended. When another agent or Steve changes
  the machine, this file records the result; intent belongs in an ADR.
- When an ADR is accepted or rejected, update §4 and §5 in the same commit.
- Distinguish "not started" from "blocked" — blocked names what it waits on.
