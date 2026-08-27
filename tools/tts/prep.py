#!/usr/bin/env python3
"""Dami — turn long recordings into a Piper training set (L4).

    uv run --with soundfile --with numpy --with soxr [--with demucs] \\
        tools/tts/prep.py <voice> [names...] [--clean] [--cuda]

--clean isolates the vocal stem with demucs first; these recordings carry a music bed at
about -30 dB and VITS learns whatever is under the speech. --cuda puts that on the GPU,
default CPU so it can run while a training job holds the card.

Reads /home/steve/Data/piper/train/<name>.mp3 (or .wav) and <name>.txt, and writes
/home/steve/Data/piper/train/<voice>/{wav/*.wav, metadata.csv} — the layout train.sh
expects: 22,050 Hz mono clips of a few seconds each, with the exact words said.

Clip boundaries come from the local faster-whisper sidecar (127.0.0.1:8090), which hears
where the sentences end. The words come from YOUR transcript, aligned to what whisper
heard: an ASR transcript is close but not exact, and a TTS model learns the difference
between text and audio, so the ground truth has to be the ground truth. Audio never
leaves the host.
"""
import difflib
import json
import re
import sys
import urllib.request
import uuid
from pathlib import Path

import numpy as np
import soundfile as sf
import soxr

ROOT = Path("/home/steve/Data/piper/train")
SEPARATED = ROOT / "separated"
STT = "http://127.0.0.1:8090/v1/audio/transcriptions"
MODEL = "Systran/faster-whisper-small.en"
RATE = 22050
MIN_SECONDS, MAX_SECONDS = 1.2, 14.0
MIN_WORDS = 3  # "Oh," is a fragment, not a training example
PAD = 0.15  # a breath either side, so words are not clipped


def normalize(word):
    return re.sub(r"[^a-z0-9']", "", word.lower())


def isolate(path, device):
    """The vocal stem, via demucs.

    Narration recorded over a music bed teaches the model the music too: VITS reproduces
    whatever sits under the speech, which is why a voice trained on these recordings
    sounds muddy however long it trains.
    """
    out = SEPARATED / "htdemucs" / path.stem / "vocals.wav"
    if out.exists():
        return out

    import shlex

    import demucs.separate
    demucs.separate.main(shlex.split(
        f"-n htdemucs --two-stems=vocals -d {device} "
        f"-o {shlex.quote(str(SEPARATED))} {shlex.quote(str(path))}"))
    if not out.exists():
        raise FileNotFoundError(f"demucs wrote no vocal stem for {path}")
    return out


def transcribe(path):
    """Segments with timings, from the local sidecar."""
    boundary = uuid.uuid4().hex
    parts = []
    for name, value in (("model", MODEL), ("response_format", "verbose_json")):
        parts.append(f"--{boundary}\r\nContent-Disposition: form-data; name=\"{name}\"\r\n\r\n{value}\r\n".encode())
    parts.append(
        f"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{path.name}\"\r\n"
        f"Content-Type: application/octet-stream\r\n\r\n".encode())
    parts.append(path.read_bytes())
    parts.append(f"\r\n--{boundary}--\r\n".encode())
    request = urllib.request.Request(
        STT, data=b"".join(parts),
        headers={"Content-Type": f"multipart/form-data; boundary={boundary}"})
    with urllib.request.urlopen(request, timeout=1800) as response:
        return json.load(response)["segments"]


def align(segments, transcript):
    """Map each whisper segment onto a span of the real transcript's words."""
    said = transcript.split()
    said_keys = [normalize(word) for word in said]
    heard, owner = [], []
    for index, segment in enumerate(segments):
        for word in segment["text"].split():
            key = normalize(word)
            if key:
                heard.append(key)
                owner.append(index)

    mapping = {}
    matcher = difflib.SequenceMatcher(a=heard, b=[k for k in said_keys if k], autojunk=False)
    kept = [position for position, key in enumerate(said_keys) if key]
    for heard_at, said_at, size in matcher.get_matching_blocks():
        for step in range(size):
            mapping.setdefault(owner[heard_at + step], []).append(kept[said_at + step])

    spans, used = {}, 0
    for index in range(len(segments)):
        positions = mapping.get(index)
        if not positions:
            continue
        start, end = max(used, min(positions)), max(positions) + 1
        if end <= start:
            continue
        spans[index] = " ".join(said[start:end])
        used = end

    return spans


def prepare(voice, names, clean=False, device="cpu"):
    out = ROOT / voice
    (out / "wav").mkdir(parents=True, exist_ok=True)
    rows, seconds, dropped = [], 0.0, 0
    for name in names:
        source = next((ROOT / f"{name}{ext}" for ext in (".mp3", ".wav", ".m4a", ".flac")
                       if (ROOT / f"{name}{ext}").exists()), None)
        text_path = ROOT / f"{name}.txt"
        if source is None or not text_path.exists():
            print(f"  {name}: no audio or no transcript; skipped", file=sys.stderr)
            continue

        if clean:
            print(f"  {name}: isolating the vocal stem on {device}")
            source = isolate(source, device)

        audio, rate = sf.read(source, dtype="float32", always_2d=True)
        mono = audio.mean(axis=1)
        if rate != RATE:
            mono = soxr.resample(mono, rate, RATE)
        peak = float(np.max(np.abs(mono))) or 1.0
        mono = mono * (0.95 / peak)

        segments = transcribe(source)
        spans = align(segments, text_path.read_text(encoding="utf-8"))
        written = 0
        for index, segment in enumerate(segments):
            said = spans.get(index)
            length = segment["end"] - segment["start"]
            if not said or len(said.split()) < MIN_WORDS or not (MIN_SECONDS <= length <= MAX_SECONDS):
                dropped += 1
                continue
            start = max(0, int((segment["start"] - PAD) * RATE))
            end = min(len(mono), int((segment["end"] + PAD) * RATE))
            clip = f"{name}_{index:04d}"
            sf.write(out / "wav" / f"{clip}.wav", mono[start:end], RATE, subtype="PCM_16")
            rows.append(f"{clip}|{said}")
            written += 1
            seconds += length
        print(f"  {name}: {written} clip(s) from {len(segments)} segment(s)")

    (out / "metadata.csv").write_text("\n".join(rows) + "\n", encoding="utf-8")
    print(f"== {len(rows)} clips, {seconds / 60:.1f} minutes -> {out}")
    print(f"   train: tools/tts/train.sh {voice}")
    return 0 if rows else 1


if __name__ == "__main__":
    if len(sys.argv) < 2 or sys.argv[1].startswith("--"):
        print(__doc__, file=sys.stderr)
        raise SystemExit(2)
    arguments = [a for a in sys.argv[1:] if not a.startswith("--")]
    flags = {a for a in sys.argv[1:] if a.startswith("--")}
    voice_name = arguments[0]
    sources = arguments[1:] or sorted(
        {p.stem for p in ROOT.glob("*.txt")} & {p.stem for ext in ("mp3", "wav", "m4a", "flac")
                                                for p in ROOT.glob(f"*.{ext}")})
    raise SystemExit(prepare(
        voice_name, sources, clean="--clean" in flags,
        device="cuda" if "--cuda" in flags else "cpu"))
