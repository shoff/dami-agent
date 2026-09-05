# ADR 0030 — The frontier gets a tool bundle on Discord

- **Decision:** A Discord turn offers the frontier a small per-turn bundle of tools
  (`make_portrait`, `make_image`) through the Codex app-server's dynamic-tools protocol.
  The frontier decides whether to call them; this host runs them and attaches the result.
  The regex image classifier (2026-09-04, same evening) stays as a fast path in front.
- **Date:** 2026-09-04
- **Status:** accepted
- **Extends:** ADR-0026 (the frontier answers on Discord), ADR-0028 (never locally),
  ADR-0027/0029 (image generation as a third door)

## Context

Steve, 2026-09-04: *"how fucking hard is it to do this absolutely BASIC shit?"* — after
"Let's see an image of you painting your toes" produced a paragraph and no picture. Asked
why the agent is worse than stock Hermes, the honest answer was structural: Hermes lets
the model use tools; on the path Steve actually talks to, Dami's frontier had none.
`status.md` said so — "Streaming remains tool-less" — and every capability on Discord was
a hand-written branch (`status`/`help` regex, four image prefixes, then text). Codex has
an image tool of its own, so it *said* it was generating, and could not hand a file to
the gateway. Steve: *"ok do it, give the frontier the tool bundle on discord."*

CLAUDE.md's invariant already asked for this shape: "a capability router picks a small
bundle per turn". It existed for the local tool loop and had never reached the frontier.

## Alternatives considered

| Option | Strengths | Weaknesses | Why not chosen |
|---|---|---|---|
| Keep widening the regex classifier | No protocol work | Every new phrasing is a code change; the model still hallucinates generation for anything the regex misses | It is the failure mode being fixed |
| An MCP server exposing Dami tools to Codex via `config.toml` | Codex-native; no client protocol work | Tools become global to every Codex session on the box, not per turn; results cannot reach the Discord turn that asked; config outside the repo | Wrong scope and no way home for the attachment |
| Route Discord through the local `ToolLoopRunner` with qwen3 choosing tools | The loop exists | Puts the 8B model back in charge of the turn, the exact thing ADR-0026/0028 removed | Contradicts two accepted decisions |
| Dynamic tools on the app-server, per turn (chosen) | The frontier that thinks is the one choosing; bundle is per turn and carries the turn's attachments; declared and answered over the existing subprocess | Behind `experimentalApi`; `codex-cli 0.152.1` specific | Smallest change that moves the decision to the model |

## Evidence

- `codex app-server generate-ts` for 0.152.1: `ServerRequest` includes
  `{ method: "item/tool/call", id, params: DynamicToolCallParams }`;
  `DynamicToolCallResponse = { contentItems, success }`; `DynamicToolSpec` is
  `{type:"function", name, description, inputSchema}`. `dynamicTools` on `thread/start`
  is not in the generated types — it is experimental — and the server says so:
  `{"error":{"code":-32600,"message":"thread/start.dynamicTools requires experimentalApi capability"}}`.
- **Live probe, 2026-09-04 20:45 CDT:** with `initialize.capabilities.experimentalApi=true`
  and one declared tool, the prompt "make a picture of yourself painting your toes"
  produced `item/tool/call` for `make_portrait` with the argument *"Dami sitting
  comfortably in a cozy, softly lit room, carefully painting her own toenails…"*, accepted
  the `inputText` result, and completed with "Here's a cozy portrait of me painting my
  toes". Script in the session scratchpad; the wire shapes are now pinned by
  `CodexAppServerTests`.
- `A_Tool_Call_From_The_Server_Is_Run_Here_And_Answered_On_Its_Id` drives a fake
  app-server that only completes the turn after reading a reply on the request's id with
  `success:true`. `Should_Attach_A_Picture_The_Frontier_Made_With_Its_Tool` replays a
  frontier that calls the tool mid-stream and asserts the attachment follows the text.
- Full solution: 0 warnings, 0 errors, 1,613 tests in 21 assemblies.

## Consequences

