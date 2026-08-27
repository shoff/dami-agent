#!/usr/bin/env bash
#
# Dami — train (fine-tune) a Piper voice on this host's GPU, then install it (L4).
#
#   tools/tts/train.sh <voice-name> [max-epochs]
#
# Expects recordings under /home/steve/Data/piper/train/<voice-name>/:
#   metadata.csv     one line per clip:  <clip-id>|<exactly what is said>
#   wav/<clip-id>.wav  (any rate/channels; ffmpeg normalises to 22050 Hz mono 16-bit)
#
# Fine-tunes from the public-domain LJ Speech medium checkpoint in train/base (same
# architecture as the voice Dami speaks with now), exports ONNX, installs the voice
# next to the others, and prints the two lines that switch Dami to it. Nothing is
# uploaded anywhere; the recordings never leave this disk.
set -euo pipefail

VOICE="${1:?voice name, e.g. steve}"
EPOCHS="${2:-1500}"
ROOT=/home/steve/Data/piper
DATA="$ROOT/train/$VOICE"
BASE="$ROOT/train/base/lj-med_1000.sanitized.ckpt"
RAW="$ROOT/train/base/lj-med_1000.ckpt"
WORK="$DATA/work"

[[ -f "$DATA/metadata.csv" ]] || { echo "train: $DATA/metadata.csv is missing" >&2; exit 1; }
[[ -d "$DATA/wav" ]] || { echo "train: $DATA/wav/ is missing" >&2; exit 1; }
# torch 2.6 loads checkpoints with weights_only=True, which refuses the pathlib paths
# pickled into the published checkpoint. Rewrite those as strings, once, so the load is
# a plain tensor load rather than arbitrary unpickling.
if [[ ! -f "$BASE" ]]; then
    [[ -f "$RAW" ]] || { echo "train: base checkpoint $RAW is missing (see README)" >&2; exit 1; }
    echo "== sanitising the base checkpoint for torch 2.6"
    uv run --with torch python - "$RAW" "$BASE" <<'PY'
import pathlib, sys, torch
def clean(o):
    if isinstance(o, pathlib.PurePath): return str(o)
    if isinstance(o, dict): return {k: clean(v) for k, v in o.items()}
    if isinstance(o, (list, tuple)): return type(o)(clean(v) for v in o)
    return o
torch.save(clean(torch.load(sys.argv[1], map_location="cpu", weights_only=False)), sys.argv[2])
PY
fi
clips=$(wc -l < "$DATA/metadata.csv")
echo "== $clips clip(s) in $DATA/wav (already 22050 Hz mono; prep.py wrote them)"
mkdir -p "$WORK/cache" "$WORK/lightning"

echo "== training $VOICE for $EPOCHS epochs from the LJ Speech checkpoint (GPU)"
cd "$WORK"
uv tool run --from "piper-tts[train]" python -m piper.train fit \
    --data.voice_name "$VOICE" \
    --data.csv_path "$DATA/metadata.csv" \
    --data.audio_dir "$DATA/wav" \
    --data.cache_dir "$WORK/cache" \
    --data.config_path "$WORK/$VOICE.onnx.json" \
    --data.espeak_voice en-us \
    --data.batch_size 16 \
    --model.sample_rate 22050 \
    --trainer.accelerator gpu --trainer.devices 1 --trainer.precision 32 \
    --trainer.max_epochs "$EPOCHS" \
    --trainer.default_root_dir "$WORK/lightning" \
    --ckpt_path "$BASE"

ckpt=$(ls -t "$WORK"/lightning/lightning_logs/*/checkpoints/*.ckpt 2>/dev/null | head -1)
[[ -n "$ckpt" ]] || { echo "train: no checkpoint was written" >&2; exit 1; }
echo "== exporting $ckpt"
uv tool run --from "piper-tts[train]" python -m piper.train.export_onnx \
    --checkpoint "$ckpt" --output-file "$ROOT/$VOICE.onnx"
cp "$WORK/$VOICE.onnx.json" "$ROOT/$VOICE.onnx.json"

echo "== installed $ROOT/$VOICE.onnx"
echo "listen:   echo 'Hello, this is Dami.' | uv tool run --from piper-tts piper --data-dir $ROOT -m $VOICE -f /tmp/$VOICE.wav && paplay /tmp/$VOICE.wav"
echo "switch:   sudo systemctl edit dami-tts       -> Environment=DAMI_TTS_VOICE=$VOICE"
echo "          sudo systemctl edit dami-host      -> Environment=Piper__Voice=$VOICE"
echo "          sudo systemctl restart dami-tts dami-host"
echo "record:   whose voice, and their consent, in docs/decisions/0022-voice-piper-ljspeech.md"
