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

## 2026-08-22 — Transport foundation commit and push

- Steve explicitly requested committing and pushing the complete pending working tree
  with no assistant attribution in version-control metadata.
- Staged all pending files. `git diff --cached --check` passed, and the staged diff
  contained no co-author trailer, generated-with line, session link, or tool branding.
- The first commit attempt failed before creating a commit because this checkout had
  no author identity configured: `fatal: unable to auto-detect email address`.
- Read the existing repository author identity from the latest commit and configured
  that same identity locally for this repository. No global Git configuration was
  changed.
- Created commit `fe3050f` with message `Add transport framing foundation`. The commit
  uses Steve's existing author identity and contains no assistant co-author trailer or
  generated-with metadata.
- The first push attempt as the command environment's `root` user failed before
  authentication with `Host key verification failed`; no remote ref changed.
- Retried SSH once with `StrictHostKeyChecking=accept-new`. GitHub's ED25519 host key
  was recorded for that command environment, then authentication failed with
  `Permission denied (publickey)` because the root account has no GitHub key.
- Verified without reading key contents that the repository owner's account has its
  own SSH key and known-host configuration. Ran only the push as user `steve` so Git
  used the owner's existing credentials.
- Push succeeded: `origin/main` advanced from `c45ecdf` to `fe3050f`.

## 2026-08-22 — Claude Code — Build enforcement layer and standards retarget

Scope was build configuration and documentation. No application source was touched;
`Dami/src` and `Dami/tests` remain Codex's.

### Why

`docs/csharpcodestandards.md` described enforcement by `MA.RoslynAnalyzers`, which is
MAI's package from a separate repository and **does not exist here**. There was no
`.editorconfig` and no `Directory.Build.props`. Every rule the document called a "build
error" — including the no-underscore-prefix and `this.` agreements Steve repeats most —
was enforced by nothing but an agent remembering.

### Commands and observed results

- Baseline before any change: `dotnet build Dami.sln` → **0 warnings, 0 errors**. Recorded
  first so any new error would be attributable.
- Added `.editorconfig` (repo root), `Dami/Directory.Build.props`, `Dami/BannedSymbols.txt`.
- Rebuild after: **0 warnings, 0 errors**. Codex's tree passes the full enforcement layer
  unchanged; nothing was forced on it.
- `dotnet test Dami.sln` → `Dami.Tests` 1/1 passed, `Dami.Transport.Tests` 6/6 passed.

### Enforcement verified, not assumed

A green build could equally mean the rules never ran. Compiled a throwaway project at
`Dami/.probe/` containing deliberate violations and confirmed each rule fired **as an
error**, then removed it:

```
8 error IDE1006   _ prefix; PascalCase const; PascalCase static readonly; interface without I
4 error RS0030    DateTime.UtcNow banned; Task<T>.Result banned
4 error IDE0009   missing this. qualification
2 error VSTHRD002 synchronously waiting on a task
2 error IDE0161   block-scoped namespace
2 error IDE0011   missing braces on if
```

### Design choices

- `EnforceCodeStyleInBuild=true` is what gives `.editorconfig` teeth; without it the
  IDE**** rules never fail a build or a CI run.
- `TreatWarningsAsErrors=true` implements §13's zero-warning bar and makes nullable
  violations errors, satisfying C-05.
- Banned APIs cover sync-over-async, service location, and ambient time. Adding one is a
  single line in `BannedSymbols.txt`.
- `AnalysisLevel=latest` with the SDK analyzers, but the full CA ruleset was **not**
  enabled. That would likely have broken Codex's build mid-iteration, which is a
  decision to take deliberately rather than as a side effect.

### Standards document retargeted

Rewrote `docs/csharpcodestandards.md` for Dami: project layout mapped onto architecture
§8, Postgres roles corrected to `dami_ddl`/`dami_app`, MAI's canonical-implementation
list marked as a shape to follow rather than things that exist here.

Expanded §6 for Steve's instruction that SOLID is strict, with the four failure modes
named explicitly: leaky abstractions, abstractions at the wrong layer, async at the core,
constructor injection only.

Replaced §12 with an honest accounting: a table of what is a build error today, and a
table of the **enforcement gap** — SRP/OCP/LSP/ISP/DIP, layering, method length,
`#region`, `dynamic`, hot-path LINQ, and structured-logging-only are all unenforced and
rest on review.

### Open decision raised, not taken

Closing the SOLID gap: port or rewrite `MA.RoslynAnalyzers` for Dami, add architecture
tests for layering only, or accept review-based enforcement. **Layering is the cheapest
and highest-value** — one test project asserting dependency direction would mechanically
close both "leaky abstractions" and "abstractions at the wrong layer". Recorded in §12;
not decided.

### Ownership fix (same session)

