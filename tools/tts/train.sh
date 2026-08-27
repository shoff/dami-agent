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
BASE="$ROOT/train/base/lj-med_1000.ckpt"
WORK="$DATA/work"
VENV=/home/steve/Data/piper/train/venv

[[ -f "$DATA/metadata.csv" ]] || { echo "train: $DATA/metadata.csv is missing" >&2; exit 1; }
[[ -d "$DATA/wav" ]] || { echo "train: $DATA/wav/ is missing" >&2; exit 1; }
[[ -f "$BASE" ]] || { echo "train: base checkpoint $BASE is missing (see README)" >&2; exit 1; }
[[ -x "$VENV/bin/python" ]] || { echo "train: run tools/tts/setup-training.sh first" >&2; exit 1; }
clips=$(wc -l < "$DATA/metadata.csv")
echo "== $clips clip(s) in $DATA/wav (already 22050 Hz mono; prep.py wrote them)"
mkdir -p "$WORK/cache" "$WORK/lightning"

# Warmstart, not resume: copy every matching-shape parameter out of the LJ Speech
# checkpoint and start training on this voice at epoch 0. Resuming would drag along a
# 2022 Lightning trainer config the current CLI does not accept.
echo "== training $VOICE for $EPOCHS epochs, warmstarted from LJ Speech (GPU)"
cd "$WORK"
"$VENV/bin/python" -m piper.train fit \
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
    --model.warmstart_ckpt "$BASE"

ckpt=$(ls -t "$WORK"/lightning/lightning_logs/*/checkpoints/*.ckpt 2>/dev/null | head -1)
[[ -n "$ckpt" ]] || { echo "train: no checkpoint was written" >&2; exit 1; }
echo "== exporting $ckpt"
"$VENV/bin/python" -m piper.train.export_onnx \
    --checkpoint "$ckpt" --output-file "$ROOT/$VOICE.onnx"
cp "$WORK/$VOICE.onnx.json" "$ROOT/$VOICE.onnx.json"

echo "== installed $ROOT/$VOICE.onnx"
echo "listen:   echo 'Hello, this is Dami.' | uv tool run --from piper-tts piper --data-dir $ROOT -m $VOICE -f /tmp/$VOICE.wav && paplay /tmp/$VOICE.wav"
echo "switch:   sudo systemctl edit dami-tts       -> Environment=DAMI_TTS_VOICE=$VOICE"
echo "          sudo systemctl edit dami-host      -> Environment=Piper__Voice=$VOICE"
echo "          sudo systemctl restart dami-tts dami-host"
echo "record:   whose voice, and their consent, in docs/decisions/0022-voice-piper-ljspeech.md"
