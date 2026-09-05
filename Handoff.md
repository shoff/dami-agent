# Dami handoff — 2026-09-01

## Purpose and current user direction

The user asked to add Elasticsearch/Kibana containers and structured application logging
after an SSE conversation response ended prematurely. An initial implementation added
Elasticsearch, Kibana, JSON logging, and a Filebeat journald shipper.

The user then corrected the design explicitly:

> “FFS dotnet core uses ILogger and there are serilog elasticsearch nuget packages that
> if the logger isn't available it just does nothing,”

They are right. **Do not close N9 with Filebeat as the final design.** Replace Filebeat
with a direct Serilog Elasticsearch provider behind the existing
`Microsoft.Extensions.Logging.ILogger` surface. Retain JSON console logging to journald
as the independent fallback. An absent or unavailable Elasticsearch instance must never
make either Dami host unavailable.

No Serilog package or direct-sink code was added before this handoff. Filebeat remains
running only until its direct replacement is built, deployed, and verified.

## Repository and safety rules

- Read `docs/onboarding.md` before work. It was read during this session, but a successor
  should read it if it is not in their current context.
- The PostgreSQL board is authoritative: use `DAMI_ACTOR=codex DAMI_ACTOR_KIND=Agent dami
  board dami --open`, then `claim`, `criterion`, and `complete`. Do not claim `TODO.md`.
- Strict TDD: write/change one test; observe its first failure; implement minimum code;
  run narrow then affected tests. Do not call a test written after the implementation TDD.
- Append factual chronology to `docs/work-log.md` before production changes and after
  verification. Update `docs/status.md` for material state changes.
- No underscore-prefixed fields; use `this.` for instance members. No new project or
  architectural boundary. The user has explicitly scoped the needed Serilog packages.
- Do not stage, commit, push, reset, or broadly clean the tree. It is already dirty with
  other user-owned G19–G26 and Discord work.

## Board state

Refresh with:

```bash
DAMI_ACTOR=codex DAMI_ACTOR_KIND=Agent dami board dami --open
```

| Task | Board id | Criteria that remain unmarked |
|---|---:|---|
| N9 Elasticsearch/Kibana observability and structured runtime logs | `f3947c38` | `f3399018` containers/durable/no-public-listener; `782bb0b2` Host/Proactive JSON logs with correlation and no prompt/profile payloads; `82961972` Kibana query with verified Dami event |
| N10 Disabled daily portrait must not stop the proactive tier | `d7a87242` | `6efc62ff` disabled portrait not scheduled/unsupported cadence not written; `1150427f` deployed proactive active |

N10 is implemented and runtime-verified, but criteria are not marked. N9 has useful
evidence but is not acceptable until direct Serilog ingestion replaces Filebeat.

## Live workstation state

| Component | State / address | Detail |
|---|---|---|
| `dami-host` | `active`; `http://127.0.0.1:5810/health` → `{"status":"ok"}` | current dirty-tree Release published to `/opt/dami/host` |
| `dami-proactive` | `active` | a current tick completed the embedder pass normally |
| Elasticsearch | `dami-elasticsearch`, healthy, `http://127.0.0.1:9200` | `docker.elastic.co/elasticsearch/elasticsearch:9.5.2`; data in `/home/steve/Data/dami-observability/elasticsearch` |
| Kibana | `dami-kibana`, up, `http://127.0.0.1:5601` | `docker.elastic.co/kibana/kibana:9.5.2`; data view **Dami Runtime**, `dami-runtime-*`, time field `@timestamp` |
| Filebeat — temporary/wrong final design | `dami-filebeat`, up, no published port | `docker.elastic.co/beats/filebeat:9.5.2` |

Elasticsearch and Kibana bind only to loopback. Elastic security/HTTPS are disabled only
inside this local diagnostic stack. Never expose either port without an authenticated HTTPS
design decision.

The first Filebeat start ingested about 5,351 events because the GUI was polling. The
current request policy suppresses successful read-request lifecycle records to avoid that
noise; failed GET/HEAD/OPTIONS requests still warn.

## N10 defect and repair

Restarting proactive exposed a pre-existing interaction with uncommitted portrait work:

