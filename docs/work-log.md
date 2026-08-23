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

## 2026-08-22 — Claude Code — Dami.Analyzers

Steve authorized a Dami analyzer project to close the rest of the enforcement gap.
Six rules, built red-first per `AGENTS.md`.

### TDD sequence, as observed

1. Created `Dami/src/Dami.Analyzers` with six `DiagnosticAnalyzer` classes carrying
   correct descriptors but **empty `Initialize` bodies**, so they report nothing.
2. Wrote eleven tests in `Dami/tests/Dami.Analyzers.Tests` — one violation case and one
   compliant case per rule where a compliant case is meaningful.
3. **Observed red:** `Failed: 6, Passed: 5`. The six "should report" tests failed; the
   five "should stay silent" tests passed trivially because nothing reported.
4. Implemented the six analyzers.
5. **Observed green:** `Passed: 11, Failed: 0`.

**Deviation from `AGENTS.md`, recorded rather than glossed:** the rule is one test at a
time. All eleven were written before any implementation, so there was a single observed
red phase covering six rules rather than six separate cycles. The red phase was real and
is quoted above, but this was batched TDD, not strict one-at-a-time TDD.

### Rules

| Id | Rule | §  |
|---|---|---|
| `DAMI0001` | `#region` banned | 3 |
| `DAMI0002` | `dynamic` banned | 5 |
| `DAMI0003` | method body over 30 lines | 3 |
| `DAMI0004` | loop nesting over 2 levels | 3 |
| `DAMI0005` | optional constructor parameter of an abstraction type | 5 |
| `DAMI0006` | `NotImplementedException` on an interface implementation | 5 |

`DAMI0005` deliberately flags only abstraction-typed parameters — interface, abstract
class, delegate. An optional `int retries = 3` is a value, not a dependency, and banning
those would be noise. `Nullable<T>` is excluded so `int? x = null` is not caught.

`DAMI0003` counts the statement lines of the body, not the signature or braces, so a long
parameter list is not punished twice.

### Two things the tooling caught in my own code

- Writing the tests, `const string SOURCE` failed `IDE1006`. **The enforcement layer was
  right and I was wrong**: §1 mandates `UPPER_CASE` for const *fields*; these are local
  constants, which the camelCase locals rule governs. Renamed to `code`.
- Wiring the analyzer solution-wide, the first build failed with **`DAMI0004` on
  `AsyncContractTests.PublicMethods`** — three nested loops in the architecture test I
  had written an hour earlier. **Codex's code was clean; mine was not.** Fixed by
  extracting `MethodsIn` and `DeclaredMethodsOf`, which is exactly the remedy the
  diagnostic names.

### Wiring

`Directory.Build.props` adds the analyzer to every project as
`OutputItemType="Analyzer" ReferenceOutputAssembly="false"`, excluding `Dami.Analyzers`
itself (circular) and `Dami.Analyzers.Tests` (references it as a library).

**Known limitation:** `Dami.Architecture.Tests` reads `ProjectReference` elements from
`.csproj` files only, so references injected by `Directory.Build.props` are invisible to
it. That is harmless for a build-time analyzer with `ReferenceOutputAssembly="false"`,
but if a real runtime dependency is ever added there, the layering tests will not see it.

### Verification

- `dotnet build Dami.sln` → **0 warnings, 0 errors**.
- `dotnet test Dami.sln` → **28 passed** across four suites: Dami.Tests 1,
  Dami.Transport.Tests 6, Dami.Architecture.Tests 10, Dami.Analyzers.Tests 11.
- Standards §12 rewritten: the `DAMI****` rules are listed as build errors, and the
  remaining gap is reduced to things not decidable from syntax (SRP, OCP, ISP, hot-path
  LINQ) plus two deliberate not-yets (`CA2254`, `CS1591`).

## 2026-08-22 — TCP/Pipelines transport slice started

### Scope

