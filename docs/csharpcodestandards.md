# Dami Core — C# Code Styles & Standards

Carried over from MAI and retargeted to Dami Core on 2026-08-22. The conventions are
inherited wholesale; the enforcement is **not**, and §12 is explicit about the
difference. Target framework is **net10.0** everywhere.

Sources of truth, in precedence order: `AGENTS.md` and `CLAUDE.md` (root),
`.editorconfig` and `Dami/Directory.Build.props`, and this guide as the narrative
companion.

> **What actually enforces these rules here.** MAI enforced them with the
> `MA.RoslynAnalyzers` package from a separate repository. **That package does not exist
> for Dami Core.** Everything mechanically enforceable without it is enforced — see §12
> for exactly what is a build error today and what rests on review discipline. Do not
> assume a rule in this document is checked by a machine unless §12 says so.

The solution builds with **zero warnings**; that is the bar for "done."
`TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` are set solution-wide, so a style
violation fails the build rather than decorating an IDE.

---

## 1. Naming

| Element | Convention | Example |
|---|---|---|
| Types, methods, properties, public/protected members | PascalCase | `CommitAttributionWorker`, `RecordAsync` |
| Locals, parameters, private fields | camelCase — **no `_` prefix, ever** | `mutationLedger`, `recordedAt` |
| `const` fields (ALL scopes — public, internal, private) | `UPPER_CASE_WITH_UNDERSCORES` | `MAX_TOOL_RESULT_LENGTH`, `WORKER_FLOOR` |
| `static readonly` fields (ALL scopes) | camelCase | `trivialLinePattern`, `clockSkewGrace` |
| Interfaces | `I` prefix | `IMutationLedger`, `ICheckpointStore` |
| Async methods | `Async` suffix | `FindMatchesAsync` |

```csharp
// const — always UPPER_CASE, regardless of visibility
private const int MIN_SIGNIFICANT_CHARS = 4;
public const string SETTINGS_PATH_ENV = "DAMI_SETTINGS_PATH";

// static readonly — always camelCase, regardless of visibility
private static readonly TimeSpan clockSkewGrace = TimeSpan.FromMinutes(5);
private static readonly Regex trivialLinePattern = new(@"^[\s{}();,\[\]]*$", RegexOptions.Compiled);

// WRONG — build errors:
// private const int MaxRetryCount = 3;                    // PascalCase const
// private static readonly TimeSpan DefaultTimeout = ...;  // PascalCase static readonly
// private readonly ILogger _logger;                       // underscore prefix
```

**Known exception**: Avalonia resolves attached/styled properties by the PascalCase
`{Name}Property` field convention. If the GUI spike lands on Avalonia, those fields are
PascalCase with a targeted `#pragma warning disable IDE1006` and a comment saying why.

## 2. Member access — `this.` always

Every access to an instance member is prefixed with `this.`:

```csharp
this.logger.LogInformation("Processed {Count} commits", processed);
this.startupTrustCheckPending = false;
```

## 3. Files, namespaces, structure

- **File-scoped namespaces** only (`namespace Dami.Contracts.Transport;`).
- One public type per file; file named after the type.
- **No `#region`** — banned by analyzer.
- Prefer modern C# (11+): pattern matching, target-typed `new`, collection expressions,
  `var` when the type is obvious.
- **Methods ≤ 30 lines** unless complexity genuinely justifies it (a single exhaustive
  dispatch table may run slightly over; a method doing two things may not).
- **Braces always** — no single-line `if`/`else`/`for`/`foreach`/`while` (enforced by
  `MissingBracesAnalyzer`); body on its own line.
- **Never nest loops more than 2 levels** — extract the inner logic to a private method.
- Guard clauses over nested conditionals — early `return` to flatten.

## 4. Types, immutability, nullability

- Models are **`sealed record`** (or `record struct`) with constructor guards:

```csharp
public sealed record MutationRecord
{
    public MutationRecord(Guid id, string threadId, /* … */)
    {
        ArgumentNullException.ThrowIfNull(threadId);
        ArgumentNullException.ThrowIfNull(skillName);
        // nullable parameters (developerId, error, …) are deliberately unguarded
        this.Id = id;
        this.ThreadId = threadId;
    }
}
```

- Simple cross-boundary DTOs are positional records: `GitHubCommitFileDto(string FilePath, string? Patch)`.
- Mark fields `readonly` whenever possible; expose collections as `IReadOnlyList<T>` /
  `IReadOnlyDictionary<K,V>`, defaulting optionals with `?? Array.Empty<T>()` — never null.
- **Nullable reference types enabled** solution-wide. Avoid `!` suppression except at
  proven boundaries, with a comment justifying it.
- Every public type and member carries a one-line `/// <summary>`; the best comments in
  this codebase state a constraint the code cannot show (why a `finally` drains, why a
  token is `CancellationToken.None`), not what the next line does.

