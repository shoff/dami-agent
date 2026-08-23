# Dami Core — Work Log

This is the append-only record of implementation activity. `docs/status.md` records
current state; this file records who performed each action and what evidence was
observed.

## 2026-08-22 — Codex — Initial transport slice started

### Request and plan

- Steve authorized starting the C# project from the documented project plan.
- Planned the first vertical slice as `Dami.Contracts`, `Dami.Transport`, and
  `Dami.Transport.Tests`.
- Rechecked the filesystem and Git state before editing. The existing placeholder
  application had already been moved from `Dami/Dami/` to `Dami/src/Dami/`, with a
  matching test project under `Dami/tests/Dami.Tests/`. Those concurrent changes were
  preserved.

### Files added

- `Dami/src/Dami.Contracts/Dami.Contracts.csproj`
- `Dami/src/Dami.Contracts/Transport/FrameFlags.cs`
- `Dami/src/Dami.Contracts/Transport/TransportFrame.cs`
- `Dami/src/Dami.Contracts/Transport/ITransport.cs`
- `Dami/src/Dami.Transport/Dami.Transport.csproj`
- `Dami/src/Dami.Transport/Framing/FrameCodec.cs`
- `Dami/src/Dami.Transport/LoopbackTransport.cs`
- `Dami/tests/Dami.Transport.Tests/Dami.Transport.Tests.csproj`
- Transport framing and loopback tests under `Dami/tests/Dami.Transport.Tests/`

### Files modified

- `Dami/Dami.sln` — preserved the existing placeholder entries and added the three
  transport-slice projects beneath the existing `src` and `tests` solution folders.

### Design choices

- Framing and payload serialization are separate. The frame codec treats payloads as
  opaque bytes.
- Frames carry an explicit protocol version, message type, sequence, correlation ID,
  flags, and payload.
- Multi-byte values use network byte order.
- Frame bodies are length-prefixed with an unsigned varint and capped at 16 MiB.
- Incomplete input is not consumed.
- `LoopbackTransport` uses a bounded channel so the reference transport exercises
  backpressure rather than hiding it.

### Process deviation

- The tests and implementation were added in the same edit before Steve reiterated
  that strict, demonstrable TDD is mandatory. This work is test-covered but was not
  performed with an observed red phase and must not be represented as true TDD.
- No build or test result has yet been claimed for this slice.

## 2026-08-22 — Codex — Development rules recorded

- Steve made strict TDD, SOLID design, and comprehensive agent work logging mandatory.
- Added the root `AGENTS.md` to make the red/green/refactor sequence and logging
  convention explicit for all agents.
- Created this work log rather than using `docs/status.md`, because status is a state
  record and was already being actively maintained by another agent.

## 2026-08-22 — Codex — Transport test discovery correction

- Ran `dotnet test tests/Dami.Transport.Tests/Dami.Transport.Tests.csproj --no-restore
  --verbosity normal` from `Dami/`.
- Observed: build succeeded with 0 warnings and 0 errors, but the command displayed no
  test discovery or execution result. This was not accepted as a passing test run.
- Ran the same project with `--list-tests --verbosity diagnostic`.
- Diagnostic evidence: `Skipping running test ... add
  <IsTestProject>true</IsTestProject> property to project file.`
- Added the missing `IsTestProject` property. This is test-runner configuration, not a
  production behavior change.
- The next run correctly invoked the test target but failed with `NETSDK1004` because
  the new test project had not been restored. No test executed.
- Ran `dotnet restore tests/Dami.Transport.Tests/Dami.Transport.Tests.csproj
  --verbosity minimal`. Restore succeeded for Contracts, Transport, and Transport.Tests.
- Reran the narrow suite. Compilation failed with two `CS0103` errors because
  `TestContext` is not available in the declared xUnit 2.9.3 package.
- Replaced the xUnit 3-only cancellation-token access in the loopback test with
  `CancellationToken.None`. Production code was not changed.