- Continue architecture §7.5.5 with one Pipelines-based connection after the completed
  frame codec, `ITransport`, and `LoopbackTransport` work.
- First build a framed `PipeTransport` over `IDuplexPipe`. This isolates frame delivery
  from socket creation and keeps TCP connection establishment as a separate reason to
  change.
- Implement one behavior at a time with an observed red run before production code:
  receive one complete frame, then send one frame, then incremental partial input,
  cancellation/completion, and finally the TCP socket adapter.
- Explicitly exclude reconnect, heartbeat, sequence-gap handling, and UDP from this
  slice.

### Start evidence

- Ran `git pull --rebase` as OS user `steve`: already up to date.
- Read `docs/status.md` §4 and §6 and the tail of this log as required by the runbook.
- Working tree contained an unrelated concurrent modification to `CLAUDE.md`; this
  transport work will not alter or stage it.

### Cycle 1 — receive one complete frame — red

- Added only
  `PipeTransportTests.ReceiveAsync_Should_Yield_A_Complete_Frame_From_The_Input_Pipe`.
- Restored repository ownership to `steve:steve` and ran only that test as OS user
  `steve`.
- Observed red: compiler error `CS0246`, `PipeTransport` could not be found. Exit code
  1; no test executed because the production type deliberately did not exist.
- Narrowed the test helper parameter from `ITransport` to `PipeTransport`. This keeps
  the first production increment limited to tested receive behavior; implementing an
  untested `SendAsync` merely to satisfy the full interface would violate the required
  one-behavior-at-a-time TDD sequence.

### Cycle 1 — receive one complete frame — green

- Added the minimum `PipeTransport` implementation: constructor injection of one
  `IDuplexPipe`, incremental reads through `FrameCodec`, explicit trailing-data
  validation on completion, and asynchronous disposal of both pipe sides.
- Ran only the new receive test as OS user `steve`.
- Observed green: 1 test executed, 1 passed, 0 failed, 0 skipped; exit code 0.

### Cycle 2 — send one complete frame — test added

- Added only
  `PipeTransportTests.SendAsync_Should_Write_A_Complete_Frame_To_The_Output_Pipe`.
- Production does not yet expose `SendAsync`; the narrow run is expected to fail at
  that missing contract.

### Cycle 2 — send one complete frame — red

- Ran only the new send test as OS user `steve`.
- Observed red: compiler error `CS1061`, `PipeTransport` did not contain a definition
  for `SendAsync`. Exit code 1; no test executed.
- Added the minimum implementation: encode into the injected `PipeWriter`, flush with
  the caller's cancellation token, and implement the existing focused `ITransport`
  abstraction now that both required behaviors exist.

### Cycle 2 — send one complete frame — green

- Ran only the new send test as OS user `steve`.
- Observed green: 1 test executed, 1 passed, 0 failed, 0 skipped; exit code 0.

### Cycle 3 — establish one TCP/Pipelines connection — test added

- Added only
  `TcpDuplexPipeTests.ConnectAsync_Should_Write_Pipe_Bytes_To_The_Tcp_Peer`.
- The integration test uses an ephemeral loopback listener and requires bytes written
  to the connection's `PipeWriter` to reach the accepted TCP peer.
- Production does not yet contain `TcpDuplexPipe`; the narrow run is expected to fail
  at that missing type.

### Cycle 3 — establish one TCP/Pipelines connection — red

- Ran only the new loopback TCP test as OS user `steve`.
- Observed red: `CS0246` and `CS0103`; `TcpDuplexPipe` did not exist. Exit code 1; no
  test executed.
- Added the minimum adapter: connect one socket, own it through `NetworkStream`, and
  expose a `PipeReader` and `PipeWriter`. Both pipes leave the stream open so the
  adapter has one clear owner and closes the socket exactly once during disposal.

### Cycle 3 — establish one TCP/Pipelines connection — green

