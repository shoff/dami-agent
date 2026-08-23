# ADR 0011 — Frontier capability via the Codex CLI and Steve's ChatGPT subscription

- **Decision:** The first working frontier provider is the locally installed Codex CLI (`codex exec`), authenticated by Steve's existing browser login (`auth_mode: chatgpt`), invoked as a subprocess. No API keys, no per-token billing — usage is covered by the subscription, exactly as Hermes does it.
- **Date:** 2026-08-23
- **Status:** accepted — directed by Steve ("use my codex subscription max… NO API token costs")
- **Supersedes:** none; complements ADR-0010. The `AnthropicChatClient` adapter remains built and gated, dormant until credentials ever arrive.

## Context

ADR-0010 designed the frontier door around an HTTP adapter with an API key. Steve's direction changes the economics and the mechanism: his ChatGPT subscription (Codex Max) already covers frontier usage, the Codex CLI is already installed (`~/.local/bin/codex`, v0.149.0) and already authenticated on this machine via browser OAuth (`~/.codex/auth.json`, `auth_mode: chatgpt`, refresh tokens present). Verified live before this ADR: `codex exec "Reply with exactly one word: pong"` → `pong`, 5,004 tokens, billed to the subscription.

## How the ADR-0010 gate maps to a subprocess

A subprocess does not pass through `HttpEgressClient`, so the boundary's mechanisms map rather than transfer:

| ADR-0010 mechanism | Subprocess equivalent |
|---|---|
| Refuse non-`Egressable` prompts | identical — the adapter refuses before spawning anything |
| Provider host on the egress allowlist | **not enforceable** — the CLI owns its own transport. Replaced by an explicit `Codex:Enabled` flag (default false) plus the composition-root registration, both deliberate visible acts |
| Egress events in the caller's trace | identical — `EgressRequested`/`Completed`/`Refused`, purpose line only, never the prompt |
| Credentials never in the repo | stronger — the adapter never touches credentials at all; `auth.json` belongs to the CLI and to Steve |

Additional containment the HTTP path never had: the subprocess runs `--sandbox read-only`, `--cd` a scratch directory, `--skip-git-repo-check` — the frontier model gets the prompt and nothing else: no workspace, no writes, no repo.

**ADR-0010 §5 is unchanged:** context assembled from the memory stores is `LocalOnly` by construction. Until a redaction step exists, subscription-frontier calls carry only non-retrieved content — a bare question, never memories or beliefs.

## Alternatives considered

| Option | Why not |
|---|---|
| Anthropic API adapter (already built) | metered billing — exactly what Steve declined |
| Reuse `auth.json`'s OAuth token against the backend directly | fragile, undocumented surface, and the CLI already exists as the sanctioned wrapper |
| Codex app-server / proto mode | richer but heavier; `exec --output-last-message` is sufficient for completion-shaped calls and trivially testable |

## Consequences

Frontier synthesis at zero marginal cost, tomorrow's models included with the subscription. Costs accepted: subprocess latency (seconds of CLI startup per call), a dependency on the CLI's flag surface staying stable, and token accounting living in OpenAI's dashboard rather than our events (the events record that and when egress happened, not its size).

## Reversal path

The adapter implements `IFrontierChat` like any other; deregistering it in the composition root removes the capability, and the Anthropic adapter can take its place whenever metered billing ever becomes acceptable.
