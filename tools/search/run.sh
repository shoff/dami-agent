#!/usr/bin/env bash
# Dami Core - the private SearXNG on loopback (ADR-0033). Pinned by digest; JSON enabled
# for the runtime; no limiter because the only client is this host. Settings live in
# /home/steve/Data/dami-searxng/settings.yml (source of truth: tools/search/settings.yml).
#
#   tools/search/run.sh          (re)create and start the container
set -euo pipefail
IMAGE="searxng/searxng@sha256:55e1fa15a63ff04e79e213e6aa2837549877b0c6d60757cdb633ae9111cb5fea"   # latest on 2026-09-05
DATA=/home/steve/Data/dami-searxng
mkdir -p "$DATA"
[ -f "$DATA/settings.yml" ] || cp "$(dirname "${BASH_SOURCE[0]}")/settings.yml" "$DATA/settings.yml"
docker rm -f dami-searxng >/dev/null 2>&1 || true
# --dns: the container otherwise inherits only the first, slow resolver and every engine
# call starts with a three-second timeout.
docker run -d --name dami-searxng --restart unless-stopped \
  --dns 1.1.1.1 --dns 192.168.4.23 \
  -p 127.0.0.1:8888:8080 -v "$DATA:/etc/searxng" \
  -e SEARXNG_BASE_URL=http://127.0.0.1:8888/ "$IMAGE" >/dev/null
sleep 5
curl -sf -m 20 'http://127.0.0.1:8888/search?q=dami&format=json' >/dev/null && echo "dami-searxng: up on 127.0.0.1:8888"