## 5. Banned outright

- `dynamic`
- Service Locator / `IServiceProvider` injection / runtime service resolution /
  `Activator.CreateInstance` — **constructor injection only** *(enforced: RS0030)*
- **Sync-over-async**: `Task.Result`, `Task.Wait()`, `Task.WaitAll/WaitAny`,
  `Thread.Sleep`, `GetAwaiter().GetResult()`. Async at the core means async all the way
  down; a blocking call anywhere in the chain forfeits the property for everything above
  it *(enforced: RS0030, VSTHRD002)*
- `async void` outside event handlers — exceptions escape to the synchronization context
  and cannot be caught *(enforced: VSTHRD100)*
- Ambient time: `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.UtcNow`. Inject
  `ISystemClock` so time is testable *(enforced: RS0030)*
- `#region`
- `NotImplementedException` on interface members (LSP: an implementation must honor the
  full contract or not implement the interface)
- Optional constructor parameters for dependencies — a DI container silently resolves
  the default and disables the feature (this bit us in production once; see the
  "greedy-ctor" incident). New dependencies are **required** leading parameters.

## 6. SOLID + DRY (hard constraints)

**SOLID is mandatory, not aspirational**, and most of it is **not** machine-checked here
(§12). It rests on review and on the layering rules in §7. Four failure modes are called
out because they are the ones that actually happen:

- **Leaky abstractions.** An interface must not expose the mechanism behind it. No
  `NpgsqlConnection` on a repository interface, no `HttpResponseMessage` on a domain
  service, no EF `IQueryable` escaping the persistence layer, no provider-specific
  exception types crossing a boundary. If swapping the implementation forces a change to
  the interface, the abstraction leaked.
- **Abstractions at the wrong layer.** An abstraction belongs where it is *consumed*, not
  where it is implemented. `Dami.Core` defines what it needs; `Dami.Persistence` and the
  provider projects implement it. An interface that exists only to mirror one concrete
  class, in the same project as that class, is not an abstraction — it is ceremony.
- **Async at the core.** Async is a property of the whole call chain. Public APIs that do
  I/O are `Task`/`ValueTask`/`IAsyncEnumerable<T>`, cancellation tokens are threaded
  through every one of them (C-06, including proactive work), and no layer blocks to
  adapt. Do not add a sync wrapper "for convenience."
- **Constructor injection only.** Dependencies are required leading parameters. No
  optional dependency parameters, no service location, no runtime resolution.

- **SRP**: one reason to change per class. If describing it needs "and", split it.
- **OCP**: extend via new implementations of existing abstractions (`ISkill`,
  `IBeforeCompletionHook`, `IArtifactSource<T>`), never by growing a switch.
- **ISP**: small, focused interfaces — the write side (`IMutationLedger`) and the read
  side (`IAuthoredLineReader`) are separate interfaces even when one class implements both.
- **DIP**: all dependencies flow through abstractions defined in `Dami.Contracts` and
  `Dami.Core`. A lower layer never names a higher one. See §7 for the direction.
- **DRY**: before writing anything, search the solution for an existing implementation.
  One canonical implementation per capability; adapt/refactor the existing one rather
  than creating a parallel twin. Concrete standing rules:
  - `JsonDefaults.Web` — never `new JsonSerializerOptions(JsonSerializerDefaults.Web)`
  - `McpJsonDefaults.Indented` for MCP serialization
  - `AnsiCodes.*` for ANSI escapes; `ThinkingAnimationDefaults.*` for indicator timing
  - Config section names live as `*_SECTION` constants in a single constants file
  - A single `SecretRedactor` ruleset once one exists — anything logging or persisting
    user-supplied command or argument text runs through it, and D-012's egress boundary
    depends on there being exactly one

  **These are MAI's standing rules, listed as the shape to follow. Dami Core has no
  canonical implementations yet** — the first person to need one creates it and adds it
  here rather than writing a second.

## 7. Abstraction placement — where code belongs

Mapped onto the solution layout in `dami-core-system-architecture.md` §8:

- `Dami.Contracts` — events, tool contracts, approval contracts, memory and transport
  interfaces crossing process boundaries. **No dependencies at all**, simple types only.
  `ITransport` lives here so the runtime never learns what protocol carries its events.
- `Dami.Core` — session lifecycle, context assembly, cancellation, turn orchestration.
  Business-capability interfaces (the "what") and pure dependency-free logic. **Never
  references an implementation project or an external SDK.**
- `Dami.Persistence`, `Dami.Providers`, `Dami.Transport`, `Dami.Capabilities.*`,
  `Dami.Memory`, `Dami.Vision`, `Dami.Voice` — implementations of abstractions declared
  above them, plus shared plumbing.
