# Dami Core — Workstation Runbook

**What is running on this machine, how to check it, and the traps that will cost you an
evening if nobody told you.** Written for an agent or a human arriving at this host
without the history.

- **Last updated:** 2026-08-23 10:55 CDT
- **Host:** Linux Mint 22.3 (Ubuntu 24.04 `noble` base), RTX 4080 16 GiB, 125 GiB RAM
- **Companion docs:** `status.md` for what is done, `onboarding.md` for orientation,
  `decisions/` for why

> **More than one agent works in this repository.** Read §7 before you change anything
> shared — the database, `status.md`, or a running service.

---

## 1. Service inventory

Everything binds to **loopback only**. Nothing on this host is reachable from the
network, and that is deliberate — remote access is SSH first, then talk to localhost.

| Service | Address | Container | Image (pinned) | Notes |
|---|---|---|---|---|
| PostgreSQL | `127.0.0.1:5432` | — bare metal | `postgresql-16 16.15-1.pgdg24.04+2` | D-004: not containerised |
| Embeddings | `127.0.0.1:8080` | `dami-embed` | `ghcr.io/huggingface/text-embeddings-inference:89-1.9.0` | `BAAI/bge-m3`, 1024 dims |
| Reranker | `127.0.0.1:8081` | `dami-rerank` | same image | `BAAI/bge-reranker-v2-m3`, cross-encoder |
| LLM sidecar | `127.0.0.1:11434` | `dami-llm` | `ollama/ollama:0.32.15` | `qwen3:8b` pulled |
| pgAdmin | desktop app | — native | `pgadmin4-desktop 9.17` | the container was removed; do not recreate it |
| Proactive tier | systemd `dami-proactive` | — bare metal | published to `/opt/dami/proactive` | hourly tick; five services (scout, reflection, pushback-audit, media-librarian, embedder); config via `systemctl edit dami-proactive`; logs in `journalctl -u dami-proactive` |
| `dami` CLI | `/usr/local/bin/dami` | — native | published to `/opt/dami/cli` | inbox/read/feedback, beliefs/correct/retract, recall, trace, note, health |

All containers are `--restart unless-stopped` and `docker.service` is enabled at boot,
so they return after a reboot without intervention.

**VRAM budget.** 16376 MiB total. TEI embedder + reranker are permanently resident at
**3254 MiB**. `qwen3:8b` adds **~5.6 GiB** while loaded and unloads itself after
`OLLAMA_KEEP_ALIVE=5m`. With all three loaded: **8865 MiB**, leaving ~7.3 GiB for a
vision model and a resident TTS. That is the whole budget — see §4.

### Host packages

| | |
|---|---|
| .NET SDK | 10.0.400 |
| Docker | 29.1.3, NVIDIA Container Toolkit 1.20.0-1, `nvidia` runtime registered |
| NVIDIA driver | 595.84 (CUDA 13.2), open kernel module |
| uv | 0.12.5 at `/usr/local/bin/uv`, checksum-verified from the GitHub release |
| git / gh | 2.43.0 / 2.98.0 |
| Timeshift | RSYNC mode, daily+weekly+boot, retention 5/3/3 |

---

## 2. Database access

| Role | Purpose | Privileges |
|---|---|---|
| `postgres` | administration only | superuser, peer auth over the local socket |
| `dami_ddl` | owns schema `dami`; migrations and DDL | create in `dami` |
| `dami_app` | the runtime | `CONNECT`, `USAGE` on `dami`, DML only |

**Nothing should connect as `postgres`.** Administration from a shell uses peer auth and
needs no password:

```bash
sudo -u postgres psql -d dami-data
```

Application and migration credentials live in `/home/steve/.pgpass` (mode 0600) and
**nowhere else**. `psql -h 127.0.0.1 -U dami_app -d dami-data` picks them up with no
prompt. Once a .NET project exists, the connection string goes in user-secrets, not in a
file in the working tree — `csharpcodestandards.md` §9.

**The database name contains a hyphen.** `dami-data` must be double-quoted in SQL
(`grant ... on database "dami-data"`), though `psql -d dami-data` and .NET
`Database=dami-data` are fine.

**pgvector 0.8.6.** Index dimension ceilings, measured on this cluster:

| Type | HNSW ceiling | Fits |
|---|---|---|
| `vector` | 2000 dims | BGE-M3 (1024) |
| `halfvec` | 4000 dims | Qwen3-Embedding-4B (2560) |
| — | — | Qwen3-Embedding-8B (4096) fits **neither**; needs Matryoshka truncation |