- Reran the narrow suite. Six tests executed: four passed and two framing round-trip
  tests failed. The reported scalar fields and payload bytes were identical, but
  `TransportFrame` record equality compared `ReadOnlyMemory<byte>` by backing-memory
  identity.
- Corrected the tests to compare a semantic representation containing every scalar
  field and the hexadecimal payload content. No production code was changed for this
  test defect.
- Reran the narrow transport suite with `--no-restore --verbosity minimal`.
  Result: 6 tests executed, 6 passed, 0 failed, 0 skipped; build completed without a
  reported warning or error.
- Ran `dotnet test Dami.sln --no-restore --verbosity minimal`.
  Result: both test assemblies executed; 7 total tests passed, 0 failed, 0 skipped.
- Ran `dotnet format Dami.sln --verify-no-changes --no-restore --verbosity minimal`.
  Result: exit code 0 with no formatting changes required.
- Ran `git diff --check`; it returned no whitespace errors.
- Listed the solution projects and confirmed all five expected entries: the two
  existing placeholders plus Contracts, Transport, and Transport.Tests.
- Final status also showed concurrent modifications to `CLAUDE.md`, `docs/status.md`,
  and `docs/workstation-runbook.md`. Codex did not edit those files during this slice.

## 2026-08-22 — Claude Code — Host infrastructure: Postgres, GPU, inference sidecars

Session scope was host infrastructure only. No .NET source was written; the `Dami/`
solution belongs to Codex.

### Git and GitHub

- Configured `/home/steve/.gitconfig`: `init.defaultBranch=main`,
  `push.autoSetupRemote=true`, `fetch.prune=true`, `pull.rebase=true`.
- Generated a passphrase-less ed25519 key for `steve`, pre-seeded GitHub's host key
  after checking its fingerprint, switched `origin` from HTTPS to SSH. Verified:
  `Hi shoff! You've successfully authenticated`.
- Installed `gh` 2.98.0 from GitHub's apt repo. **Not yet authenticated** —
  `gh auth login` needs an interactive browser session from Steve.

### Deviation, recorded rather than tidied away

- Commit `7d3b508` used `git add -A` and accidentally captured Codex's in-flight
  `Dami/Dami.sln`, `Dami/Dami/Dami.csproj`, and `Dami/Dami/Program.cs` under an
  unrelated commit message about pgAdmin and apt. Nothing was lost, but the history is
  misleading. Subsequent commits stage explicitly by path.
- Several files created by agents were root-owned inside Steve's repository
  (`AGENTS.md`, `docs/work-log.md`, `Dami/src/Dami.Contracts`, `Dami/src/Dami.Transport`,
  `Dami/tests/Dami.Transport.Tests`). Ran `chown -R steve:steve` on the repository.

### apt repositories repaired

- `pgadmin4.list` pointed at suite `zena`, Mint's codename, which pgAdmin does not
  publish. `apt-get update` returned 404 and a hard error, breaking **every**
  repository. Verified `noble` → 200 and `zena` → 404 before correcting it.
- Installed `pgadmin4-desktop` 9.17. Removed the `dami-pgadmin` container and its image.
- All seven repositories now validate with 0 errors; all five keyrings present.

### PostgreSQL

- Added PGDG (`noble` hardcoded). This swapped `postgresql-16` from Ubuntu's build to
  `16.15-1.pgdg24.04+2` and pgvector `0.6.0` → `0.8.6`, satisfying D-004 in full.
  Pre-change dump at `/home/steve/Data/pg-backups/pre-pgdg-20260822.sql`.
- Created the `vector` extension in `dami-data`; it existed only in `postgres`.
- Measured index dimension ceilings rather than quoting them:
  `vector` caps at 2000 dims, `halfvec` at 4000. Qwen3-Embedding-4B (2560) is indexable
  via `halfvec`; 8B (4096) is not and needs Matryoshka truncation.
- Created least-privilege roles per charter §10.1: `dami_ddl` owns schema `dami`,
  `dami_app` has DML only. Verified `dami_app` cannot create in the schema
  (`permission denied for schema dami`) and cannot self-escalate
  (`permission denied to alter role`). Passwords generated, never echoed, stored only in
  `/home/steve/.pgpass` at 0600.