- `Dami.Privacy` — the egress boundary. Enforcement lives in the composition root and
  must stay auditable in one file (D-012).
- `Dami.Host`, `Dami.Host.Proactive`, `Dami.Gateway.*` — edge and composition roots
  only. If another project could ever need it, it does not belong here. **Edge projects
  never reference each other.**

Dependency rules are strict and directional: `Contracts` depends on nothing; `Core`
depends only on `Contracts`; implementations depend on `Core` and `Contracts`; only a
composition root knows about implementations. **Nothing in this is currently enforced by
a build check** — see §12.

## 8. Performance

**Hot path** means: frequently executed code, large data processing, and **background
services** (workers count). On hot paths:

- **No LINQ** — explicit `for`/`foreach` loops (see any `Postgres*` store or worker).
- No exceptions for control flow; no boxing; no closures in loops.
- `Span<T>`/`ReadOnlySpan<T>`/`Memory<T>` and `ArrayPool<T>` where buffers recur.

Generally: async/await for all I/O with `.ConfigureAwait(false)` in library code,
pagination on every collection endpoint, no N+1 queries, cache when measured-beneficial.
Work that must not gate a user-facing path (telemetry, audit writes) is
**fire-and-forget with the exception swallowed and logged** — never awaited on the
hot path, and never allowed to fault the caller.

## 9. Logging & security

- Structured logging only: `this.logger.LogInformation("Loaded {Count} items", count)` —
  interpolated log messages are an analyzer error.
- **Never log secrets** (tokens, passwords, connection strings); persisted summaries of
  user args go through `SecretRedactor`.
- No credentials in code, config committed to git, or tests — configuration and
  environment variables only. Parameterized SQL only (see the `Build*Sql` pattern below).
- Validate all external input; OWASP Top 10 applies. Source code never leaves customer
  premises (core product principle).

## 10. Postgres store pattern

Every Postgres store follows one shape (reference: `PostgresTierDecisionStore`,
`PostgresMutationLedger`):

- Ctor `(NpgsqlDataSource dataSource, IOptions<PostgresOptions> storeOptions, ILogger<T> logger)`
  with `ThrowIfNull` guards.
- Table names built from `PostgresOptions.SchemaName` — **never hardcode `dami`**
  (`private string Table => $"{this.storeOptions.SchemaName}.execution_events";`).
- SQL exposed as **pure static builders** — `public static string BuildUpsertSql(string table)` —
  so projections are unit-testable without a database.
- Idempotent writes: `INSERT … ON CONFLICT … DO NOTHING/UPDATE`.
- Parameters via `AddWithValue` with `(object?)value ?? DBNull.Value` for nullables;
  timestamps from injected `ISystemClock.UtcNow`, never `DateTimeOffset.UtcNow` inline.
- DDL ships as a runner under `tools/ddl/` (Npgsql script, connect-retry, explicit
  `GRANT`s). On this workstation the roles are **`dami_ddl`** (owns schema `dami`,
  runs migrations) and **`dami_app`** (runtime, DML only). The runtime never connects as
  `postgres`. Credentials come from user-secrets or the environment — never a file in the
  working tree.

## 11. Testing standards (STRICT)

- **xUnit only. NSubstitute only. No FluentAssertions.**
- Project naming `<ProjectName>.Tests`; folders mirror production; files `<ClassName>Tests.cs`.
- Method naming: `MethodName_Should_Describe_Expected_Behavior()` — PascalCase words
  separated by underscores.
- **One assertion per test.** The only exception is mapping/composite validation of a
  single input (a tuple `Assert.Equal` over coupled outputs qualifies).
- Constructor null validation is mandatory and explicit: `[Theory]` + `[InlineData]`
  (or per-parameter `[Fact]`s when the parameter types differ) asserting
  `ArgumentNullException`.
- **No** `// Arrange` / `// Act` / `// Assert` comments.
- Mocks are local variables or built by helper methods; explicit setup and `Received()`
  verification. No `InternalsVisibleTo` — test helpers are `public`. MAI's shared
  `BaseTest` has no Dami equivalent yet.
- Fire-and-forget behavior is made testable by exposing the in-flight task
  (e.g. `SkillDispatcher.LastMutationRecord`), not by sleeping.
- Tests never touch the developer's real environment: settings/tokens are redirected to
  temp files by `[ModuleInitializer]`s (`TestSettingsIsolation`), UI tests close every
  window they show (`CloseAndDrain`), and the desktop suite pins serial execution +
  a raised thread-pool floor (`TestThreadPoolGuard`) — each guard documents the CI
  incident that motivated it.
- No hardcoded telemetry values: report and measurement fields are computed or genuinely
  empty, never placeholder numbers. MAI enforced this with analyzer `MAI6001`; **here it
  is review discipline only**. It matters more in this project than it did in MAI, because
  `status.md` and the work log both treat unevidenced numbers as a defect.

