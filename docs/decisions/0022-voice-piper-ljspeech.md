# ADR 0022 — Dami's voice: Piper, the LJ Speech voice, on the host

- **Status:** proposed (Claude, 2026-08-25) — Steve accepts or rejects
- **Decides:** L4 (TTS engine + legally clean voice source with documented consent)

## Decision

Text to speech runs on this host through **Piper** (`piper-tts`, MIT), as a loopback-only
sidecar on `127.0.0.1:8091` (`tools/tts/server.py`, unit `tools/systemd/dami-tts.service`),
speaking with **`en_US-ljspeech-medium`**. The runtime reaches it through `ISpeechClient`
(`POST /speak`, a bounded worker under a trace); `dami say <text>` is the verb.

## Why this voice

The voice's model card (`rhasspy/piper-voices`, `en/en_US/ljspeech/medium/MODEL_CARD`,
read 2026-08-25) states: dataset `https://keithito.com/LJ-Speech-Dataset/`, **license:
public domain**, "trained from scratch … using the LJ Speech dataset". LJ Speech is a
single speaker reading public-domain books for LibriVox, published as public domain. That
is the documented-consent bar L4 set: the speaker recorded for public release, and the
dataset and the derived model are both stated public domain.

The first voice tried, `en_US-lessac-medium`, was rejected and its files deleted: its card
points at the Blizzard 2013 licence, which grants use "exclusively for Research Purposes
only", forbids commercial voice-synthesis products, and forbids redistribution. Personal
daily use by Steve is not research, and "delete on termination" is not a footing to build
a presence on.

## Alternatives

- **A cloned or commissioned voice** — the charter's preferred end state; needs a source
  Steve chooses and consent he documents. This ADR does not close that door; it gives Dami
  a clean voice today.
- **Cloud TTS** — refused by D-012: speech is egress.
- **Coqui/XTTS** — heavier, GPU-resident, and its model licence is non-commercial.

## Evidence

- `curl -X POST 127.0.0.1:8091/speak -d '{"text":"…"}'` → `200`, 149,036 bytes, 16-bit mono
  22.05 kHz WAV, 0.19 s for one sentence on CPU; VRAM unaffected (D6).
- `PiperSpeechClientTests`, `SpeechEndpointsTests`, `SayCommandsTests` pass.

## Consequences

- The voice is American, female, single-speaker, medium quality. Steve can reverse it by
  installing another voice into `/home/steve/Data/piper` and changing `Piper:Voice` —
  after reading its model card and recording it here.
- The sidecar never downloads a voice; installation is a deliberate act with a licence read.

## Reversal

Stop and disable `dami-tts`, remove the `Piper` registration from the Host, delete the
voice files. Nothing else depends on it.
