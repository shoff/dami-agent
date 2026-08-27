# Dami's voice — Piper on this host

Runtime: `server.py` (loopback `:8091`, unit `../systemd/dami-tts.service`), voice files in
`/home/steve/Data/piper/`, current voice `en_US-ljspeech-medium` (public domain, ADR-0022).

## Training your own voice (`train.sh`)

1. **Record.** 20–60 minutes of clean speech, one sentence per clip, quiet room, same
   mic and distance throughout. Any format ffmpeg reads. Read varied sentences — a
   chapter of a public-domain book works; questions and short replies matter for an
   assistant. More clips beats longer clips.
   **Long recordings are fine** — drop `<something>.mp3` and `<something>.txt` (the exact
   words, any length) straight into `/home/steve/Data/piper/train/` and run
   `uv run --with soundfile --with numpy --with soxr tools/tts/prep.py <voice>`. It cuts
   clips at the sentence boundaries the local whisper sidecar hears, gives each one the
   words from your transcript (aligned, because ASR text is close but not exact), and
   writes the layout below. No ffmpeg needed; nothing leaves the host.

2. **Or lay it out yourself** under `/home/steve/Data/piper/train/<name>/`:
   ```
   metadata.csv        clip001|Good morning. What is on the calendar today?
   wav/clip001.wav     …
   ```
   The text must be exactly what was said, with punctuation.
3. **Train:** `tools/tts/train.sh <name> [epochs]` — fine-tunes from the LJ Speech
   medium checkpoint in `train/base/` (846 MB, public domain, downloaded 2026-08-26 from
   `rhasspy/piper-checkpoints`) on the RTX 4080. Expect an hour or two for 1,500 epochs on
   a small set; the script exports `<name>.onnx` + `.onnx.json` and prints the two
   drop-in lines that switch `dami-tts` and `dami-host` to it.
4. **Record consent** in `docs/decisions/0022-voice-piper-ljspeech.md`: whose voice, and
   that they agreed to it being Dami's. For your own voice that is one sentence.

Nothing in this pipeline sends audio anywhere; the recordings and the model stay on this
disk. `train/` is not the voice directory — only the exported `.onnx` beside the others is
served.
