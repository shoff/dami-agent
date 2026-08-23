# Dami Core — Status

**The running record of what is done, what is not, and what is waiting on a decision.**
Orientation lives in `docs/onboarding.md`; plans live in the architecture and charter.
This file holds only observed state.

- **Last updated:** 2026-08-22 19:54 CDT (`2026-08-23T00:54Z`)
- **Updated by:** Claude Code session, from direct inspection of this workstation
- **Current phase:** 0 and 1, both in progress

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

The repository holds planning documents and no source code. The workstation has a
verified GPU with working container passthrough, a .NET 10 SDK, and Docker. The data
layer is mid-migration and currently not running. Nothing in Phase 2 has started.

Two ADRs are open and both block Phase 2 in the sense that reversing either after data
lands is far more expensive than reversing it now. Neither is mine to accept.

---

## 2. Phase board

Phase order follows the architecture document §10, which supersedes the charter's.

### Phase 0 — Preserve and instrument · *in progress, Mac-side*

| Item | State | Evidence |
|---|---|---|
| Verified backups of Hermes state, databases, corpus | not started | — |
| Corpus exported to portable, schema-explicit format | not started | — |
| 50-query retrieval eval set built | not started | — |
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
| Snapshot / rollback available | **not started** | `timeshift --list` → `Mode: RSYNC`, `No snapshots on this device` |
| Container runtime | done | `docker --version` → 29.1.3 |
| GPU passthrough into containers | done | `docker run --rm --gpus all ubuntu:24.04 nvidia-smi` → RTX 4080 visible |
| CUDA compute proven from a pinned container | **not done** | `nvidia-smi` in a container proves device visibility and driver injection only. No kernel has been launched. |
| .NET SDK | done | `dotnet --version` → 10.0.400 |
| PostgreSQL on bare metal | done | `pg_lsclusters` → `16 main 5432 online`; listening on `127.0.0.1:5432` |
| pgvector extension present | done | `select installed_version ... where name='vector'` → `0.6.0` |
| pgvector usable for the planned corpus | **no** | 0.6.0 caps HNSW at 2000 dimensions; two of three D-010 candidates fail. See §3. |
| PostgreSQL from PGDG (D-004) | **not met** | all packages from `noble/universe`; PGDG repo not configured |
| `uv` for Python sidecars | not installed | `command -v uv` → nothing |
| SSH and remote access | unknown | Not verified by this session |

**Phase 1 cannot close.** Two exit conditions are unmet: rollback is not available, and
CUDA compute has not been demonstrated — only device visibility.

### Phase 2 — Data foundation · *not started*

| Item | State |
|---|---|
| Schemas: observation corpus, conclusions ledger, pushback ledger, event store | not started |
| Local embedding service on GPU | not started — container not chosen, see §4 |
| Migrate the 7,000 memories | blocked on Phase 0 corpus export |
| Run the eval, select the embedder on evidence | blocked on Phase 0 eval set |
| Local reranker service | not started |
| Retrieval pipeline verified end to end | not started |

### Phases 3–10 · *not started*

Transport and runtime port · privacy boundary and interest scout · model of Steve ·
vision and media librarian · GUI · reflection pass and domains · self-improvement ·
voice and presence · gateways and cutover.

No source code exists in this repository. The first code, per architecture §7.5.5, is
the frame reader/writer over `ReadOnlySequence<byte>` with round-trip property tests
across deliberately split buffers, then `ITransport` and `LoopbackTransport`.

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
| Timeshift | present, RSYNC mode, **zero snapshots** | Mint default |

### Not installed

`uv` · Podman · Ollama · any embedding, reranker, vision, STT, or TTS service.

### Containers and images

| Name | Image | State |
|---|---|---|
| `dami-pgadmin` | `dpage/pgadmin4:latest` | up, `127.0.0.1:5050` |
| `dami-data` | was `postgres:latest` | **removed** during this session by the other agent |

Images pulled: `pgvector/pgvector:pg18`, `postgres:latest`, `dpage/pgadmin4:latest`,
`ubuntu:24.04`, `hello-world:latest`.

### PostgreSQL — running on bare metal, but the extension version blocks Phase 2

Confirmed 2026-08-22 19:54 CDT. The container route was abandoned; Postgres now runs
as a host service, which is what D-004 asked for.

| | |
|---|---|
| Cluster | `16 / main`, online, `127.0.0.1:5432` |
| Version | PostgreSQL **16.15** (`16.15-0ubuntu0.24.04.1`) |
| Data directory | `/home/steve/Data/pgsql-dami-data`, owned `postgres:postgres`, mode `0700` |
| pgvector | **0.6.0** (`postgresql-16-pgvector 0.6.0-1`) |
| Package source | `archive.ubuntu.com/ubuntu noble/universe` — **PGDG is not configured** |
| Databases | `postgres` only; no Dami schema yet |

**D-004 is half met.** Bare metal: yes. From PGDG: no. That is not pedantry, because
the repository choice is what pins the extension version, and the extension version is
what blocks the work:

```
$ create index on t1024 using hnsw (v vector_cosine_ops);   -- BGE-M3, 1024 dims
CREATE INDEX
$ create index on t2560 using hnsw (v vector_cosine_ops);   -- Qwen3-Embedding-4B, 2560 dims
ERROR:  column cannot have more than 2000 dimensions for hnsw index
$ create index on t4096 using hnsw (v vector_cosine_ops);   -- Qwen3-Embedding-8B, 4096 dims
ERROR:  column cannot have more than 2000 dimensions for hnsw index
$ select 1 from pg_type where typname='halfvec';            -- added in pgvector 0.7.0
(0 rows)
```