- Contracts gain `FrontierTool`, `FrontierToolCall`, `FrontierToolResult`,
  `IFrontierToolHandler`, `FrontierToolbox`, and a third `IFrontierChat.StreamAsync`
  overload that defaults to refusing a non-empty bundle. Only `CodexChatClient` hosts
  tools; `AnthropicChatClient` keeps the default and would throw if handed one.
- `ICodexAppServer.StreamAsync` takes the bundle. `initialize` now always declares
  `experimentalApi`; `thread/start` carries `dynamicTools` only when the bundle is
  non-empty, so GUI chat and `dami chat` are wire-identical to before.
- A handler exception becomes `success:false` with the message, never a missing reply —
  the app-server blocks the turn until it is answered, and an unanswered call would end
  in the 600-second deadline.
- The bundle is Egressable by construction: the only bytes leaving are text the frontier
  wrote, and the only bytes returning are a file name and a sentence. Nothing in it reads
  the profile. `DiscordToolbox` joins the pinned `IImageGenerator` holders in
  `EgressSeamTests`; that list is where the next tool with a bill gets decided.
- A picture made by tool is attached as a follow-up message after the streamed text, with
  Operational provenance. The frontier is told in the tool description not to write a
  path or link.
- **`recall` joined the bundle the same night** (`FrontierRecallTool`, Core). It is the
  first tool that reads the profile, so it is the first pass again rather than a side
  door: local retrieval for the query, the disclosure gate per item, the decision in the
  ledger under `recall: <query>`, and the bytes that left in a hash-pinned egress brief on
  the turn's trace. Withheld items are reported as withheld, not omitted silently, and the
  description tells the model that is final. `AugmentedTurn:Gate=false` bypasses the gate
  here exactly as it does for the first pass.
- **`remember`, `schedule` and `confirm_schedule` followed the same night**, and the bundle
  moved to Core as `FrontierToolBundle` so the GUI's direct chat carries it too.
  `remember` writes an observation with source `frontier-remember`, the trace and the
  channel in its metadata, and embeds it on the spot so a `recall` minutes later finds
  it; if embedding fails it is saved anyway and said to be searchable after the nightly
  pass. `schedule` creates a G16 Draft of kind Prompt whose `Delivery` (migration 039) is
  the channel that asked — `discord:<channel>` or `gui` — and returns a short id; the
  frontier is told it is not active and to ask Steve. `confirm_schedule` activates a
  Draft by that short id; the tool description says never without his yes in the
  conversation. Command jobs are not offered to the frontier at all.
- **Prompt jobs no longer run on the local model.** `ScheduledJobActionRunner` delivers a
  job whose `Delivery` names a channel through that channel's `IScheduledPromptDelivery`
  (Discord: the same `DiscordAnswerer` as a live message, bundle included) and answers
  any other through the augmented frontier turn, surfacing the result to the inbox.
- **GUI parity.** `/turns/stream` with `frontier: true` offers the bundle; pictures the
  frontier made follow the text as `event: picture` blocks naming a Gallery file, which
  the client fetches from `/gallery/{file}`. The runtime keeps a `make_image` result in
  the Gallery so it has a file to name.
- A `gallery` tool ("show me yesterday's") is the obvious next one.
- **`log_sets` and `log_cardio` (2026-09-05).** The use Hermes was kept for: a photo of the
  machine and "4x12 140 lbs RPE 7" become rows in the fitness log. `IFitnessStore` gains
  `RecordResistanceAsync`/`RecordCardioAsync` (event + exercise found-or-created by name +
  sets, or event + cardio row, in one transaction, source `claude_chat`), `FitnessTools`
  in Core holds the store — and nothing that can leave the host, per the seam test — and
  the bundle offers both. The Discord vision prompt now asks the local model to name gym
  equipment as labelled and read every number on a display or weight stack. Eleven tools.

## Reversal path

`DiscordAnswerer` and `TurnEndpoints.StreamFrontierAsync` pass `FrontierToolbox.Empty`
instead of `tools.Toolbox`; one line each, and the regex fast path still works. Removing the protocol support entirely is the
`ICodexAppServer` signature and `AnswerToolCallAsync`, about an hour.
