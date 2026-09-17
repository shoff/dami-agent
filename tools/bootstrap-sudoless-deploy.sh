#!/usr/bin/env bash
#
# Dami Core — one-time root setup so that every later deploy, config change, and
# restart needs no sudo (runbook §"Deploying without sudo").
#
#   sudo bash tools/bootstrap-sudoless-deploy.sh
#
# What it does, idempotently:
#   1. Installs tools/polkit/49-dami-units.rules: steve may start/stop/restart any
#      dami-* unit without a password. Nothing else is granted.
#   2. Adds a drop-in to dami-host and dami-proactive that reads a steve-owned
#      environment file LAST, so runtime configuration — allowlist entries, provider
#      switches, API keys — is edited as steve and never needs root again:
#          /home/steve/.config/dami/host.env       (dami-host)
#          /home/steve/.config/dami/proactive.env  (dami-proactive)
#      systemd applies EnvironmentFile= after Environment=, so a line in these files
#      overrides the same key in the root-owned drop-ins. Secrets belong here (0600),
#      not in the world-readable override.conf files.
#   3. daemon-reload, so the drop-ins take effect on the next restart.
#
# /opt/dami is already owned by steve, so the binaries never needed root. The three
# things above were the whole reason deploy.sh asked for sudo.
set -euo pipefail

SERVICE_USER=steve
SERVICE_HOME=/home/$SERVICE_USER
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RULE_SRC="$REPO/tools/polkit/49-dami-units.rules"
RULE_DST=/etc/polkit-1/rules.d/49-dami-units.rules
CONFIG_DIR="$SERVICE_HOME/.config/dami"

if [[ ${EUID} -ne 0 ]]; then
    echo "bootstrap: run this once with sudo: sudo bash tools/bootstrap-sudoless-deploy.sh" >&2
    exit 2
fi
id "$SERVICE_USER" > /dev/null

echo "== 1. polkit rule: $SERVICE_USER manages dami-* units"
[[ -f "$RULE_SRC" ]] || { echo "bootstrap: missing $RULE_SRC" >&2; exit 1; }
install -d -m 0755 /etc/polkit-1/rules.d
if cmp -s "$RULE_SRC" "$RULE_DST"; then
    echo "   already installed"
else
    install -m 0644 "$RULE_SRC" "$RULE_DST"
    echo "   installed $RULE_DST"
fi

echo "== 2. user-owned runtime configuration"
install -d -m 0700 -o "$SERVICE_USER" -g "$SERVICE_USER" "$CONFIG_DIR"
for unit in host proactive; do
    env_file="$CONFIG_DIR/$unit.env"
    if [[ ! -f "$env_file" ]]; then
        cat > "$env_file" <<HEADER
# dami-$unit runtime configuration, read by systemd after the root-owned drop-ins
# and overriding them key for key. Edit as $SERVICE_USER, then: systemctl restart dami-$unit
# One KEY=value per line, no quotes needed, no 'Environment=' prefix. Secrets live
# here (this file is 0600); never in the repository or a shell history.
HEADER
        chown "$SERVICE_USER:$SERVICE_USER" "$env_file"
        chmod 0600 "$env_file"
        echo "   created $env_file"
    else
        chmod 0600 "$env_file"
        echo "   kept $env_file"
    fi

    dropin_dir="/etc/systemd/system/dami-$unit.service.d"
    dropin="$dropin_dir/zz-user-config.conf"
    install -d -m 0755 "$dropin_dir"
    wanted=$(printf '# %s\n[Service]\nEnvironmentFile=-%s\n' \
        "Steve-owned runtime configuration (tools/bootstrap-sudoless-deploy.sh). Read last; overrides the files above." \
        "$env_file")
    if [[ -f "$dropin" ]] && [[ "$(cat "$dropin")" == "$wanted" ]]; then
        echo "   drop-in present: $dropin"
    else
        printf '%s\n' "$wanted" > "$dropin"
        chmod 0644 "$dropin"
        echo "   wrote $dropin"
    fi
done

echo "== 3. daemon-reload"
systemctl daemon-reload

echo "== verify"
for unit in host proactive; do
    if systemctl cat "dami-$unit" | grep -Fq "EnvironmentFile=-$CONFIG_DIR/$unit.env"; then
        echo "   dami-$unit reads $CONFIG_DIR/$unit.env"
    else
        echo "   dami-$unit does NOT read $CONFIG_DIR/$unit.env" >&2
        exit 1
    fi
done
echo "   prove the polkit half as $SERVICE_USER, from any shell, no sudo:"
echo "     systemctl --no-ask-password restart dami-host && systemctl is-active dami-host"
echo "done. From now on tools/deploy.sh, tools/dami-up and tools/dami-down run without sudo."
