#!/usr/bin/env bash
#
# Dami — listen to a training checkpoint without installing it (L4).
#
#   tools/tts/sample.sh <voice> [checkpoint.ckpt] ["something to say"]
#
# Exports the checkpoint to a scratch ONNX, speaks one line, plays it. Nothing under
# /home/steve/Data/piper is touched, so the voice Dami currently uses is unaffected.
# With no checkpoint it uses the latest. val_mel is not the signal for "done" — VITS
# saturates it early while the adversarial losses keep removing audible artifacts — so
# the way to choose a final checkpoint is to listen to a few and pick.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VOICE="${1:?voice name}"
ROOT=/home/steve/Data/piper
VENV="$ROOT/train/venv"
WORK="$ROOT/train/$VOICE/work"
CKPT="${2:-$(ls -t "$WORK"/lightning/lightning_logs/*/checkpoints/*.ckpt 2>/dev/null | head -1)}"
LINE="${3:-Good evening Steve. The network is fine, and there are three meetings this week.}"

[[ -n "$CKPT" && -f "$CKPT" ]] || { echo "sample: no checkpoint found for $VOICE" >&2; exit 1; }
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT

echo "== $(basename "$CKPT")"
"$VENV/bin/python" "$HERE/export_entry.py" --checkpoint "$CKPT" \
    --output-file "$scratch/$VOICE.onnx" 2>&1 | grep -iE "^INFO:__main__" || true
cp "$WORK/$VOICE.onnx.json" "$scratch/$VOICE.onnx.json"

out="$ROOT/sample-$VOICE-$(basename "$CKPT" .ckpt).wav"
echo "$LINE" | uv tool run --from piper-tts piper --data-dir "$scratch" -m "$VOICE" -f "$out" 2>/dev/null
echo "== $out"
command -v paplay >/dev/null && paplay "$out" || echo "   (play it with: paplay $out)"
