# ADR 0015 — Remote MCP uses a dedicated scoped HTTP egress gate

- **Decision:** Remote MCP Streamable HTTP receives its own request-body-capable gate,
  requiring an explicit Egressable operation context and enforcing the common
  allowlist, budget, bounds, and durable event trail on every HTTP request.
- **Date:** 2026-08-24
- **Status:** accepted
- **Supersedes:** none. Extends D-012 and ADR-0010 without widening `IEgressClient`.

## Context

The official MCP SDK's Streamable HTTP transport needs arbitrary JSON-RPC POST bodies,
session headers, DELETE, and streamed HTTP responses. Dami's general `IEgressClient` is
deliberately fetch-only: its request has a destination and purpose but no body or
headers. Widening that contract would hand every feed/search service a general payload
channel and erase the structural D-012 guarantee recorded in ADR-0010.

Passing an ordinary `HttpClient` to the SDK is also unacceptable. It would bypass the
egress allowlist and C5 budget, follow redirects below Dami's policy layer, and produce
no durable `EgressRequested`/terminal events. A connection-wide privacy value or trace
does not solve this: MCP sessions are long-lived and can carry concurrent calls from
different traces. The classification and provenance must follow each async operation.

## Decision detail

1. `IEgressClient` remains bodyless. MCP is a separate, narrow body-capable door; the
   implementation is `McpEgressHttpMessageHandler` in `Dami.Privacy`, not a new general
   payload method.
2. Every remote SDK operation runs inside an immutable `EgressOperationContext` carrying
   purpose, `PrivacyClass`, trace ID, parent span ID, and origin. The context flows only
   through an explicit `IEgressOperationScopeFactory` scope. The HTTP gate reads it via
   the separate `IEgressOperationContextReader`; no scope means refusal before network
   I/O. The ambient implementation uses `AsyncLocal` only as SDK plumbing, not as an
   authority or classification heuristic, and isolates concurrent async flows.
3. Only `PrivacyClass.Egressable` may pass. The handler never tries to infer whether a
   JSON body contains profile data; that is neither reliable nor decidable. The caller
   must carry the route's classification, and F3c3b makes omission impossible at the MCP
   execution contract.
4. Every attempted HTTP request durably appends `EgressRequested` before its gate. An
   allowed request then appends `EgressCompleted` when an HTTP response is accepted;
   policy refusals append `EgressRefused`, and network/response-header failures append
   `EgressFailed`. Labels contain only the configured purpose, destination host, status,
   or policy reason — never body bytes, arbitrary headers, credentials, or remote error
   prose.
5. The gate requires HTTPS, exact host allowlisting, the shared forbidden-URI tripwire,
   and the shared event-count budget. Request bodies are buffered under an explicit byte
   ceiling before I/O. Response bodies retain streaming behavior behind a counting
   stream and fail when their byte ceiling is crossed; a declared oversized response is
   rejected before reading.
6. Redirects are refused. In particular, a cross-origin redirect cannot carry MCP
   authorization or session headers to a second host even when both hosts are
   allowlisted. The final MCP endpoint must be configured explicitly, and the inner
   `HttpClientHandler`/`SocketsHttpHandler` must have automatic redirects disabled.
7. The original convenience transport factory remains loopback-only. F3c3b is the only
   code path allowed to give a remote registration an SDK `HttpClientTransport`, and it
   must be constructed over this gate. F3d will make that wiring auditable in the Host.

## Alternatives considered

| Option | Strengths | Weaknesses | Why not chosen |
|---|---|---|---|
| Add body/headers to `EgressRequest` | One HTTP abstraction | Every general egress consumer gains an exfiltration channel | Contradicts ADR-0010 and deletes a structural guarantee |
| Give the SDK an ordinary `HttpClient` and meter tool calls | Minimal adapter code | Redirects, initialization, retries, and session shutdown bypass policy/events | It meters intent, not network egress |
| One privacy/trace context per connection | No ambient scope | Concurrent calls audit to the wrong trace; later LocalOnly use can reuse an Egressable session | Provenance and classification belong to operations |
| Inspect/redact JSON-RPC bodies in the handler | Appears defensive | Cannot reliably identify profile-derived compositions and risks corrupting protocol payloads | Explicit classification and consent are the enforceable boundary |
| Allow same-origin redirects | More endpoint tolerance | Requires replaying buffered methods, bodies, credentials, and session semantics correctly | Exact endpoint configuration is simpler and safer |

## Evidence

Red-first privacy tests exercise an exact JSON POST through the gate, LocalOnly and
missing-context refusal before network I/O, percent-encoded forbidden fragments,
request and response ceilings, cross-origin redirect refusal, failure events, nested
scope recovery, and concurrent async-flow isolation. The implementation gate and full
solution totals are recorded in `docs/work-log.md` with F3c3a completion.

## Consequences

Remote MCP can use the official SDK without creating an unmetered side door, while feed
fetching remains structurally bodyless. Each HTTP request pays an awaited durable-event
and budget check, and request bodies are buffered once to enforce their limit. This is
intentional security-path cost. Streamed responses are not buffered as a whole.

The SDK adapter must open an operation scope around connect, discovery, invocation, and
session shutdown. A response that exceeds its limit after headers have been accepted
propagates an `InvalidDataException`; its durable requested/completed header events
remain evidence that the network request occurred. The final remote endpoint cannot
depend on redirects.

## Reversal path

Remote MCP is still disabled by the loopback-only convenience factory, so reversal is
small: remove the scoped-handler overload and these context contracts, leaving local MCP
unchanged. If a future SDK exposes a per-request callback carrying explicit application
state, replace the `AsyncLocal` scope adapter with that callback while retaining the
same context, policy, tests, and event semantics.