```csharp
[Fact]
public async Task DispatchAsync_Should_Record_A_Failed_Mutation_With_Its_Error()
{
    var ledger = Substitute.For<IMutationLedger>();
    var dispatcher = CreateDispatcher(ledger);
    var skill = CreateSkill("update_jira_issue", isMutating: true, SkillResult.Fail("boom"));

    await dispatcher.DispatchAsync(skill, EmptyParams(), Context(), Session(), CancellationToken.None);

    await ledger.Received(1).RecordAsync(
        Arg.Is<MutationRecord>(r => !r.Success && r.Error == "boom"),
        Arg.Any<CancellationToken>());
}
```

## 12. What is actually enforced

MAI used `MA.RoslynAnalyzers`, which does not exist for Dami Core. This section is the
honest accounting of what a build catches today and what does not.

### Build errors today

Verified 2026-08-22 by compiling deliberate violations and confirming each rule fired.

| Rule | Catches | Standard |
|---|---|---|
| `IDE1006` | `_` prefix, `const` not `UPPER_CASE`, `static readonly` not camelCase, missing `I`/`T` prefix, non-PascalCase members — at **all** accessibilities | §1 |
| `IDE0009` | missing `this.` on instance-member access | §2 |
| `IDE0161` | block-scoped namespaces | §3 |
| `IDE0011` | missing braces on `if`/`else`/`for`/`foreach`/`while` | §3 |
| `IDE0005` | unused usings | §3 |
| `RS0030` | banned APIs: `Task.Result`, `Task.Wait`, `Thread.Sleep`, `Activator.CreateInstance`, `IServiceProvider.GetService`, `DateTime.UtcNow` and siblings | §5, §8 |
| `VSTHRD002` | synchronously blocking on a task | §5 |
| `VSTHRD100`/`VSTHRD110` | `async void`, unobserved tasks | §5 |
| nullable warnings | `Nullable=enable` + `TreatWarningsAsErrors` | §4, C-05 |
| `DAMI0001` | `#region` | §3 |
| `DAMI0002` | `dynamic` | §5 |
| `DAMI0003` | method body over 30 lines | §3 |
| `DAMI0004` | loop nesting over 2 levels | §3 |
| `DAMI0005` | optional constructor parameter of an abstraction type | §5 |
| `DAMI0006` | `NotImplementedException` on an interface implementation | §5 |

`DAMI****` rules come from `Dami/src/Dami.Analyzers`, wired into every project by
`Directory.Build.props`. Layering and leaky abstractions are covered separately by
`Dami/tests/Dami.Architecture.Tests`.

Configured in `.editorconfig` (repo root), `Dami/Directory.Build.props`, and
`Dami/BannedSymbols.txt`. Adding a banned API is one line in `BannedSymbols.txt`.

### The enforcement gap — review discipline only

**Nothing checks these. They are still mandatory.**

| Not enforced | Standard | Note |
|---|---|---|
| SRP, OCP, ISP | §6 | Not decidable from syntax. A "one reason to change" detector would be a heuristic pretending to be a rule; review is the honest answer. |
| LSP beyond `NotImplementedException` | §5, §6 | `DAMI0006` catches the common case. The general property is not statically checkable. |
| No LINQ on hot paths | §8 | Needs a definition of "hot path" the compiler can see. Attribute-marking hot paths and analyzing those would work; not built. |
| One assertion per test; no Arrange/Act/Assert comments | §11 | review |

Two rows left this table on 2026-08-23 (N3): `CA2254` is now an error in
`.editorconfig` — the codebase had zero violations, the discipline had held by
hand — and `CS1591` is enforced in `src/` while `tests/Directory.Build.props`
keeps it waived for tests, where `Method_Should_Behavior` names are the
documentation and a `<summary>` restating them would be noise.

**What closed the gap:** `Dami.Analyzers` (six rules, §12 table above) and
`Dami.Architecture.Tests` (layering, leaky surfaces, async contracts). What remains is
either undecidable from syntax or a deliberate not-yet.

**Adding a rule** is a new `DiagnosticAnalyzer` in `Dami/src/Dami.Analyzers` plus a
red-first test in `Dami/tests/Dami.Analyzers.Tests`. Both are small; the harness is
about thirty lines and takes no external testing package.

## 13. Definition of done

1. `dotnet build Dami.sln` — **0 warnings, 0 errors**.
2. `dotnet test Dami.sln` — all green. Never claim an interrupted, cancelled, or
   timed-out run passed (`AGENTS.md`).
3. Surgical diffs: no reformatting of unrelated code, no drive-by refactors.
4. Complete files — no placeholders, no "rest omitted".
5. Architectural decisions (queues, retries, idempotency, budgets) explained in the
   change description, and their non-obvious constraints written into code comments
   where the code alone can't show them.