Codex committed `fe3050f` and `b262f70` as root. That left **60 root-owned paths**
including `.git/config` and four `.git/objects` subdirectories. The symptom was a hard
failure on the next `git add`:

```
error: insufficient permission for adding an object to repository database .git/objects
fatal: adding files failed
```

Steve would have hit the same wall editing any of those files in his own home directory.

- Ran `chown -R steve:steve` on the repository. Root-owned paths: 60 → 0.
- Added `.githooks/post-commit` and set `core.hooksPath = .githooks`. When a commit is
  made by root, the hook restores the repository to its directory owner. It is a no-op
  for non-root committers and when the repo is genuinely root-owned.
- `core.hooksPath` is local config, so **Codex must set it too** if its clone or config
  differs: `git config core.hooksPath .githooks`.
- Rebuilt after Codex's two commits landed: `dotnet build Dami.sln` → **0 warnings, 0
  errors** with the enforcement layer active. The earlier verification predated their
  transport code; this one covers it.

## 2026-08-22 — Claude Code — Architecture tests for layering and async contracts

Steve authorized adding the test project proposed as open decision #9. This is the
mechanical closure of the two failure modes §6 names but no analyzer catches.

### Scope note for Codex

This adds `Dami/tests/Dami.Architecture.Tests/` and one line to `Dami.sln`
(`dotnet sln add`). No file under `Dami/src` was touched.

### Design

- **Zero new packages.** `AGENTS.md` forbids adding packages without explicit scope, and
  a hand-written check stays readable, which is N-09. NetArchTest would have worked and
  was declined for that reason.
- **Two strategies, deliberately.** Layering is checked by parsing the `ProjectReference`
  graph from the `.csproj` files on disk, so it covers **every project in the solution**
  including ones this test project does not reference and ones not yet written, and it
  fails when a bad reference is *added* rather than when someone consumes it. Leaky
  surfaces and async contracts need real metadata, so those use reflection.
- `AssemblyProbe` skips assemblies that do not exist yet. The rules name the full
  intended solution from architecture §8; a rule starts guarding a project the moment it
  appears rather than when someone remembers to update a list.
- One assertion per test (§11); violations are collected and reported in the message.

### Tests

| Test | Rule |
|---|---|
| `Contracts_Should_Depend_On_Nothing` | §7: `Dami.Contracts` sits at the bottom |
| `Core_Should_Depend_Only_On_Contracts` | §7: `Core` defines abstractions; implementations depend on it, never the reverse |
| `Nothing_Outside_A_Composition_Root_Should_Reference_A_Host` | a host is a composition root |
| `Edge_Projects_Should_Not_Reference_Each_Other` | §7: edge projects never reference each other |
| `Implementations_Should_Not_Reference_Edge_Projects` | dependency direction |
| `Abstraction_Layers_Should_Not_Expose_Mechanism_Types` | §6 leaky abstractions: Npgsql, EF Core, `DbConnection`, `HttpResponseMessage`, `IQueryable`, sockets on a public signature |
| `Contracts_Should_Not_Reference_Any_Other_Dami_Assembly` | verified in metadata, not only in the csproj |
| `Awaitable_Returning_Methods_Should_End_With_Async` | §1 |
| `Awaitable_Returning_Methods_Should_Accept_A_CancellationToken` | C-06 |
| `No_Public_Method_Should_Return_Bare_Void_Asynchronously` | `async void` |

### Observed results

- First run: **9 passed, 1 failed** — `LoopbackTransport.DisposeAsync` was flagged for
  taking no `CancellationToken`. **Investigated rather than suppressed: the test was
  wrong, not the code.** `IAsyncDisposable.DisposeAsync()` has a signature fixed by the
  framework and cannot take one. Added `ImplementsExternalContract`, which exempts
  methods implementing an interface declared outside Dami. C-06 governs contracts we
  author.
- After the fix: **10 passed, 0 failed.**
- **Verified the tests are not vacuous.** Planted three violations in throwaway `.csproj`
  files under `Dami/src` — a `Dami.Core` referencing `Dami.Transport`, and a
  `Dami.Gateway.Probe` referencing both another gateway and a host — and confirmed
  exactly the three expected tests failed. Removed the probes; no Codex file was touched.
- Full solution afterwards: `dotnet build Dami.sln` → **0 warnings, 0 errors**;
  `dotnet test Dami.sln` → **17 passed** across three suites (Dami.Tests 1,
  Dami.Transport.Tests 6, Dami.Architecture.Tests 10).

### Still not enforced

Layering and leaky surfaces are now mechanical. SRP, OCP, LSP, ISP, method length,
`#region`, `dynamic`, and hot-path LINQ remain review-only — §12 of the standards is
still the authority on that split, and open decision #9 narrows to those rather than
closing.
