# Discord media and response-latency diagnosis

Date: 2026-09-01  
Board: `f95a8966` (M1c)  
Hermes reference: `NousResearch/hermes-agent` commit
`c56f8cdd4817fcc6a50d79ca9e57b1c173850e4a`, cloned to `/tmp/hermes-agent`

## Answer

These are separate missing or unhealthy paths, not one Discord defect.

1. **Dami does not expose typing at all.** `IEgressChannel` has only `SendAsync` and
   `ListenAsync`; the Discord REST client has no call to
   `POST /channels/{channel.id}/typing`. Hermes starts a refresh task before invoking
   the agent and its Discord adapter refreshes the typing endpoint until completion.
2. **Dami deliberately buffers the Discord answer.** `DiscordGatewayWorker.ThinkAsync`
   calls `IAugmentedTurn.RunAsync` or `ITracedTurnRunner.RunTracedAsync`, receives a
   completed string, and only then calls `SendAsync`. The newly implemented
   `IAugmentedTurn.StreamAsync` is not used by Discord. Hermes wires model deltas to a
   `GatewayStreamConsumer`, which sends an initial message and progressively edits it.
3. **Inbound images reach the vision model, but the live model path is catastrophically
   slow and destabilizes the shared Ollama service.** The observed 12:27 image was
   downloaded in 476 ms, then `qwen2.5vl:7b` took 212.6 seconds. Ollama expanded the
   image to 4,064 prompt tokens and filled the 4,096-token context, producing only 32
   output tokens. Dami then ran two local planning calls (15.8 s and 17.3 s), started a
   third, and the Ollama container restarted after that request ran for 126 seconds.
   Both the frontier preparation and local fallback then failed, so Discord received no
   answer. This is why it appears unable to understand the image even though parsing,
   download, and vision dispatch all occurred.
4. **Interactive image creation is not connected.** `IImageGenerator` is registered only
   in `Dami.Host.Proactive` for `DailyPortraitService`. The Discord worker and interactive
   tool registry cannot request generation. The final upload rail already exists:
   `OutboundContent.Attachments` flows through `DiscordEgressChannel` to multipart
   Discord upload. No interactive component creates an `OutboundAttachment` from a
   generated image. Hermes registers an `image_generate` tool, extracts its returned
   local path independent of whether the model repeats it, and routes image paths to the
   Discord adapter as native attachments.

## Code evidence

### Dami

- `Dami/src/Dami.Host.Discord/DiscordGatewayWorker.cs:190-198` waits for
  `ThinkAsync` to return a full answer before sending.
- `Dami/src/Dami.Host.Discord/DiscordGatewayWorker.cs:210-242` calls only completed
  response APIs.
- `Dami/src/Dami.Core/Frontier/AugmentedFrontierTurn.cs:155-195` proves a streaming
  frontier API already exists and yields fragments.
- `Dami/src/Dami.Contracts/Privacy/IEgressChannel.cs:85-98` exposes no typing, initial
  message, or edit operation.
- `Dami/src/Dami.Gateway.Discord/DiscordRest.cs:74-130` can post text and multipart
  attachments but cannot post typing or edit an existing message.
- `Dami/src/Dami.Host.Discord/DiscordVision.cs:43-92` downloads and captions inbound
  images sequentially before any response is visible.
- `Dami/src/Dami.Vision/OllamaVisionClient.cs:42-49` sends the original image directly
  to Ollama with `stream = false`; it performs no resize/token-budget normalization.
- `Dami/src/Dami.Host.Proactive/ProactiveComposition.cs:215-224` is the only production
  registration of `IImageGenerator`.
- `Dami/src/Dami.Gateway.Discord/DiscordEgressChannel.cs:70-84` already forwards
  outbound attachments to the Discord REST multipart implementation.

### Hermes

- `/tmp/hermes-agent/gateway/platforms/base.py:6509-6529` starts continuous typing
  before the message handler.
- `/tmp/hermes-agent/plugins/platforms/discord/adapter.py:5646-5674` implements the
  Discord typing endpoint refresh loop.
- `/tmp/hermes-agent/gateway/run.py:5868-5928` creates a stream consumer when streaming
  is enabled; `run.py:6271` attaches its delta callback to the agent.