1. `DailyPortraitService` was always registered when `DailyPortrait:Enabled` was false.
2. It uses `EightHourly` cadence.
3. Migration 035's `proactive_runs_cadence_known` permits only `Nightly`, `Weekly`, and
   `Quarterly`.
4. The scheduler wrote a row for the disabled service; PostgreSQL returned SQLSTATE
   `23514`; the default background-service policy stopped the host.

`Dami/src/Dami.Host.Proactive/ProactiveComposition.cs` now calls a private
`AddDailyPortrait()` helper. It registers the portrait service, image generator, and
options only when `DailyPortrait:Enabled` is explicitly true. Test
`Disabled_DailyPortrait_Should_Not_Be_Scheduled` failed first, then passed. This does not
weaken the database constraint. Enabling portraits later needs a separate cadence
migration first.

Historical `23514` logs at 21:22 and 21:25 CDT pre-date the repaired 21:31 CDT restart;
they are not a current outage.

## Existing structured logging source changes

Both entry points currently clear providers, enable activity tracking, and add scoped JSON
console logging:

- `Dami/src/Dami.Host/Program.cs`
- `Dami/src/Dami.Host.Proactive/Program.cs`

The Host also adds `StructuredRequestLoggingMiddleware` after the existing exception
middleware. Added files:

- `Dami/src/Dami.Host/DamiRequestLogScope.cs`: `RequestId`, `RequestMethod`,
  `RequestPath`, activity `TraceId`; intentionally no request body or query string.
- `Dami/src/Dami.Host/DamiRequestLoggingPolicy.cs`: normal lifecycle records only for
  state-changing requests; read failures are still warnings.
- `Dami/src/Dami.Host/StructuredRequestLoggingMiddleware.cs`: structured start,
  completion, and failure type/duration records, then rethrow.

`Dami/src/Dami.Host/TurnEndpoints.cs` now emits an `X-Dami-Trace` response header for all
streaming paths, including direct GUI frontier chat. This correlates the visible client
trace to request logs without recording the prompt.

Tests added/changed:

- `Dami/tests/Dami.Host.Tests/Observability/DamiRequestLogScopeTests.cs`
- `Dami/tests/Dami.Host.Tests/Observability/DamiRequestLoggingPolicyTests.cs`
- `Dami/tests/Dami.Host.Tests/FrontierEndpointsTests.cs`
  (`PostStreamingFrontier_Should_Return_The_DamiTrace_Header`)
- `Dami/tests/Dami.Proactive.Tests/ProactiveCompositionTests.cs`
  (`Disabled_DailyPortrait_Should_Not_Be_Scheduled`)

The missing scope/policy types and missing stream header were observed failing before
their production changes. Logging narrow tests passed 8/8. Final affected suite counts:
Host 101/101 and Proactive 245/245.

## Temporary Filebeat implementation to remove

These files currently drive the running temporary stack:

- `tools/observability/docker-compose.yml`
- `tools/observability/filebeat.yml`
- `tools/observability/README.md`

Compose creates Elasticsearch, Kibana, and Filebeat. Filebeat runs as root in the
container, read-only, with `SYS_CHROOT`, mounting `/:/hostfs:ro`; it filters the journald
input to `dami-host.service` and `dami-proactive.service`, then sends to the internal ES
alias. It has no published port. This complexity is the reason to replace it.

After direct ingestion is proven:

1. Remove Filebeat service/config/mount/volume declaration from Compose.
2. Stop/remove only `dami-filebeat`. Do **not** delete ES data.
3. Leave unused `dami-filebeat-data` cursor volume unless the user explicitly asks to
   delete it.
4. Update README/runbook/status and append a work-log correction; never rewrite history.

## Required Serilog replacement

The official current package is `Elastic.Serilog.Sinks` 9.0.0, compatible with
Elasticsearch 8+ (local server is 9.5.2). The adapter package is
`Serilog.Extensions.Logging` 10.0.0.

Recommended implementation:

1. Add both package references to `Dami.Host.csproj` and `Dami.Host.Proactive.csproj`.
   Do not add a new project.