**Consequences, stated plainly:**

- **Two of D-010's three embedding candidates cannot be indexed at native dimension.**
  D-010 requires the embedder be chosen by eval evidence. On 0.6.0 the tooling picks it
  instead, and the answer is BGE-M3 by default rather than by measurement.
- `halfvec` does not exist. Added in 0.7.0, it stores at half precision and raises the
  index dimension ceiling — the direct fix for the errors above, and it halves index
  size on a machine where VRAM and disk both matter.
- **Iterative index scans, added in 0.8.0, are absent.** Architecture §9.3 specifies
  `ANN top-50 → optional relational filter (domain, date, source) → rerank top-8`.
  Pre-0.8 HNSW retrieves k candidates and *then* applies the filter, so a selective
  filter can return far fewer than 50 rows — sometimes near zero. Iterative scans exist
  precisely to fix this. The planned retrieval pipeline is the exact shape that suffers.

**The fix is small.** PGDG builds `postgresql-16-pgvector` at current versions, so
adding the repository upgrades the extension in place with no major-version change and
no data migration. It also brings D-004 into full compliance. Whether to also move to
PostgreSQL 17/18 is a separate and much larger question — the removed container was
18.6, so this cluster is two major versions behind what was briefly running.

Not yet decided, and worth deciding before any schema is created.

---

## 4. Waiting on Steve

Nothing below can be settled by inspection. Each blocks work that is expensive to undo.

| # | Question | Blocks | Note |
|---|---|---|---|
| 1 | Accept or reject **ADR-0001** — Linux Mint 22.3 as host, reversing D-003's Debian 13 | Phase 1 close | Reversal is a reinstall now, a data migration after Phase 2 |
| 2 | Accept or reject **ADR-0002** — Timeshift rsync snapshots for rollback on ext4 | Phase 1 exit | Requires one rehearsed restore before Phase 1 is called done |
| 3 | **Add the PGDG repository and upgrade pgvector past 0.7/0.8?** | Phase 2 schema | Resolves the D-010 blocker and the D-004 shortfall together. In-place extension upgrade, no data migration. Separately: stay on PostgreSQL 16 or move to 17/18? |
| 4 | **Which embedding container** — TEI, Infinity, Ollama, or vLLM | Phase 2 | Options and tradeoffs were presented; recommendation was TEI with Ollama kept separate for the LLM sidecar |
| 5 | Split `D-001`…`D-022` into individual ADR files, or leave them in the register | doc hygiene | `CLAUDE.md` says decisions live in `docs/decisions/`; the register is a parallel structure |
| 6 | Retarget `docs/csharpcodestandards.md` from MAI to Dami | before first code | It still says `MAI.sln`, `MAI.Core`, `MA.RoslynAnalyzers`, `mai_dev`. `MA.RoslynAnalyzers` does not exist for this project. |
| 7 | Add `apt-mark hold` on the NVIDIA toolkit and driver stack | host stability | ADR-0002 assumes controlled update windows; nothing enforces them yet |

---

## 5. Where the documents and the machine disagree

Kept explicit so nobody discovers these by surprise. Each needs a doc change, a machine
change, or an ADR — not silence.

| Documented | Actual | Status |
|---|---|---|
| Host is Debian 13 + Cinnamon (D-003) | Linux Mint 22.3 + Cinnamon | ADR-0001 proposed |
| Rollback via Btrfs/Snapper or LVM (charter, architecture §10) | ext4, no snapshots configured | ADR-0002 proposed |
| Postgres on bare metal (D-004) | bare metal, cluster `16/main` online | **resolved 2026-08-22** |
| Postgres from the PGDG repository (D-004) | `noble/universe`; PGDG not configured | **open** — pins pgvector at 0.6.0 |
| Embedding candidates Qwen3-4B/8B and BGE-M3 (D-010) | pgvector 0.6.0 caps HNSW at 2000 dims; only BGE-M3 fits | **open** — tooling would decide what D-010 says evidence must |
| Retrieval is ANN top-50 then relational filter (arch §9.3) | pre-0.8 pgvector filters after retrieval; no iterative scans | **open** — filtered queries can return far fewer than 50 |
| Containers are pinned | `dpage/pgadmin4:latest` | **unresolved** |
| Embedding candidates are Qwen3-Embedding-4B/8B, BGE-M3 (D-010) | — | 8B at fp16 ≈ 16 GB, which is the entire card. The eval should include smaller variants or 8B is undeployable alongside a reranker, vision, TTS, and an LLM sidecar. |
| Acceptance suite of 14 items (charter §14) | — | Predates the proactive layer and tests none of it: no entry for surfacing quality, scarcity, supersession, pushback rate, or egress enforcement |

---

## 6. Next actions, in order

1. **Add PGDG and upgrade pgvector** (§4 #3). Two of three embedding candidates are
   unindexable until then, and the retrieval pipeline's filter step is degraded.
2. **Accept or reject ADR-0001 and ADR-0002.** Both get more expensive after Phase 2.
3. **Configure Timeshift and rehearse one restore.** Phase 1 cannot close without it,
   and an untested restore is an assumption rather than a rollback path.
4. **Prove CUDA compute from a pinned container** — an actual kernel launch, not
   `nvidia-smi`.
5. **Pick the embedding container** (§4 #4) and stand it up pinned.
6. **Phase 0 on the Mac** — backups, corpus export, eval set, instrumentation. Phase 2
   is blocked on all four regardless of what happens on this workstation.

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
