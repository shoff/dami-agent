# ADR 0010 — How frontier model calls cross the egress boundary

- **Decision:** Frontier providers get their own gate — `IFrontierChat` implemented by provider adapters that enforce the allowlist and emit egress events themselves — rather than widening `EgressRequest` to carry a body.
- **Date:** 2026-08-23
- **Status:** proposed — design for the first frontier adapter; no code yet
- **Supersedes:** none (complements D-012 and the `Dami.Privacy` implementation)

## Context

D-012 says a frontier-model call "is an egress event and is subject to the same check." Two mechanisms now exist and they are about to collide:

- `IEgressClient` / `EgressRequest` is deliberately narrow: a destination URI and a purpose, **no body, no headers**. The narrowness is documented as part of the enforcement — a feed fetch structurally cannot exfiltrate a payload.
- A frontier chat call is *all* body. The prompt is the payload, and sending it is the point.

Widening `EgressRequest` with an optional body would give every holder of the general egress client a payload channel, quietly deleting the property that made the scout's fetches trustworthy. The boundary's two cases are genuinely different shapes and should not share one door.

The router already encodes the precondition: `ModelTier.Frontier` is only ever returned for `PrivacyClass.Egressable` work, and `LocalOnly` cannot reach it under any configuration (tested).

## Decision detail

1. **`EgressRequest` stays bodyless.** The general egress door remains fetch-only.
2. **`IFrontierChat`** (in `Dami.Contracts.Models`) is the second door: `CompleteAsync(FrontierPrompt, CancellationToken)`. `FrontierPrompt` carries the prompt text, the `PrivacyClass` under which it was assembled, and the `TraceId`/`Origin` for the event trail.
3. **Adapters enforce, not callers.** A provider adapter (e.g. `AnthropicChatClient` in `Dami.Providers`):
   - **throws `EgressRefusedException` if `PrivacyClass != Egressable`** — the router should make this unreachable; the adapter makes it impossible;
   - checks its destination against the same `EgressOptions.AllowedHosts` (`api.anthropic.com` must be allowlisted deliberately, like any host);
   - emits `EgressRequested` / `EgressCompleted` / `EgressRefused` into the caller's trace, so "what left this machine" stays one query — with the **purpose line, never the prompt text**, in the event label;
   - counts tokens/latency into event metadata for the N-01 accounting.
4. **Credentials** arrive via user-secrets or environment (`Anthropic:ApiKey`), never the repository. `RoutingOptions.FrontierEnabled` stays false until an adapter with credentials is registered — the composition root remains the audit point: no `IFrontierChat` registration, no frontier capability.
5. **Context assembly is the classification point.** `AssembledContext` is built from profile-derived stores, so a prompt containing retrieved memories or beliefs is `LocalOnly` *by construction* unless a redaction/consent step explicitly downgrades it — that step does not exist yet, and until it does, frontier calls can carry only non-retrieved content (e.g. a bare technical question). This is restrictive and correct; loosening it is its own future decision with its own ADR.

## Alternatives considered

| Option | Why not |
|---|---|
| Widen `EgressRequest` with a body | Hands every egress-capable service a payload channel; deletes the structural guarantee the scout relies on |
| Route frontier HTTP through `HttpEgressClient` internals | Same objection with extra plumbing; also forces chat semantics through a fetch-shaped interface |
| No allowlist for provider hosts ("it's just Anthropic") | The allowlist is the boundary's failure-loudly property; providers are not exempt from it |

## Consequences

Two doors, each with the narrowest shape its job allows. The privacy invariant becomes checkable at three layers: router (policy), adapter (mechanism), composition root (capability). The cost is a second interface and the discipline of keeping prompt text out of event labels.

## Reversal path

Contracts-only so far; nothing to reverse until an adapter exists. If the two-door design proves wrong, collapsing to one widened interface is mechanical — the reverse migration is the dangerous one, which is why this ADR exists before the code.