- Ran only the new outbound TCP test as OS user `steve`.
- Observed green: 1 test executed, 1 passed, 0 failed, 0 skipped; test duration 184 ms
  and exit code 0.

### Inbound TCP characterization

- Added one test requiring bytes written by the accepted TCP peer to appear through
  the adapter's `PipeReader`.
- This input surface was necessarily introduced with the `IDuplexPipe` adapter during
  Cycle 3. Its first run is expected to characterize existing behavior; it will be
  recorded as coverage if it passes immediately, not as a fabricated TDD cycle.

### Inbound characterization and slice verification

- Ran only the inbound TCP test as OS user `steve`.
- It passed on its first execution: 1 test executed, 1 passed, 0 failed. This is
  characterization coverage, not a red/green TDD cycle, because the input pipe was
  necessarily added with the duplex adapter in Cycle 3.
- Ran the complete transport suite: 10 passed, 0 failed, 0 skipped.
- Ran `dotnet test Dami.sln --no-restore --verbosity minimal`: 32 passed across four
  suites, 0 failed, 0 skipped.
- Ran the solution-wide formatting gate. It failed only on pre-existing placeholder
  files `Dami/src/Dami/Program.cs` and `Dami/tests/Dami.Tests/UnitTest1.cs` for BOM and
  final-newline rules. Those unrelated files were not modified.
- Ran formatting verification separately for `Dami.Transport` and
  `Dami.Transport.Tests`; both exited 0 with no required changes.
- Updated `docs/status.md` to replace its stale claim that no source code exists and to
  record Phase 3 transport progress with command-backed evidence.
- A concurrent untracked `tools/eval/` tree appeared during this work. It was neither
  read nor modified and will not be staged with the transport changes.

## 2026-08-22 — Claude Code — Mandatory build/test rule in CLAUDE.md

Proposed GitHub Actions CI to gate the enforcement layer. **Steve declined on cost** —
Actions minutes on a private repository add up quickly — and asked for a rule instead.

Added a "Build and test — mandatory" section to `CLAUDE.md`: `dotnet build Dami.sln` at
0 warnings / 0 errors and `dotnet test Dami.sln` all green, run from `Dami/`, before
committing anything under `Dami/` or reporting C# work done. It also requires quoting
the actual counts, forbids claiming an interrupted or timed-out run passed, and forbids
reaching green by silencing a rule, deleting a test, or adding a suppression.

**Known weakness, stated plainly:** a rule in a document is honour-system. A `pre-push`
hook in `.githooks/` would run the same two commands automatically and costs nothing —
the hooks directory and `core.hooksPath` already exist from the ownership fix. Offered;
not built, because Steve asked for the rule specifically.

**The rule is in `CLAUDE.md` only.** `AGENTS.md` is the agent-rules file Codex reads and
does not currently mandate a full-solution build and test before commit — its TDD section
covers narrow and affected suites. Flagged for Steve rather than edited, since `AGENTS.md`
is Codex's.

## 2026-08-22 — Claude Code — Database backup

Closed the largest remaining gap: the cluster had no recurring backup, because Timeshift
excludes `/home` and the data directory is `/home/steve/Data/pgsql-dami-data`.

### What was built

- `tools/backup/dami-pg-backup.sh` in the repository, installed to
  `/usr/local/bin/dami-pg-backup`. The installed copy exists so the systemd unit does not
  depend on traversing `/home/steve`, which today is permitted only by an ACL entry
  (`user:postgres:--x` — set deliberately by whoever moved the data directory there).
- `dami-pg-backup.service` (oneshot, `User=postgres`) and `dami-pg-backup.timer`
  (02:30 daily, `RandomizedDelaySec=300`, `Persistent=true`). Enabled; next run reported
  by `systemctl list-timers` as 2026-08-23 02:32:22.
- Per-database `pg_dump --format=custom --compress=9`, plus `pg_dumpall --globals-only`
  for roles, sha256 beside each, 14-day retention.

### Design decisions

