#!/usr/bin/env bash
#
# Dami Core — deploy what is staged in ~/.cache/dami-pub to /opt/dami, install the
# sidecar units, make sure the egress allowlist carries the civic feed host, and
# restart. Run as steve; it asks for sudo once.
#
#   tools/deploy.sh            publish from the tree, then deploy
#   tools/deploy.sh --no-build deploy what is already staged
#
# Runtime configuration stays in the systemd drop-ins (runbook §4); this script only
# appends an Environment= line that is missing, never rewrites the drop-in.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STAGE="$HOME/.cache/dami-pub"
CIVIC_HOST="www.lakevillemn.gov"

if [[ "${1:-}" != "--no-build" ]]; then
    echo "== publishing Release builds"
    (cd "$REPO/Dami" \
        && dotnet publish src/Dami.Host           -c Release -o "$STAGE/host"      -v quiet \
        && dotnet publish src/Dami.Host.Proactive -c Release -o "$STAGE/proactive" -v quiet \
        && dotnet publish src/Dami.Gateway.Cli    -c Release -o "$STAGE/cli"       -v quiet)
fi

for dir in host proactive cli; do
    [[ -d "$STAGE/$dir" ]] || { echo "deploy: nothing staged at $STAGE/$dir" >&2; exit 1; }
done

echo "== syncing /opt/dami (sudo)"
sudo rsync -a "$STAGE/host/"      /opt/dami/host/
sudo rsync -a "$STAGE/proactive/" /opt/dami/proactive/
sudo rsync -a "$STAGE/cli/"       /opt/dami/cli/

echo "== sidecar unit: dami-tts"
if ! cmp -s "$REPO/tools/systemd/dami-tts.service" /etc/systemd/system/dami-tts.service 2>/dev/null; then
    sudo cp "$REPO/tools/systemd/dami-tts.service" /etc/systemd/system/dami-tts.service
    sudo systemctl daemon-reload
fi
# A hand-started sidecar from a shell would hold the port; the unit owns it from now on.
pkill -f "[t]ools/tts/server.py" 2>/dev/null || true
sudo systemctl enable --now dami-tts

echo "== egress allowlist: $CIVIC_HOST"
DROPIN=/etc/systemd/system/dami-proactive.service.d/override.conf
if ! systemctl cat dami-proactive | grep -q "AllowedHosts__[0-9]*=$CIVIC_HOST"; then
    next=$(systemctl cat dami-proactive | grep -oE "Egress__AllowedHosts__[0-9]+" | grep -oE "[0-9]+$" | sort -n | tail -1)
    next=$(( ${next:-0} + 1 ))
    sudo mkdir -p "$(dirname "$DROPIN")"
    if [[ -f "$DROPIN" ]]; then
        printf 'Environment=Egress__AllowedHosts__%s=%s\n' "$next" "$CIVIC_HOST" | sudo tee -a "$DROPIN" > /dev/null
    else
        printf '[Service]\nEnvironment=Egress__AllowedHosts__%s=%s\n' "$next" "$CIVIC_HOST" | sudo tee "$DROPIN" > /dev/null
    fi
    sudo systemctl daemon-reload
    echo "   added Egress__AllowedHosts__$next=$CIVIC_HOST"
else
    echo "   already present"
fi

echo "== restarting"
sudo systemctl restart dami-host dami-proactive
sleep 3
systemctl is-active dami-host dami-proactive dami-tts | paste -sd' '
curl -s -o /dev/null -w "dami-host /health %{http_code}\n" http://127.0.0.1:5810/health
curl -s -m 5 http://127.0.0.1:8091/health && echo
echo "== deployed builds"
ls -la /opt/dami/host/Dami.Host.dll /opt/dami/proactive/Dami.Host.Proactive /opt/dami/cli/dami | awk '{print "  "$6" "$7" "$8"  "$9}'
echo "civic feeds fetch on the next proactive tick (immediately after this restart if never run today)."