---

## 3. Health check

**`dami health`** does all of this in one command, including the GPU-placement check,
and exits 1 on any failure. The raw commands below remain for when the CLI itself is
suspect.

```bash
# database
sudo -u postgres psql -d dami-data -tAc "select extversion from pg_extension where extname='vector'"   # 0.8.6

# embeddings — expect 1024
curl -s -X POST http://127.0.0.1:8080/embed -H 'Content-Type: application/json' \
  -d '{"inputs":"health check"}' | python3 -c "import sys,json;print(len(json.load(sys.stdin)[0]))"

# reranker — expect an ordered list
curl -s -X POST http://127.0.0.1:8081/rerank -H 'Content-Type: application/json' \
  -d '{"query":"gpu","texts":["cuda kernels","bread recipe"]}'

# llm — expect {"version":"0.32.15"}
curl -s http://127.0.0.1:11434/api/version

# GPU placement — every inference service must say CUDA, not CPU
docker logs dami-embed  2>&1 | grep -iE "model on|Using CPU"
docker logs dami-rerank 2>&1 | grep -iE "model on|Using CPU"
docker exec dami-llm ollama ps          # PROCESSOR column must read 100% GPU
```

---

## 4. Traps on this host

Four things have already cost time here. They will recur.

### 4.1 `lsb_release -cs` returns `zena`, and no vendor publishes it

Mint's codename is `zena`; the Ubuntu base is `noble`. Any vendor install script that
builds a repository URL from `$(lsb_release -cs)` produces a 404 and breaks
`apt-get update` **for every repository**, not just its own. This is exactly how the
pgAdmin repo broke.

**Always hardcode `noble`.** Verify before adding:

```bash
curl -s -o /dev/null -w "%{http_code}\n" https://<vendor>/apt/noble/dists/<suite>/Release
```