2. Preserve existing `AddJsonConsole()` fallback. Add a Serilog provider only when a valid
   configured local ES endpoint exists. No endpoint means no sink provider and console
   logging remains.
3. Use `Enrich.FromLogContext()`, host-specific ECS data streams such as
   `logs-dami-host-default`/`logs-dami-proactive-default`, and
   `BootstrapMethod.Silent`. Sink/bootstrap/network trouble must not block startup.
4. Keep current correlation scope and Dami trace. Verify they arrive as structured
   ECS/Serilog fields, not only rendered message text.
5. TDD configuration/fallback behavior. Avoid a unit test that accidentally sends events
   to the live Elasticsearch container.
6. Deploy both hosts. Prove they remain active with Elasticsearch stopped. Restart ES,
   issue a safe Dami event, query it from Kibana, then remove Filebeat.

Authoritative references:

- <https://www.elastic.co/docs/reference/ecs/logging/dotnet/serilog-data-shipper>
- <https://www.nuget.org/packages/Elastic.Serilog.Sinks/>
- <https://www.nuget.org/packages/Serilog.Extensions.Logging/>

## Verification already observed

```text
dotnet build Dami/Dami.sln --no-restore -v:q
Build succeeded. 0 Warning(s), 0 Error(s)

Host suite:       Passed 101/101
Proactive suite:  Passed 245/245

systemctl is-active dami-host dami-proactive
active
active

GET http://127.0.0.1:5810/health
{"status":"ok"}

docker compose -f tools/observability/docker-compose.yml ps
dami-elasticsearch ... Up (healthy) 127.0.0.1:9200->9200/tcp
dami-kibana        ... Up           127.0.0.1:5601->5601/tcp
dami-filebeat      ... Up
```

A full solution test command was started after the final build:

```bash
dotnet test Dami/Dami.sln --no-build --no-restore \
  --logger "console;verbosity=quiet" -m:1 --disable-build-servers
```

Its per-assembly output was truncated by the tool partway through the report. No failure
was shown, but rerun it and capture a conclusive final summary before claiming a global
test count or closing N9. Also run `git diff --check` and, if safe in this shared dirty
tree, `dotnet format Dami/Dami.sln --verify-no-changes --no-restore`.

## Documentation caveat

`docs/work-log.md` and `docs/status.md` currently record the working Filebeat stack. A
prior work-log entry labels N9 completed, but the board is not complete and the user's
later correction supersedes that label. Append a correction; do not delete history.

## Dirty-tree protection and deployment

`git status --short` is extensive: it includes unrelated GUI direct-subscription/image
Gallery G19–G26 work, Discord work, contracts/providers, tests, docs, plus N9/N10. Do not
touch unrelated changes. No active subagents were known at handoff.

The deployed binaries live under `/opt/dami`. This release was published into
`/home/steve/.cache/dami-pub/`, synced to `/opt/dami/host` and `/opt/dami/proactive`, and
restarted by user `steve`. Do not deploy unreviewed unrelated dirty work without clearly
calling it out; prior deployment necessarily contained then-current shared G26 changes.

### File ownership note

Agents run as root while the repository belongs to `steve`. The required
`chown -R steve:steve /home/steve/dev/dami-agent` was attempted after creating this file.
`Handoff.md`, `docs/work-log.md`, and `docs/status.md` are confirmed `steve:steve`.
The command reported `Operation not permitted` only for four generated `obj/` cache files:
the Avalonia resource cache and three Release `PublishOutputs.*.txt` files. Do not remove
or overwrite those generated files just to correct their ownership.

## Immediate continuation checklist

1. Read `docs/onboarding.md` and this file, then inspect N9/N10 diffs only.
2. Append a “N9 direct Serilog correction started” work-log entry before source changes.
3. Write a failing test for absent/valid Elasticsearch configuration and fallback.
4. Add the two scoped package references, restore, implement, and test.
5. Deploy, prove both hosts survive ES down, prove direct event search after ES returns.
6. Stop/remove Filebeat only after that proof; update docs; mark all N9/N10 criteria;
   complete the board tasks with exact evidence. Do not commit unless explicitly asked.
