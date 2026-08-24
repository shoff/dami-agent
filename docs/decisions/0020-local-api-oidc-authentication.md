# ADR 0020 — Local API authentication uses OIDC with PostgreSQL state

- **Decision:** Protect the Dami runtime API with OIDC-derived bearer credentials and
  persist identities, client registrations, grants, refresh-token metadata, and
  revocation state in PostgreSQL.
- **Date:** 2026-08-24
- **Status:** accepted
- **Supersedes:** none

## Context

D-005 binds `Dami.Host` to localhost and defers authentication until remote exposure.
Localhost is not an identity boundary: native tools, MCP subprocesses, inference
sidecars, browsers, and arbitrary processes may all originate loopback traffic. Steve
selected OIDC with PostgreSQL-backed state on 2026-08-24 and directed implementation
to proceed with pragmatic v1 choices that can be iterated later.

The existing `/approvals/{id}/resolve` surface is more consequential than an ordinary
turn submission. Authentication therefore also needs authorization scopes; possession
of any valid client credential must not imply permission to resolve approvals.

## Alternatives considered

| Option | Strengths | Weaknesses | Why not chosen |
|---|---|---|---|
| Trust loopback | No implementation cost | Any local process can impersonate a Dami client | Localhost limits routing, not identity |
| Shared static token | Small | Weak client identity, rotation, revocation, and approval separation | Insufficient for several clients with different authority |
| Mutual TLS for every client | Strong channel identity | Awkward browser and CLI enrollment; not the protocol Steve selected | OIDC is the accepted direction |
| OIDC with PostgreSQL state | Standard flows, scoped clients, revocation, auditable durable state | Adds protocol and persistence work | Chosen |

## Evidence

The active Host exposes turns, sessions, surfacings, beliefs, approvals, tool
proposals, traces, and events on `127.0.0.1:5810`; the endpoint inventory is recorded
in `docs/workstation-runbook.md`. MCP and model sidecars also run locally. The decision
to use OIDC and PostgreSQL is Steve's explicit architecture choice. No library spike or
throughput benchmark has been run yet; package and schema selection remain
implementation evidence to collect, not facts asserted by this ADR.

## Consequences

- Authentication is required on localhost as well as any future remote binding.
- The CLI uses device authorization; a browser GUI uses authorization code with PKCE.
- Headless first-party services receive separately provisioned, narrowly scoped client
  identities. MCP and inference sidecars receive no runtime-API credential by default.
- Approval resolution requires a dedicated `dami.approvals.resolve` scope and a user
  grant; background-service credentials cannot resolve approvals.
- `/health` may remain anonymous only while it exposes readiness and no private state.
- Access tokens are short-lived. Refresh credentials are rotated and revocable; stored
  secret material is hashed or encrypted rather than persisted in plaintext.
- Signing and encryption private keys do not live in the application database or the
  repository.
- The implementation must use a maintained OIDC/OAuth library. Dami will not implement
  token issuance, signature validation, PKCE, or device authorization itself.

## Reversal path

Authentication remains behind ASP.NET Core authentication and authorization handlers.
The issuer or OIDC library can be replaced while retaining Dami's scopes and client
policy. PostgreSQL auth tables are isolated from domain and event schemas so they can
be migrated or removed independently. Disabling authentication is not an acceptable
production reversal; a development-only test handler may replace it in isolated tests.