- `/tmp/hermes-agent/gateway/stream_consumer.py` buffers/rate-limits deltas and updates
  one platform message rather than posting every token.
- `/tmp/hermes-agent/gateway/run.py:22120-22131` eagerly analyzes inbound images;
  `run.py:26657-26726` injects the description and preserves a local path for a more
  targeted follow-up vision call.
- `/tmp/hermes-agent/gateway/run.py:2132-2147` deterministically extracts the local
  result path from `image_generate` tool output.
- `/tmp/hermes-agent/gateway/platforms/base.py:6889-6940` routes generated/local images
  to native attachment delivery.
- `/tmp/hermes-agent/plugins/platforms/discord/adapter.py:4094-4172` batches image
  attachments and `adapter.py:5440-5521` uploads local or remote images inline.

## Live evidence

From `journalctl -u dami-host` for 12:27:49–12:34:02 CDT:

- Discord CDN download: 476 ms, HTTP 200.
- `IVisionClient` / Ollama `qwen2.5vl:7b`: 212,603 ms, HTTP 200.
- Local planner calls immediately after vision: 15,806 ms and 17,292 ms.
- Next local disclosure-gate call: failed after 126,081 ms with a premature response.
- Local fallback: failed immediately because Ollama had reset; the worker logged
  `Discord turn failed` and sent nothing.

From `docker logs dami-llm` for the same window:

- Ollama evicted the resident text model to load vision.
- Vision prompt evaluation was 4,064 tokens / 198.5 seconds within a 4,096-token
  context; generation was 32 tokens / 4.8 seconds.
- Ollama then reloaded `qwen3:8b`, served two planning requests, returned HTTP 500 for
  the third after 126 seconds, and restarted.
- The container has both `qwen2.5vl:7b` and `qwen3:8b`; the 16 GiB GPU also carries the
  embedder, reranker, and STT processes. Model swapping and oversized image encoding,
  not Discord download time, dominate this image turn.

## Test-first remediation plan

The work should be split so latency/media fixes remain independently reviewable.

1. **Typing lifecycle.** Add tests that typing starts before vision/retrieval, refreshes
   while a turn runs, and stops on success, refusal, exception, cancellation, and
   fallback. Add narrow Discord REST typing support or a focused Discord-presence
   abstraction; do not widen every egress channel unless another channel needs it.
2. **Progressive Discord delivery.** Add a worker test whose fake augmented stream
   blocks after its first fragment and assert Discord already received an initial
   message/update. Add REST create-message returning message ID plus edit-message. Buffer
   fragments by time/size and edit at a conservative cadence; finalize once, split at
   Discord's 2,000-character limit, and journal the reconciled final answer. Do not send
   one REST request per token.
3. **Vision latency and isolation.** First add an integration measurement for a real
   phone-sized image: preprocessing time, model-load time, TTFT, total caption time, and
   encoded vision-token count. Resize/downsample before Ollama so the image cannot consume
   the entire context, cap caption work, and return a user-visible degraded result on
   timeout. Keep vision from killing text inference: validate either a smaller measured
   vision model, explicit unload/serialization policy, or a separately managed vision
   sidecar against the 16 GiB budget before choosing. The evidence does not yet justify
   one of those three by assumption.
4. **Interactive image generation.** Add a capability test showing an explicit image
   request selects a focused generation abstraction and returns bytes plus MIME type;
   add a Discord worker test proving those bytes become `OutboundAttachment`. Reuse the
   existing privacy-class enforcement, `IImageGenerator`, and multipart upload. Add the
   generator to the interactive composition root only with the existing configured-host,
   credential, audit, and egress-budget constraints; do not expose the proactive service
   itself as a chat tool.
5. **Acceptance measurement.** For text, record message receipt → typing visible → first
   Discord text → final text. For images, record receipt → caption ready → first Discord
   text → final text. A streamed frontier still cannot emit before local retrieval and
   disclosure gating finish, so the current measured ~3.9-second gate-to-first-fragment
   floor remains separate from Discord buffering.

No production code or deployment was changed during this diagnosis.
