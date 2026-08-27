#!/usr/bin/env bash
#
# Dami — build the Piper training environment (L4). Run once; train.sh uses it.
#
# A persistent venv rather than `uv tool run`, because VITS needs a Cython extension
# (`monotonic_align`) compiled against Python's headers, and the published wheel ships
# neither the built extension nor its source. The system Python has no headers and
# installing them needs root, so this uses a uv-managed CPython, which does. Nothing
# here needs sudo.
set -euo pipefail

VENV=/home/steve/Data/piper/train/venv
PYX_URL=https://raw.githubusercontent.com/OHF-Voice/piper1-gpl/main/src/piper/train/vits/monotonic_align/core.pyx

uv python install 3.12
MANAGED=$(uv python find 3.12 --managed-python)
[[ -f "$(dirname "$MANAGED")/../include/python3.12/Python.h" ]] || {
    echo "setup: the managed Python has no headers; cannot build the extension" >&2; exit 1; }

echo "== venv at $VENV"
rm -rf "$VENV"
uv venv "$VENV" --python "$MANAGED"
uv pip install --python "$VENV/bin/python" "piper-tts[train]" cython setuptools numpy

SITE=$("$VENV/bin/python" -c "import piper, os; print(os.path.dirname(piper.__file__))")
MA="$SITE/train/vits/monotonic_align/monotonic_align"
echo "== building monotonic_align in $MA"
mkdir -p "$MA"
: > "$MA/__init__.py"
curl -sSL -o "$MA/core.pyx" "$PYX_URL"
[[ -s "$MA/core.pyx" ]] || { echo "setup: could not fetch core.pyx" >&2; exit 1; }

PY_INC=$("$VENV/bin/python" -c "import sysconfig; print(sysconfig.get_paths()['include'])")
NP_INC=$("$VENV/bin/python" -c "import numpy; print(numpy.get_include())")
cd "$MA"
"$VENV/bin/cython" core.pyx
gcc -shared -fPIC -fopenmp -O2 -I"$PY_INC" -I"$NP_INC" core.c \
    -o "core.$("$VENV/bin/python" -c 'import sysconfig; print(sysconfig.get_config_var("EXT_SUFFIX").lstrip("."))')"

"$VENV/bin/python" - <<'PY'
import torch
from piper.train.vits.monotonic_align import maximum_path
maximum_path(torch.randn(2, 7, 11), torch.ones(2, 7, 11))
print("monotonic_align OK; cuda:", torch.cuda.is_available())
PY
echo "== ready: tools/tts/train.sh <voice>"
