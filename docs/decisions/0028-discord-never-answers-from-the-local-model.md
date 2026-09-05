# ADR 0028 — Discord never answers from the local model

- **Decision:** The Discord gateway answers from the frontier or reports that it could not.
  The local-model fallback ADR-0026 kept "for a subscription hiccup" is removed, and the
  seam it ran through (`ITracedTurnRunner`) is no longer a dependency of the gateway.
- **Date:** 2026-09-04
- **Status:** accepted
- **Amends:** ADR-0026 (the local model feeds the answer; it does not write it)

## Context

Steve, 2026-09-04: *"this agent is STILL using local model from discord IT SHOULD NEVER DO
THAT."*

ADR-0026 moved the answer to the frontier but kept qwen3:8b as the fallback "when the
frontier is unreachable", reasoning that a hiccup should degrade the answer rather than
the gateway. The N11 repair on 2026-09-02 widened that fallback to cover a frontier
deadline. In practice the fallback did not fire on hiccups. It fired on:

- **2026-09-03 23:04:23** — Discord returned `429 Too Many Requests` to a progressive
  *edit* of the reply the frontier was already streaming. The exception left
  `DiscordReplyStreamer`, was caught as "frontier failed", and the finished frontier
  answer was thrown away in favour of a local one.
- **2026-09-03 23:19:40** — the Codex app-server produced no delta for the full 600-second
  deadline. The turn was cancelled, caught as "frontier timed out", and answered locally.

Both times the reply carried the "_(answered locally — the frontier was unreachable)_"
note, which is how Steve saw it. Neither was a case where a local answer was better than
saying what happened.

## Alternatives considered

| Option | Strengths | Weaknesses | Why not chosen |
|---|---|---|---|
| Keep the fallback, fix the two triggers | Least change | The next trigger produces the same qwen3 answer; the rule Steve stated is "never", not "less often" | Fixes symptoms of a path that should not exist |
| Retry the frontier before falling back | Fewer fallbacks | Still ends in a local answer; doubles a ten-minute hang | Same objection |
| Keep `Discord:Frontier=false` as a reversal switch | ADR-0026's stated reversal path survives | With no local answer path, the switch would mean "answer nothing"; a flag whose only effect is silence is a trap | Removed with the path |
| Remove the path and the seam; report failures (chosen) | The rule is structural — the worker cannot answer locally because it has nothing to answer with | A frontier outage means no Discord answers at all | That is what Steve asked for |

## Evidence

- `journalctl -u dami-host`, 2026-09-03T23:04:23-05:00: `Frontier turn failed; answering
  locally` with `HttpRequestException: 429 (Too Many Requests)` from
  `DiscordRest.EditMessageAsync` via `DiscordReplyStreamer.StreamAsync`.
- Same journal, 23:19:40: `Frontier turn timed out; answering locally` from
  `CodexAppServer.ReadDeltasAsync`, followed immediately by `POST
  http://127.0.0.1:11434/api/generate` and `/api/chat` — the local model answering.
- The prior frontier turn completed at 23:05:17; the timed-out one therefore began around
  23:09:40 and ran the full `Codex:TimeoutSeconds` default of 600.
- `Should_Have_No_Local_Model_To_Answer_With` pins the constructor:
  `DiscordGatewayWorker` takes no `ITracedTurnRunner`. `DiscordCompositionTests` builds the
  gateway without one registered.
- `EditMessageAsync_Should_Wait_And_Retry_When_Rate_Limited` covers the 429 trigger at its
  source, so a rate-limited edit no longer discards the frontier's answer.
- Full solution after the change: 0 warnings, 0 errors, 1,576 tests passing across 21
  assemblies.

## Consequences

- A frontier failure on Discord produces one Operational message: what went wrong, in one
  line, with the trace id. Nothing is journaled for that turn, so the next message's
  window does not contain "the frontier did not answer" as if Dami had said it.
- A ten-minute Codex hang is now visible as a ten-minute silence followed by an
  explanation. That hang is a separate defect and is not fixed here; it is now reported
  rather than papered over.
- The local model still does what ADR-0026 assigned it on this path: retrieval planning,
  disclosure gating, and image captioning. This decision is about who writes the reply.
- `DiscordOptions.Frontier` no longer exists. A drop-in that set `Discord__Frontier` is
  ignored.

## Reversal path

Re-add an `ITracedTurnRunner` parameter to the worker and a catch that calls it; the
pinning test will fail and has to be deleted deliberately. Cost: an afternoon. There is
no configuration switch, on purpose.
