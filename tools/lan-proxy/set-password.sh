#!/usr/bin/env bash
# Writes ~/.config/dami/lan-proxy.env (0600) with the proxy's one credential.
#
#   tools/lan-proxy/set-password.sh              generate a random password, print where it is
#   tools/lan-proxy/set-password.sh --prompt     type your own (not echoed)
#
# The plaintext lands only in ~/.config/dami/lan-proxy.password (0600) so it can be read
# back once; the env file holds the bcrypt hash. Neither is ever printed here.
set -euo pipefail

CONFIG_DIR="${HOME}/.config/dami"
ENV_FILE="${CONFIG_DIR}/lan-proxy.env"
PASSWORD_FILE="${CONFIG_DIR}/lan-proxy.password"
USER_NAME="${LAN_PROXY_USER:-steve}"
ADDRESS="${LAN_PROXY_ADDRESS:-$(hostname -I | awk '{print $1}')}"
IMAGE="caddy:2.11.4"

mkdir -p "${CONFIG_DIR}"
chmod 700 "${CONFIG_DIR}"

if [[ "${1:-}" == "--prompt" ]]; then
    read -r -s -p "Password for ${USER_NAME}: " password; echo
    read -r -s -p "Again: " again; echo
    [[ "${password}" == "${again}" ]] || { echo "they differ; nothing written" >&2; exit 1; }
else
    password="$(head -c 24 /dev/urandom | base64 | tr -d '/+=' | head -c 20)"
fi
[[ -n "${password}" ]] || { echo "empty password; nothing written" >&2; exit 1; }

hash="$(printf '%s\n' "${password}" | docker run --rm -i "${IMAGE}" caddy hash-password)"

umask 077
printf '%s\n' "${password}" > "${PASSWORD_FILE}"
{
    echo "# dami-lan-proxy credential (tools/lan-proxy/set-password.sh). Never commit."
    echo "LAN_PROXY_USER=${USER_NAME}"
    # Compose interpolates env_file values, so every $ in the bcrypt hash is written as $$.
    echo "LAN_PROXY_HASH=${hash//\$/\$\$}"
    echo "LAN_PROXY_ADDRESS=${ADDRESS}"
} > "${ENV_FILE}"
chmod 600 "${PASSWORD_FILE}" "${ENV_FILE}"

echo "user: ${USER_NAME}; address: https://${ADDRESS}:8443/"
echo "password: in ${PASSWORD_FILE} (0600); hash in ${ENV_FILE}"
echo "apply: docker compose -f tools/lan-proxy/docker-compose.yml up -d --force-recreate"