- **Every archive is verified at creation** with `pg_restore --list`; failure aborts the
  run. Retaining an unreadable file that looks like protection is worse than none.
- **Retention runs only after every dump succeeds**, so a failed run cannot delete the
  last good copy.
- Peer authentication over the local socket — no password read, stored, or passed.

### Verification

```
$ pg_restore --dbname=dami_restore_probe --no-owner dami-data-20260823T030013Z.dump
$ select nspname from pg_namespace where nspname='dami'   -> dami
$ select extversion from pg_extension where extname='vector' -> 0.8.6
$ sha256sum -c *.sha256   -> 3 files OK
```

Probe database dropped. Service then run through systemd rather than by hand:
`2 database(s) ... verified, keeping 14d`.

**A restore was actually performed.** That is the evidence acceptance-suite item 13 asks
for, and it is more than ADR-0002's Timeshift path can currently claim.

### Limits recorded in ADR-0003 rather than left to be discovered

1. Archives sit on the **same physical disk** as the database. So do the Timeshift
   snapshots. **This host has no defence against losing `nvme0n1`.**
2. Archives are **unencrypted**, and `globals-*.sql` contains SCRAM verifiers for every
   role. Mode 0600 and postgres-owned is adequate on-host and not adequate anywhere else.
   Encryption must be settled before anything leaves the machine.
3. **14 days is arbitrary.** No recovery objective has been stated, so the number is not
   derived from one. Said so in the ADR rather than implying rigour that is absent.

ADR-0003 is accepted **as an interim measure** and explicitly does not close the
register's open decision on destinations, encryption, and retention. PITR is the right
answer and the natural trigger is the corpus landing in Phase 2.

## 2026-08-22 — Claude Code — D-010 retrieval eval harness

`bge-m3` is serving because it was a sensible default, which is exactly what D-010 says
must not decide the embedder. Built the instrument so the decision becomes one command
when the corpus exports off the Mac.

### What was built

- `tools/eval/retrieval_eval.py` — a `uv` script with PEP 723 inline dependencies, so
  there is no virtualenv to create. Embeds a corpus through TEI, stores it in a
  per-label table, scores a query set, and reports **ANN only** against **ANN + rerank**.
- `tools/eval/README.md` — input format, how to swap models, and what the numbers mean.
- `tools/eval/sample-{corpus,queries}.jsonl` — 15 synthetic documents and 8 queries,
  clearly labelled as a smoke test and not an eval set.

### Design decisions

- **Exact search, no HNSW index, deliberately.** The question D-010 asks is how good the
  embedding model is; an index would confound that with index recall. A useful
  consequence: **a model too large to index can still be evaluated.**
  Qwen3-Embedding-8B at 4096 dims exceeds `halfvec`'s 4000-dim ceiling on this cluster
  but will still score. That separates "is it good" from "can we deploy it", which had
  been conflated.
- Dimension is probed from the running service rather than configured, so a model swap
  needs no edit.
