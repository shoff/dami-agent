#!/usr/bin/env bash
#
# Dami Core — deploy what is staged in ~/.cache/dami-pub to /opt/dami, install the
# sidecar units, make sure the egress allowlist carries the civic feed host, and
# restart. Run as steve; it asks for sudo once.
#
#   tools/deploy.sh             gate, publish from the tree, then deploy
#   tools/deploy.sh --no-build  deploy what is already staged (still gates and verifies)
#   tools/deploy.sh --no-gate   skip build+test; says so loudly, for a deliberate hotfix
#
# Runtime configuration stays in the systemd drop-ins (runbook §4); this script only
# appends an Environment= line that is missing, never rewrites the drop-in.
set -euo pipefail

# Run as steve, never under sudo. This script asks for sudo itself, which makes
# `sudo tools/deploy.sh` the natural thing to type - and then the gate runs as root, HOME
# is /root, and every test that touches the database fails looking for /root/.pgpass. The
# failures read as six unrelated defects (a 500 from /speak, a missing JSON property in a
# frontier turn) and are one wrong user.
if [[ ${EUID} -eq 0 ]]; then
    echo "deploy: run this as steve, not with sudo." >&2
    echo "        It asks for sudo where it needs it. As root the test gate cannot read" >&2
    echo "        ~/.pgpass and the database-backed tests fail for no real reason." >&2
    exit 2
fi

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STAGE="$HOME/.cache/dami-pub"
CIVIC_HOST="www.lakevillemn.gov"
PGURL="host=127.0.0.1 port=5432 dbname=dami-data user=dami_app passfile=$HOME/.pgpass"

BUILD=1
GATE=1
for arg in "$@"; do
    case "$arg" in
        --no-build) BUILD=0 ;;
        --no-gate)  GATE=0 ;;
        *) echo "deploy: unknown option $arg" >&2; exit 2 ;;
    esac
done

# There is no CI. This is the gate a pipeline would otherwise be, and it belongs here
# rather than in a habit: deploying an untested build is how /opt and the tree diverge.
# TreatWarningsAsErrors is on, so a warning already fails the build.
if (( GATE )); then
    echo "== gate: build and test"
    (cd "$REPO/Dami" && dotnet build Dami.sln -v quiet) \
        || { echo "deploy: BUILD FAILED - nothing deployed" >&2; exit 1; }
    (cd "$REPO/Dami" && dotnet test Dami.sln -v quiet --nologo) \
        || { echo "deploy: TESTS FAILED - nothing deployed" >&2; exit 1; }
    echo "   build clean, tests green"
else
    echo "== gate SKIPPED (--no-gate) - deploying an unverified build"
fi

# Schema first: a binary that expects a migration the database has not got fails at
# runtime. 034 had to be applied by hand because tools/ddl/apply.sh connects as the role
# 'steve', which does not exist here - its ledger lookup fails, the error is swallowed,
# and it reports every migration as pending. Read the ledger the way that works, and
# refuse to deploy ahead of the schema.
echo "== schema"
applied="$(psql "$PGURL" -tAc 'select filename from dami.schema_migrations' 2>/dev/null || true)"
if [[ -z "$applied" ]]; then
    echo "   WARNING: could not read dami.schema_migrations; check skipped" >&2
else
    pending=()
    for file in "$REPO"/tools/ddl/[0-9]*.sql; do
        name="$(basename "$file")"
        grep -qxF "$name" <<< "$applied" || pending+=("$name")
    done
    if (( ${#pending[@]} )); then
        echo "deploy: ${#pending[@]} migration(s) not applied to dami-data:" >&2
        printf '   %s\n' "${pending[@]}" >&2
        echo "   apply each as the schema owner, then re-run this script:" >&2
        echo "     psql \"host=127.0.0.1 port=5432 dbname=dami-data user=dami_ddl passfile=\$HOME/.pgpass\" -f tools/ddl/<file>" >&2
        exit 1
    fi
    echo "   up to date ($(grep -c . <<< "$applied") applied)"
fi

if (( BUILD )); then
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

# Prove the deploy landed. A stale /opt after an apparently clean run is the failure that
# costs an afternoon: everything reads as deployed and the new endpoint answers 404.
staged=$(stat -c %Y "$STAGE/host/Dami.Host.dll")
live=$(stat -c %Y /opt/dami/host/Dami.Host.dll)
if (( live < staged )); then
    echo "deploy: /opt/dami/host is OLDER than what was staged - the sync did not land" >&2
    exit 1
fi
echo "   /opt matches the staged build"
echo "civic feeds fetch on the next proactive tick (immediately after this restart if never run today)."