- Shredded `dami-data-environment.md`, which held the superuser password in the repo
  root and was untracked but not ignored. Added ignore rules for credential files.

### GPU and inference sidecars

- Installed NVIDIA Container Toolkit 1.20.0-1; registered the `nvidia` runtime.
- **CUDA compute proven inside a container**: a `cuInit()` probe returns
  `CUDA_SUCCESS` with `deviceCount=1`. `nvidia-smi` alone does not demonstrate this —
  it uses NVML and succeeds even when CUDA is broken.
- `dami-embed`: TEI `89-1.9.0` (pinned), `BAAI/bge-m3`, 1024 dims, on CUDA.
- `dami-rerank`: same image, `BAAI/bge-reranker-v2-m3`, cross-encoder, on CUDA.
- `dami-llm`: `ollama/ollama:0.32.15`, `qwen3:8b`, on CUDA.
- Installed `uv` 0.12.5 to `/usr/local/bin`, verified against the published SHA256.

### Failure observed and diagnosed

- TEI started, logged `CUDA_ERROR_SYSTEM_DRIVER_MISMATCH`, **fell back to CPU, and still
  reported healthy while serving correct embeddings.** Cause: the image sets
  `LD_LIBRARY_PATH=/usr/local/cuda/compat:...` and ships `libcuda.so.575.57.08` there.
  Forward-compat libraries run a new CUDA runtime on an *old* driver; this host's driver
  is 595.84, newer, so userspace 575 met kernel module 595. Fix:
  `-e LD_LIBRARY_PATH=/usr/local/cuda/lib64`. Proven by the `cuInit` probe —
  `12090`/`cuInit=803` before, `13020`/`cuInit=0` after. `ollama/ollama` does not have
  the compat directory and needed no fix.

### Verification evidence

- Architecture §9.3 pipeline, end to end: ten documents embedded through TEI, written by
  `dami_ddl` into `vector(1024)`, HNSW-indexed, queried by `dami_app`, then reranked.
  For *"which background job connects information from different areas of life?"* the
  ANN top-5 led with the reflection-pass document and the cross-encoder promoted
  "cross-domain correlation" above it. The reranker reordered the ANN result.
- Calibration caveat: raw cross-encoder scores were around −8 to −9 logits on a
  ten-sentence synthetic corpus. Ordering is trustworthy; absolute values are not, and a
  D-021 surfacing threshold must be tuned against the real corpus.
- `qwen3:8b`: 87–128 tok/s warm. First-ever load ~6 min (one-time CUDA kernel
  compilation); subsequent cold load **10.6 s**. `OLLAMA_NUM_PARALLEL=1`, so requests
  serialise.
- **Thinking mode changed correctness, not just latency.** Classifying *"Primed the
  fuselage halves and let them cure overnight"*: `think:false` → `code` (wrong, 0.02 s);
  `think:true` → `workshop` (correct, 3.3 s, 303 tokens). Architecture §7.4 routes simple
  classification to the local sidecar — on this model that needs thinking enabled, so the
  cheap path is not the accurate path.
- Scratch tables (`dami.embed_probe`, `dami.pipeline_probe`, `dimtest`) were dropped
  after use.

### Documentation

- Added `docs/workstation-runbook.md`: service inventory, health checks, the four
  host-specific traps, measured performance, and §7 on working alongside another agent.
  Deferred to `AGENTS.md` on development method rather than duplicating it.

### Outstanding

- Timeshift configured and one snapshot taken; a restore has never been rehearsed.
  Downgraded to recommended by Steve — still a cutover gate via acceptance item 13.
- **The database has no recurring backup.** `/home` is excluded from Timeshift, so the
  cluster is covered only by the single manual dump above.
- ADR-0001 (host OS is Linux Mint, not D-003's Debian 13) is still `proposed`.
- `gh` is installed but unauthenticated.
- `docs/csharpcodestandards.md` is still MAI's document verbatim and names
  `MA.RoslynAnalyzers`, which does not exist for this project.