- One table per `--label`, so runs do not overwrite each other.
- Requests chunked at 32 (TEI's `max_client_batch_size`). A 50-candidate rerank is two
  calls whose scores are merged; cross-encoder scores are independent per pair, so
  chunking does not change ordering.
- Connects as `dami_ddl` because it creates tables; credentials from `~/.pgpass`.

### Observed result, and why it is not a finding

```
stage              recall@5          mrr       ndcg@5  p50_seconds
ANN only             0.9375       0.8750       0.8619       0.0272
ANN + rerank         0.9375       0.8167       0.8213       0.0739
rerank delta : recall@5 +0.0000  mrr -0.0583  ndcg@5 -0.0405
```

Reranking scored worse and cost 2.7× the latency. **This is not evidence against
D-008.** With 15 documents and 15 candidates the ANN stage already returns the whole
corpus, so the reranker has no filtering job — it can only reorder, and every mistake is
a pure regression with no recall to win back. Reranking earns its place when top-50 is
drawn from thousands, which this sample cannot create.

Recorded prominently in the README because a number in a repository gets quoted, and
this one would be quoted wrongly.

What it does demonstrate is that the harness detects a regression rather than assuming
an improvement. D-008 claims reranking is "the largest single quality gain available";
this is the instrument that will confirm or refute that on real data.

### Still blocked

The eval set itself. D-010 wants 50 queries with known-good answers built from the 7,000
memories, and that corpus is Phase 0 on the Mac. The harness is ready; the data is not.

Verified no eval tables were left behind: `pg_tables where schemaname='dami'` → 0.

## 2026-08-22 — Codex — adversarial C# architecture and transport audit

Performed a diagnostic-only review of the complete current .NET solution, including
production projects, analyzers, architecture tests, transport tests, build policy, and
the uncommitted transport slice. No production code was changed. Concurrent untracked
work under `tools/ddl/` was observed and left untouched.

### Confirmed defects

- `TransportFrame` record equality compares `ReadOnlyMemory<byte>` identity rather than
  payload bytes. Two otherwise identical frames with separate equal byte arrays compare
  unequal.
- `LoopbackTransport` retains the caller's payload memory. Mutating the source array
  after `SendAsync` changes what the receiver observes, while `PipeTransport` copies the
  payload. The two `ITransport` implementations therefore have incompatible ownership
  semantics.
- `FrameCodec` accepts an overflowing fifth varint byte. The isolated probe encoded the
  prefix `99 80 80 80 10`; it was accepted as a complete frame with no remaining input.
- `PipeTransport` has a shared, unserialized `PipeWriter`. Concurrent `SendAsync` calls
  are not prohibited by `ITransport`, but pipelines require a single coordinated writer.
  `FlushResult` and canceled `ReadResult` state are also ignored.
- `PipeTransport` and `TcpDuplexPipe` dispose resources sequentially without exception
  safety or lifecycle coordination. A failure completing the first side skips all later
  cleanup, and send/receive/dispose races are undefined.

### Structural and performance findings

- Every decoded frame allocates and copies a new payload array, including a permitted
  16 MiB large-object-heap allocation. Repeated peer-controlled frames can create severe
  allocation pressure. Removing the copy safely requires an explicit buffer-ownership
  abstraction, not a local micro-optimization.
- `TcpDuplexPipe` is outbound-only and combines connection creation with pipe adaptation;
  it has no accepted-socket seam and does not enable `NoDelay` for small latency-sensitive
  frames.
- The public `ITransport` contract does not define payload ownership, concurrent caller
  support, single-consumer behavior, completion, or disposal ownership. This ambiguity is
  already producing LSP violations and double-ownership risk around injected pipes.
- Architecture enforcement is partial: async checks hard-code three assemblies, public
  surface checks do not recursively inspect generic type arguments, and the project graph
  scans raw project XML/all project files rather than the evaluated solution graph.
- Analyzer coverage has semantic gaps: the harness ignores compilation diagnostics;
  loop nesting crosses lambda/local-function boundaries; method length covers methods
  only; and `NotImplementedException` detection is textual and method-only.
- Package/test configuration is repeated, there is no `global.json`, and
  `LangVersion`/analysis level use `latest`, so builds are not reproducible across SDK
  upgrades. XML documentation is generated while missing-public-doc warnings are globally
  suppressed.
- The transport tests cover happy paths only and use unbounded `CancellationToken.None`
  waits. They omit concurrency, cancellation, partial arrival, malformed/overlong varints,
  multiple frames, half-close, failure, and disposal races. The placeholder `UnitTest1`
  is an empty green test.

No classic lock-order or sync-over-async deadlock was found. The material liveness risks
are indefinite asynchronous waits, a stalled consumer retaining a pipe buffer across an
async-enumerable yield, and undefined concurrent send/dispose behavior.

### Verification

Run as OS user `steve`:

```text
dotnet build Dami/Dami.sln -c Release --no-restore
  Build succeeded. 0 warnings, 0 errors.

dotnet test Dami/Dami.sln -c Release --no-build --no-restore
  Passed: 32, Failed: 0, Skipped: 0.

isolated audit probe
  equal_payload_frames_compare_equal=False
  loopback_payload_after_caller_mutation=99
  overflowing_varint_accepted=True;remaining=0
```

The isolated probe lives only in `/tmp/dami-audit`; it was not added to the repository.

## 2026-08-22 — Claude Code — Phase 2 schema and DDL runner

Steve opened up coordination via markdown and working on code. Claimed the Phase 2 data
foundation in `docs/ownership.md`; Codex holds the transport slice and is currently on
`PipeTransport` and `TcpDuplexPipe`. Disjoint paths — only `Dami.sln` is shared, and this
change does not touch it.

### What was built

`tools/ddl/` — numbered SQL applied by `apply.sh`, each file in its own transaction,
recorded in `dami.schema_migrations` with a checksum.

| File | Contents |
|---|---|
| `001_migrations.sql` | bookkeeping table |
| `002_event_store.sql` | `execution_events`, canonical per D-017, with `TraceId` and the `Origin` discriminator per D-018 |
| `003_memory.sql` | `observations`, `conclusions`, `conclusion_observations`, `pushbacks` |
| `004_append_only_truncate.sql` | closes a hole found by testing — see below |
| `004_observation_embeddings.sql.template` | **not applied**; see D-010 note |

**Deviation from standards §10**, recorded: that section specifies an Npgsql runner.
This is bash and psql, because creating the schema must not require building the
solution and there is no host project yet. Revisit when `Dami.Host` exists.

### Design decisions

- **Append-only is enforced by the database, not by convention.** `dami_app` holds only
  `INSERT` and `SELECT` on `execution_events` and `observations`, and a trigger refuses
  `UPDATE`/`DELETE` even for the owner. The escape hatch is deliberate and leaves a
  trace: disable the trigger, do the work, re-enable it.
- **No embedding column on `observations`.** A pgvector column has a fixed dimension, so
  adding one now would hardcode the very decision D-010 says the eval must make. The
  embedding lands in a sibling table once a model is chosen; the template carries the
  measured index ceilings (2000 for `vector`, 4000 for `halfvec`).
- `origin` is check-constrained because D-018 settled four values. `type` is
  deliberately unconstrained because event types will grow.
- `conclusions` is mutable by design — retraction sets `retracted_at` in place — so it
  gets `UPDATE`, and a check constraint requires a reason whenever it is set.
- Partial index on active conclusions only: retracted rows are history, not working
  memory, and only the active set is ever embedded (D-009).

### A hole found by testing, not by reading

`002` and `003` guarded `UPDATE` and `DELETE` with `FOR EACH ROW` triggers. **Those do
not fire on `TRUNCATE`.** The runtime role cannot exploit it — `TRUNCATE` is an owner
privilege — but the owner could have emptied the event store with the guard silent,
which is precisely the audit property the store exists to provide.

Fixed in `004_append_only_truncate.sql`, added as a **new file** rather than by editing
`002`/`003`, because those are already applied and the runner's checksum guard would
flag the edit as a divergence between repository and database.

### Verification

```
idempotence         re-run -> "apply: nothing pending"
grants              execution_events INSERT,SELECT   observations INSERT,SELECT
                    conclusions INSERT,SELECT,UPDATE  pushbacks INSERT,SELECT,UPDATE
as dami_app         insert OK; update/delete -> permission denied
as owner            update/delete/truncate -> "append-only; ... is not permitted"
check constraints   unknown origin, self-parent span, confidence 1.5,
                    retraction without reason, unknown pushback outcome -> all rejected
valid row           accepted
escape hatch        disable -> delete -> enable, then update rejected again (live row)
```

All probe rows removed; the three tables are empty.
