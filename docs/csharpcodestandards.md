# MAI C# Code Styles & Standards

The conventions used across every C# project in this solution. Most of them are not
suggestions: they are enforced at build time by the `MA.RoslynAnalyzers` NuGet package
(referenced by every C# project) and `.editorconfig`, and **violations are build errors,
not warnings**. The solution builds with zero warnings; that is the bar for "done."

Sources of truth, in precedence order: `CLAUDE.md` (root), `.editorconfig`,
the `MA.RoslynAnalyzers` package (separate repo), and this guide as the narrative
companion. Target framework is **net10.0** everywhere.

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
public const string SETTINGS_PATH_ENV = "MAI_DESKTOP_SETTINGS_PATH";

// static readonly — always camelCase, regardless of visibility
private static readonly TimeSpan clockSkewGrace = TimeSpan.FromMinutes(5);
private static readonly Regex trivialLinePattern = new(@"^[\s{}();,\[\]]*$", RegexOptions.Compiled);

// WRONG — build errors:
// private const int MaxRetryCount = 3;                    // PascalCase const
// private static readonly TimeSpan DefaultTimeout = ...;  // PascalCase static readonly
// private readonly ILogger _logger;                       // underscore prefix
```

**Known exception**: Avalonia resolves attached/styled properties by the PascalCase
`{Name}Property` field convention, so those fields are PascalCase with a targeted
`#pragma warning disable IDE1006` and a comment saying why
(see `TextMateHighlighting.FilePathProperty`).

## 2. Member access — `this.` always

Every access to an instance member is prefixed with `this.`:

```csharp
this.logger.LogInformation("Processed {Count} commits", processed);
this.startupTrustCheckPending = false;
```

## 3. Files, namespaces, structure

- **File-scoped namespaces** only (`namespace MAI.Core.Models.Mutations;`).
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
  `Activator.CreateInstance` — **constructor injection only**
- `#region`
- `NotImplementedException` on interface members (LSP: an implementation must honor the
  full contract or not implement the interface)
- Optional constructor parameters for dependencies — a DI container silently resolves
  the default and disables the feature (this bit us in production once; see the
  "greedy-ctor" incident). New dependencies are **required** leading parameters.

## 6. SOLID + DRY (hard constraints, analyzer-backed)

- **SRP**: one reason to change per class. If describing it needs "and", split it.
- **OCP**: extend via new implementations of existing abstractions (`ISkill`,
  `IBeforeCompletionHook`, `IArtifactSource<T>`), never by growing a switch.
- **ISP**: small, focused interfaces — the write side (`IMutationLedger`) and the read
  side (`IAuthoredLineReader`) are separate interfaces even when one class implements both.
- **DIP**: all dependencies flow through abstractions defined in `MAI.Core/Abstractions`.
- **DRY**: before writing anything, search the solution for an existing implementation.
  One canonical implementation per capability; adapt/refactor the existing one rather
  than creating a parallel twin. Concrete standing rules:
  - `JsonDefaults.Web` — never `new JsonSerializerOptions(JsonSerializerDefaults.Web)`
  - `McpJsonDefaults.Indented` for MCP serialization
  - `AnsiCodes.*` for ANSI escapes; `ThinkingAnimationDefaults.*` for indicator timing
  - `MAI_DIRECTORY` (`MAI.Core.Constants`) — never hardcode `".mai"`
  - `SecretRedactor` (`MAI.Infrastructure/Security`) — the one redaction ruleset;
    anything logging or persisting user-supplied command/args text runs through it
  - Config section names live as `*_SECTION` constants in `MAI.Core/Constants.cs`

## 7. Abstraction placement — where code belongs

- `MAI.Core/Abstractions` — business-capability interfaces (the "what"); no SDKs.
- `MAI.Core/Models` — domain models shared by more than one project.
- `MAI.Core/UseCases` — orchestration composing abstractions; pure, dependency-free
  logic (e.g. `AuthoredLineHasher`, `CommitAttributionMatcher`).
- `MAI.Contracts` — DTOs/events crossing process boundaries; simple types only.
- `MAI.Infrastructure` — implementations of Core abstractions **and** shared plumbing
  (resilience, health, DI helpers, serialization utilities).
- `MAI.API` / `MAI.Worker` / clients — edge-only code. If another project could ever
  need it, it does not belong in an edge project. Edge projects never reference each other.

Dependency rules are strict: `Core` never references Infrastructure or any external SDK;
`Contracts` holds simple types only.

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
- Table names built from `PostgresOptions.SchemaName` — **never hardcode `mai`/`mai_dev`**
  (`private string Table => $"{this.storeOptions.SchemaName}.agent_mutation_ledger";`).
- SQL exposed as **pure static builders** — `public static string BuildUpsertSql(string table)` —
  so projections are unit-testable without a database.
- Idempotent writes: `INSERT … ON CONFLICT … DO NOTHING/UPDATE`.
- Parameters via `AddWithValue` with `(object?)value ?? DBNull.Value` for nullables;
  timestamps from injected `ISystemClock.UtcNow`, never `DateTimeOffset.UtcNow` inline.
- DDL ships as a runner under `tools/ddl/` (Npgsql script, connect-retry,
  `ALTER … OWNER TO ma_ddladmin_rw_mai_convo_dev` after every `CREATE`, explicit
  `GRANT`s), applied to **`mai_dev` first**, `mai` only at prod promotion.

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
  verification. Infrastructure tests inherit `BaseTest` (ManageAmerica.UnitTest);
  Core tests do **not**. No `InternalsVisibleTo` — test helpers are `public`.
- Fire-and-forget behavior is made testable by exposing the in-flight task
  (e.g. `SkillDispatcher.LastMutationRecord`), not by sleeping.
- Tests never touch the developer's real environment: settings/tokens are redirected to
  temp files by `[ModuleInitializer]`s (`TestSettingsIsolation`), UI tests close every
  window they show (`CloseAndDrain`), and the desktop suite pins serial execution +
  a raised thread-pool floor (`TestThreadPoolGuard`) — each guard documents the CI
  incident that motivated it.
- No hardcoded telemetry values (analyzer `MAI6001` + its vitest twin): report and
  measurement fields are computed or genuinely empty, never placeholder numbers.

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

## 12. Analyzer categories (MA.RoslynAnalyzers — all build errors)

| Category | Enforces |
|---|---|
| `MAI.Style` | `this.` prefix, `_`-prefix ban, braces, file-scoped namespaces, `#region` ban, async suffix, no interpolated logging |
| `MAI.SOLID` | SRP/OCP/LSP/ISP/DIP detectors |
| `MAI.Guards` | null-guard patterns |
| `MAI.Performance` | hot-path violations (LINQ, boxing, closures) |
| `MAI.Security` | security pattern checks |

`.editorconfig` backs the naming rules (const → `UPPER_CASE_WITH_UNDERSCORES`,
`static readonly` → camelCase, both at all accessibilities).

## 13. Definition of done

1. `dotnet build MAI.sln` — **0 warnings, 0 errors**.
2. `dotnet test MAI.sln` — all green (plus the TS suites — ClientCore, InkCLI, VSCode —
   when their files changed).
3. Surgical diffs: no reformatting of unrelated code, no drive-by refactors.
4. Complete files — no placeholders, no "rest omitted".
5. Architectural decisions (queues, retries, idempotency, budgets) explained in the
   change description, and their non-obvious constraints written into code comments
   where the code alone can't show them.