PGDG's maintainer scripts additionally print `Unknown distribution ID in
/etc/os-release: linuxmint` and fall back to `ID_LIKE`. Harmless, but expect it.

### 4.2 CUDA forward-compatibility libraries break GPU on this host

NVIDIA CUDA base images ship `/usr/local/cuda/compat/libcuda.so.*` and put that
directory **first** on `LD_LIBRARY_PATH`. Those libraries exist to run a new CUDA
runtime on an **old** driver. This host's driver (595.84) is *newer* than the bundled
compat lib, so userspace meets a newer kernel module and CUDA refuses with
`CUDA_ERROR_SYSTEM_DRIVER_MISMATCH`.

**Fix, required for every TEI-style CUDA image:**

```
-e LD_LIBRARY_PATH=/usr/local/cuda/lib64
```

Not every image has the problem — `ollama/ollama` does not. Check before assuming:

```bash
docker run --rm --entrypoint bash <image> -c 'echo $LD_LIBRARY_PATH; ls -d /usr/local/cuda*/compat 2>/dev/null'
```

To prove CUDA genuinely works inside a container, initialise a context rather than
running `nvidia-smi` — `nvidia-smi` uses NVML and succeeds even when CUDA is broken.

### 4.3 A healthy endpoint tells you nothing about the device

TEI hit 4.2, **fell back to CPU, logged one warning, reported healthy, and served
correct embeddings.** Nothing downstream would have noticed except the latency.

**Every new inference sidecar gets its device binding checked explicitly** (§3). Do not
infer GPU use from a 200 response.

### 4.4 systemd quoting and working directory

Two things the `dami-proactive` install hit. `Environment=` values containing spaces
must be quoted or systemd splits them into nonsense assignments. And
`WorkingDirectory=` is load-bearing for .NET hosts: without it the content root is `/`
and `appsettings.json` silently does not load.

Also observed twice now (§4.3's rule proving itself): **`qwen3:8b` silently fell to
100% CPU** at 2 tok/s with 13 GiB of VRAM free — `docker exec dami-llm ollama ps` is
the check, a container restart the fix.

### 4.5 `/home` is excluded from Timeshift, and that includes the database

Timeshift covers the host, not the data. The cluster's data directory is
`/home/steve/Data/pgsql-dami-data`, which is **not** in any snapshot. That is correct —
an rsync copy of a live data directory is not a backup — but it means **the database has
no recurring backup at all** right now. One manual dump exists at
`/home/steve/Data/pg-backups/`. Backup destination, encryption, and retention are still
open decisions.

Snapshots also live at `/timeshift` on the same physical device as root: they protect
against a bad update, not against drive failure.

---

## 5. Measured performance

Recorded so nobody re-derives them, and so regressions are visible.

| Measurement | Value |
|---|---|
| Embedding, 5 sequential single requests | 0.228 s wall total, curl overhead included |
| `qwen3:8b` generation, warm | 87–128 tok/s |
| `qwen3:8b` **first ever** load | ~6 min — one-time CUDA kernel compilation |
| `qwen3:8b` subsequent cold load | **10.6 s** |
| TEI embedder + reranker resident | 3254 MiB VRAM |
| `qwen3:8b` loaded | +5.6 GiB |

**Two things worth knowing before designing against this.**

`OLLAMA_NUM_PARALLEL=1`, so requests serialise. A slow request blocks the queue — an
early runaway generation made an unrelated 2-token request take 40 s.

**Thinking mode changes correctness, not just latency.** Classifying *"Primed the
fuselage halves and let them cure overnight"* into health/workshop/code/civic:

| Mode | Answer | Cost |
|---|---|---|
| `"think": false` | `code` — **wrong** | 0.02 s, 2 tokens |
| `"think": true` | `workshop` — correct | 3.3 s, 303 tokens |

Architecture §7.4 routes "simple classification, summarization, categorization" to the
local sidecar. On this model that work needs thinking enabled to be correct, so budget
seconds rather than milliseconds — or use few-shot prompting and re-measure. Do not
assume the cheap path is accurate.

---

## 6. Conventions

- **Pin every image to a semver tag**, never `latest`. Record the digest.
- **Bind services to `127.0.0.1`.** Exposing anything beyond loopback is a separate
  decision with its own auth design, and it has not been made.
- **Model caches go under `/home/steve/Data/`** — outside the snapshot set, and
  re-downloadable.
- **Secrets never enter the repository.** `.gitignore` covers `*-environment.md`,
  `.env*`, `appsettings.*.local.json`, `.pgpass`, `secrets.json`, and key material.
  That is a safety net, not permission — credentials belong outside the working tree.
- **Evidence or it did not happen.** A `done` row in `status.md` carries the command
  that proves it. `partial` and `unknown` are legitimate entries.
- **No AI attribution in commit messages, PR descriptions, or tags.**

---

## 7. Working alongside another agent

More than one agent operates in this repository and on this host, on deliberately
separate tasks. **`AGENTS.md` is the authority on development method** — strict TDD,
SOLID, and the rule that a test written after its implementation is coverage, not TDD.
**`docs/work-log.md` is the append-only record of who did what.** This section covers
only the operational collisions those two do not.

**Division of labour so far.** Codex owns the .NET solution under `Dami/` — the
transport slice per architecture §7.5.5. This session owned host infrastructure:
Postgres, GPU, the inference sidecars, and this runbook. Confirm current ownership in
`status.md` §4 and `work-log.md` before assuming it still holds.

**Before you start**

1. `git pull --rebase` — this repo moves in small, frequent commits.
2. Read `status.md` §4 and §6, and the tail of `work-log.md`.
3. Log planned work in `work-log.md` before changing production code, per `AGENTS.md`.

**Shared state, in order of collision risk**

- **The working tree.** `git add -A` will sweep up another agent's in-flight edits. It
  has already happened once here: commit `7d3b508` accidentally captured Codex's
  `Dami.sln`, `Dami.csproj`, and `Program.cs` under an unrelated message. **Stage
  explicitly by path.** Run `git status --short` first and recognise what is not yours.
- **`status.md`.** Keep edits surgical and scoped to the rows you are changing. Update
  it in the same commit as the change it describes. Record *state*; who-did-what goes in
  `work-log.md`.
- **The database.** Schema `dami` is owned by `dami_ddl`. Coordinate before creating or
  dropping objects; drop your scratch tables when you are done.
- **Running services.** Do not `docker run` something that already exists — check
  `docker ps -a`. Do not reconfigure a service you did not stand up without recording
  it; the flags here exist for reasons that are invisible from the `docker run` line
  alone (§4.2 especially).

**File ownership.** Agents here run as `root` while the repository belongs to `steve`.
Files created by an agent land root-owned and Steve then cannot edit them. After
creating files, run:

```bash
chown -R steve:steve /home/steve/dev/dami-agent
```

**When you finish**

- Commit and push promptly if you have been asked to. Long-lived local work is how two
  agents diverge. Note that `AGENTS.md` tells agents not to commit or push unless
  explicitly asked — check which applies to you.
- Update `status.md` and this runbook if you changed what is running or learned a new
  trap. A stale runbook is worse than none — the same failure `onboarding.md` had.
