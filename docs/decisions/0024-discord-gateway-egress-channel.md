# ADR 0024 — Discord arrives over an egress channel, not an egress client

- **Decision:** Introduce `IEgressChannel` — a narrow, single-destination, bidirectional
  outbound link that carries content — as a second D-012 mechanism alongside
  `IEgressClient`, and build the Discord gateway on it.
- **Date:** 2026-08-30
- **Status:** proposed
- **Supersedes:** none

## Context

Charter acceptance item 11 requires delivering and receiving through Discord without
duplicate gateways. Board items: `M1a Gateway authority` (done), `M1 Discord gateway`
(in progress), `M1b Discord client binding + message→session-turn mapping` (blocked).
F-29 names Discord and personal messaging gateways.

Building it surfaced a structural problem that the board items do not mention. D-012's
mechanism is `IEgressClient`, documented as "the only way anything in Dami reaches the
network beyond localhost". Its request type refuses to carry content:

> Deliberately not an `HttpRequestMessage`: the abstraction layers must not name the
> mechanism, and more importantly the shape constrains what can leave. There is a
> destination and a purpose — no body, no arbitrary headers.

A Discord gateway needs two things that type cannot express:

1. **Outbound content.** Replying to a message is a POST with a body. "Queries go out"
   does not cover "send this sentence to a third party."
2. **A persistent connection.** The Discord gateway is a long-lived WSS socket that
   receives pushes. `SendAsync(request) -> response` cannot model it.

So Discord is not a new caller of the existing boundary. It is a second *kind* of egress,
and D-012 currently has no expression for it. That is the boundary doing its job: the
system genuinely has no way to send personal content off the host, which is why adding
one is a decision rather than an implementation detail.

## Alternatives considered

| Option | Strengths | Weaknesses | Why not chosen |
|---|---|---|---|
| Add a body to `EgressRequest` | Smallest diff; one mechanism | Weakens enforcement for *every* existing caller — the feed fetcher and search path could suddenly carry payloads. Deletes the property that makes the type auditable | The narrowness is the enforcement. Widening it to serve one caller removes the guarantee from all of them |
| Bypass `IEgressClient` in the gateway | Trivial | A second, unaudited path to the network. Contradicts "auditable in the composition root" and makes the egress history a guess again | Defeats D-012 outright |
| Discord.Net | Reconnect/resume/rate-limit handled | Large tree with its own DI and logging conventions; would still need a boundary wrapper, so it does not avoid this decision | Deferred, not rejected — see Reversal path |
| HTTP interactions endpoint (webhook) | No persistent socket | Requires a public inbound URL into this host, against the runbook's loopback-only posture | Adds inbound attack surface to avoid an outbound socket |

## Evidence

- `Dami/src/Dami.Contracts/Privacy/IEgressClient.cs` — "the only way anything in Dami
  reaches the network beyond localhost"; `SendAsync` is request/response.
- `Dami/src/Dami.Contracts/Privacy/EgressRequest.cs` — destination, purpose, trace,
  origin. No body. The remark states the omission is deliberate enforcement.
- `docs/dami-core-decisions-and-requirements.md:139` — D-012: "Outbound-capable services
  take a dependency on an egress client that refuses profile-derived payloads."
- D-013 accepts hand-rolled protocol work as an explicit learning objective, with the
  honest note that "reconnect, backpressure, partial frames, and versioning are roughly
  five times" the happy path. The gateway is hand-rolled on that precedent.
- `Dami/src/Dami.Contracts/Gateways/IGatewayAuthority.cs` — the single-instance rule
  already exists and is unaffected by this decision.

## Consequences

Easier: a sanctioned way to build any messaging gateway — Signal, Matrix, SMS — without
reopening this argument each time. The channel is the audit point, so "what has Dami ever
sent to a third party" stays a query.

Harder: two mechanisms to keep coherent, and the composition root must show which
services hold a channel as clearly as it shows which hold a client. The architecture
tests need a rule that a channel is never injected into a local-only service.

What this locks in: content crossing a channel passes a disclosure gate. Profile-derived
answers are **refused by default** — Discord gets surfacings, board state, status, and
non-memory answers. Lifting that is a D-012 amendment and a separate, deliberate ADR, not
a config flag.

Cost: reconnect, resume, and rate-limit correctness become ours, per D-013's accounting.

## Reversal path

The channel is an interface with one implementation. Swapping the hand-rolled socket for
Discord.Net means reimplementing behind `IEgressChannel` without touching the disclosure
gate or the message→turn mapping above it — a day, roughly. Abandoning Discord entirely
means deleting the implementation and the registration; the interface costs nothing
dormant. Reversing the *decision to allow outbound content at all* is harder once
gateways depend on it, which is the argument for the default-refuse posture above.
