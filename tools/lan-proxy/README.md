# Dami LAN proxy

A password-protected HTTPS door to the web view at `http://127.0.0.1:5810/`, for the
other workstations on this LAN. The runtime does not move: D-005 keeps `Dami.Host` on
loopback until its OIDC authentication (ADR-0020, board G5a) is cut over, and
`HostUrls.Resolve` now refuses a non-loopback bind while `Authentication:Enabled` is
false. This stack is the exposure instead — one Caddy container on the host network,
one basic-auth credential, TLS from Caddy's local CA.

```bash
tools/lan-proxy/set-password.sh                 # random password → ~/.config/dami/lan-proxy.password
tools/lan-proxy/set-password.sh --prompt        # or your own
docker compose -f tools/lan-proxy/docker-compose.yml up -d
```

Then from any machine on the LAN: `https://192.168.4.45:8443/`, user `steve`, the password
from the file. The certificate is self-signed by Caddy's internal CA: accept it once per
browser, or import `/data/caddy/pki/authorities/local/root.crt` from the
`dami-lan-proxy-data` volume.

- The credential lives only in `~/.config/dami/lan-proxy.env` (the bcrypt hash) and
  `~/.config/dami/lan-proxy.password` (0600). Nothing here is committed.
- `/lan-proxy/health` answers `ok` without a password, for the container healthcheck.
- `flush_interval -1` keeps `/turns/stream` streaming token by token through the proxy.
- Stop it: `docker compose -f tools/lan-proxy/docker-compose.yml down`. The API is
  unreachable from the LAN again the moment the container is gone.
- The alternative that needs nothing installed is an SSH tunnel from the workstation:
  `ssh -N -L 5810:127.0.0.1:5810 steve@192.168.4.45`, then `http://localhost:5810/`.

What this does not do: it is one shared password over the LAN, not identities, scopes
or revocation. That is G5a's job; when authentication is on, bind the host directly with
`Host__Urls` and retire this.
