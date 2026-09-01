# ADR 0026 — The local model feeds the answer; it does not write it

- **Decision:** The Discord gateway answers from the frontier model on context the local
  sidecar assembled, gated, and redacted. The local model's job on this path is
  retrieval, image captioning, and disclosure classification — never the reply itself.
  It remains the fallback when the frontier is unreachable.
- **Date:** 2026-08-31
- **Status:** accepted
- **Extends:** ADR-0013 (local augmentation and disclosure), ADR-0025 (the owner's own
  channel is not a third party)

## Context

Steve, 2026-08-31: *"the local model should not be directly answering me it should be
used to ADD CONTEXT."*

The gateway asked for the unkeyed `ITracedTurnRunner`, which resolves to the local
`TurnRunner` (`Dami.Host/Program.cs:55`). Nothing chose that for Discord; it is simply
the default seam, and the frontier runner was only ever reachable through the keyed
`ISessionTurnRunner("frontier")` that `dami chat --frontier` uses. The CLI got an opt-in
when it was asked for (G12); Discord never did.

The machinery this decision points Discord at already exists and already says what it is
for. `AugmentedFrontierTurn`, built for C4/ADR-0013, carries this in its own remarks:
*"Retrieval — embedding, ANN, rerank, the recency and grounding gates — all happens on
this host, and its output is what the frontier is given to think about. The local model
is infrastructure here, not the brain: it never writes the answer."* It was wired to
`POST /turns` with `augmented: true` and to nothing else.

So the change is a wiring decision with a privacy consequence, not new architecture.

## Alternatives considered

| Option | Strengths | Weaknesses | Why not chosen |
|---|---|---|---|
| Leave it local | No new egress; simplest | The observed outcome is the one Steve reported — a gateway that cannot do the job. qwen3:8b writing final answers unaided is the whole complaint | The status quo is what was rejected |
| Keyed `ISessionTurnRunner("frontier")`, as the CLI uses | Reuses G13 exactly; durable sessions come free | That path sends **identity + question with no retrieved memory** (ADR-0010). It would fix the model and lose the memory — a Dami that knows nothing about Steve | Solves half the problem and breaks the better half |
| Frontier with raw retrieved context | Best answers | Sends unredacted profile text to a third-party model | D-012; ADR-0013 exists precisely to refuse this |
| Augmented turn — local retrieval, local gate, frontier answers (chosen) | The local model does what it is good at and the frontier does what it is good at; every disclosure decision is recorded and correctable | A model call per turn for the gate, and a real new exposure (below) | — |
| Local-first, escalate on low confidence | Cheapest | "Confidence" here would be the same 8B model judging itself, which is the failure mode D-011 was written about | No trustworthy signal to escalate on |

## Evidence

- `Dami.Host/Program.cs:55` — the unkeyed `ITracedTurnRunner` resolves to `TurnRunner`;
  `DiscordGatewayWorker`'s constructor takes that seam, so the local model answered by
  construction rather than by choice.
- `DiscordGatewayWorker.AnswerAsync` passed `ConversationWindow.Empty` — every Discord
  message was turn one, with no memory of the previous one.
- `InboundMessage` was `(AuthorId, ConversationId, Text, ReceivedAt)` and
  `DiscordGatewayProtocol.ReadMessage` never read Discord's `attachments` array: an
  image was invisible to the entire path before any policy could consider it.
- `IVisionClient` was registered in `Dami.Host.Proactive` and `Dami.Gateway.Cli` but not
  in `Dami.Host`, the process that runs the gateway — so even a parsed attachment had
  nothing on hand to look at it.
- Installed models on this host, 2026-08-31: `qwen2.5vl:7b`, `qwen3:8b`. Vision input and
  text. No diffusion weights, no ComfyUI or Stable Diffusion container.

## Consequences

Dami on Discord answers with a frontier model's competence over Steve's own context, and
can read images he sends it.

**The cost, stated plainly, because it is larger than ADR-0025's.** ADR-0025 recorded that
Discord Inc. holds profile-derived *answers*. This decision adds a second holder: **the
frontier provider receives profile-derived *context* — the retrieved memories and beliefs
that shaped the answer, not merely the answer.** The mitigations are real but they are
mitigations, not guarantees:

- Every retrieved item passes `LocalDisclosureGate` first, which withholds or disguises
  per item, and each verdict is recorded in the disclosure ledger where Steve can correct
  it (those corrections are what the gate reads back as examples, G9a).
- The exact bytes that left are stored hash-pinned as an `EgressBrief`, so what was sent
  is auditable after the fact rather than promised beforehand.
- `AugmentedTurn:Gate` can be turned off, which sends everything retrieved. It defaults
  on, and turning it off is Steve's decision and no one else's.
- Conversation history is treated with the same suspicion as retrieved memory: prior
  exchanges go through the gate rather than around it, because "what Dami said last
  message" is profile-derived too.

Image *captions* are produced locally by qwen2.5vl and are then context like any other —
they pass the gate before any of them can reach the frontier. An image itself never
leaves this host.

What does not change: `ChannelDisclosurePolicy` still drops every inbound message that is
not from `OwnerUserId`, so the only conversation this affects is Steve's own. A future
channel whose recipient is not Steve still refuses under ADR-0025 and still needs its own
ADR.

**Not decided here: image generation.** No backend exists on this host, and every option
is a real commitment — a paid API is both a credential and an egress event, and local
weights compete for a 16 GiB VRAM budget already holding TTS, embedding, reranking, and
vision (onboarding §7). The outbound attachment path is built and an `IImageGenerator`
seam is defined so the decision is cheap to act on, but choosing the backend is Steve's.

## Reversal path

One registration. `DiscordGatewayWorker` takes `IAugmentedTurn` and `ITracedTurnRunner`
both; the fallback to the local runner is already the code path that runs when the
frontier is unreachable. Setting `Discord:Frontier` to false takes that path always,
which restores the pre-decision behaviour exactly, and the tests for both readings are
kept side by side.
