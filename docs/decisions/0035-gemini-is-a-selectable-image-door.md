# ADR 0035 — Gemini is a selectable image door, and the door is a configuration choice

- **Decision:** `GeminiImageGenerator` in `Dami.Providers` is a third implementation of
  `IImageGenerator`, gated exactly like the OpenAI one (Egressable-only, allowlisted host,
  absent key = absent capability, C5 budget, every call in the event stream). Which
  implementation stands behind the seam is now one configuration value, `Images:Provider`
  (`Codex` by default, `OpenAi`, `Gemini`), registered through one shared
  `AddImageGenerator` so the Host and the proactive tier cannot drift apart.
- **Date:** 2026-09-16
- **Status:** accepted
- **Amends:** ADR-0027 (image generation as a third door), ADR-0029 (the portrait on the
  subscription)

## Context

Steve, 2026-09-16: *"please add gemini image generation provider."*

Two doors existed. The keyed OpenAI one (ADR-0027) is not registered anywhere; the Codex
subscription one (ADR-0029) is hard-wired in both composition roots —
`Dami.Host/Program.cs` and `ProactiveComposition.AddDailyPortrait`. A third
implementation is worthless if nothing can select it, and selecting it by editing two
composition roots is how the two tiers end up drawing Dami through different providers.

The Codex door has its own cost: each picture is a `codex exec` with a 480 s ceiling,
sandboxed, driven by a prompt that asks an agent to call a tool and copy a file. A keyed
HTTP door is one request and one response.

## Alternatives considered

| Option | Strengths | Weaknesses | Why not chosen |
|---|---|---|---|
| Register Gemini outright in place of Codex | Smallest change | Changes what runs the moment it deploys, with no key on the host; the portrait pass would refuse every slot | Nothing should change until Steve says so |
| Select the provider by which key is present | No new knob | Silent: a key in the wrong drop-in flips the provider; two keys are ambiguous | Precedence by accident is not a decision |
| Per-caller provider (Gallery on one, portrait on another) | Flexibility | Two Damis drawn by two models diverge; the anchor argument in ADR-0029 depends on one door | Not asked for, and the identity argument says no |
| One `Images:Provider` value, one shared registration (chosen) | Default unchanged; both tiers agree; one drop-in line to switch | One more options class | — |
| Imagen `:predict` instead of `generateContent` | Purpose-built image endpoint | No inline reference image for the identity anchor; a second wire format | The anchor is the point of the portrait |

## Evidence

- `EgressSeamTests.Image_Generator_Holders_Should_Be_The_Pinned_Set` pins the holders;
  `Dami.Providers.GeminiImageGenerator` is added there, and the test is the record that
  the addition was deliberate.
- `GeminiImageGeneratorTests` (26 tests): refusal on non-Egressable, un-allowlisted host,
  missing key and spent budget, none reaching the network; refusal, completion and
  failure each recorded; the prompt never in a label; the key in the `x-goog-api-key`
  header and never in the URL; `responseModalities: ["IMAGE"]`; `1024x1536` becomes
  aspect ratio `2:3` and an unknown size sends none; one reference travels as an
  `inlineData` part preceded by the edit-or-anchor rule; `Read` accepts both
  `inlineData` and `inline_data`, reports the returned content type, and names
  `finishReason` or `promptFeedback.blockReason` when no image came back.
- `ProactiveCompositionTests.Enabled_DailyPortrait_Should_Draw_On_Gemini_When_Selected`
  and `…OpenAi_When_Selected` resolve the enabled tier with `Images:Provider` set and
  assert the generator type; the ADR-0029 test still asserts the unset default is Codex.
- Wire format from the Gemini API reference as read on 2026-09-16:
  `POST /v1beta/models/{model}:generateContent`, `generationConfig.responseModalities`,
  `generationConfig.imageConfig.{aspectRatio,imageSize}`, response
  `candidates[].content.parts[].inlineData.{mimeType,data}`. The default model,
  `gemini-3.1-flash-image`, is the first the image-generation page lists; it is one
  option value to change.
- **Not verified live.** No Gemini key exists on this host and none was created. The
  wire format is tested against canned responses only. The first real call is the
  proof, and it is recorded in the runbook's verification block.

## Consequences

- Switching is two drop-in lines and a secret: `Images__Provider=Gemini`,
  `Egress__AllowedHosts__N=generativelanguage.googleapis.com`, and
  `GeminiImages__ApiKey` out of band. Without the allowlist line the door refuses and
  says so in the event stream; without the key it refuses as absent capability.
- Gemini bills per image where the subscription does not (ADR-0027's "one door, one
  bill" now has two billed doors). The C5 budget applies to it as to OpenAI.
- `ImageRequest.Size` and `Quality` are OpenAI vocabulary. Size survives as an aspect
  ratio; Quality does not map and is ignored, with the pixel count coming from
  `GeminiImages:ImageSize` (`1K` by default). A caller that wants Gemini-specific output
  changes the option, not the request.
- The returned encoding is the provider's. Gemini may answer with JPEG; the
  `GeneratedImage` carries the real content type and a matching extension, and the
  Gallery sidecar records whatever was returned.
- `OpenAiImageGenerator` is registrable again (`Images__Provider=OpenAi`), reversing the
  "no longer registered" line of ADR-0029 without changing its default.

## Amendment, 2026-09-16 evening — a backup door

Steve: *"keep codex as the primary, anytime a image request is refused use gemini as a
backup"*, then *"if the image doesn't generate from the primary you retry it using
gemini."* `Images:Backup` names a second door; `FallbackImageGenerator` calls the primary
and, on any refusal or failure that is not the caller's own cancellation, calls the
backup with the same request. One exception is kept: a prompt that is not Egressable was
refused for what it contains, and a second door is not an answer to that (D-012). A
backup equal to the primary registers the primary alone. Each door records its own
events, so the ledger shows the primary's failure and then the backup's request.
Evidence: `FallbackImageGeneratorTests` (10) and two composition tests. The Gemini key
was found in the working tree at `Dami/gemini-key.txt`, untracked; it was moved to
`~/.config/dami/gemini-key.txt` and into both env files, and the file name is now ignored.

## Reversal path

Leave `Images__Provider` unset and nothing has changed at runtime. Deleting the
provider is three files in `Dami.Providers`, one enum member, one line in
`EgressSeamTests` and two composition tests.
