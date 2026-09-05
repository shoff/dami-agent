# Dami observability stack

This stack indexes the JSON console records from `dami-host.service` and
`dami-proactive.service`. It deliberately reads only those two systemd units and starts
at the journal tail on its first run; existing journal history is not backfilled.

Elasticsearch and Kibana bind only to `127.0.0.1`:

- Elasticsearch: `http://127.0.0.1:9200`
- Kibana: `http://127.0.0.1:5601`

The Docker network is bridge-isolated and only the two explicit ports are published to
loopback. The Filebeat container has a read-only host-root mount only because the
supported journald input needs the host `journalctl` binary; it has no host-network
namespace and is configured to send to the Elasticsearch container alias only. Its cursor
is persisted in the `dami-filebeat-data` Docker volume. Elasticsearch data persists in
`/home/steve/Data/dami-observability/elasticsearch`.

Security is disabled only inside this local, loopback-only diagnostic stack, so no
password is committed or injected into a systemd unit. Do not expose either port; moving
this beyond localhost requires an authenticated HTTPS configuration and a new recorded
security decision.

Start or update the stack:

```bash
mkdir -p /home/steve/Data/dami-observability/elasticsearch
chown 1000:1000 /home/steve/Data/dami-observability/elasticsearch
docker compose -f tools/observability/docker-compose.yml up -d
```

Confirm it is healthy and inspect the indexed runtime records:

```bash
docker compose -f tools/observability/docker-compose.yml ps
curl -s http://127.0.0.1:9200/_cat/indices/dami-runtime-*?v
curl -s http://127.0.0.1:9200/dami-runtime-*/_search?pretty
```

In Kibana, create the `dami-runtime-*` data view with `@timestamp` as its time field,
then use Discover. The decoded Dami JSON record is under `dami`; the outer journal fields
remain available for service and process diagnosis.

Stop it without deleting persisted data:

```bash
docker compose -f tools/observability/docker-compose.yml stop
```

## Retention

Two limits, both deliberately tight for now:

- **Age — 14 days**, enforced by Elasticsearch itself. Every `dami-runtime-*` data stream
  carries a data stream lifecycle of `14d`, and the cluster default
  (`data_streams.lifecycle.retention.default: 14d`, `max: 30d`) gives the same to every
  new daily stream Filebeat creates. Check with
  `curl -s 'http://127.0.0.1:9200/_data_stream/dami-runtime-*/_lifecycle?pretty'`.
- **Size — 50 GB hard cap**, enforced by `dami-es-retention` on an hourly timer: while
  the backing indices of `dami-runtime-*` total more than the cap, the oldest stream is
  deleted, oldest first, never today's. `--dry-run` says what would go;
  `DAMI_ES_CAP_GB` overrides the cap. `test-retention.sh` exercises it against a curl shim.

Install the timer (the script has no sudo of its own):

```bash
sudo cp tools/observability/dami-es-retention /usr/local/bin/
sudo cp tools/systemd/dami-es-retention.{service,timer} /etc/systemd/system/
sudo systemctl enable --now dami-es-retention.timer
```

To re-apply the age policy after a rebuild of the stack:

```bash
curl -s -X PUT http://127.0.0.1:9200/_cluster/settings -H 'Content-Type: application/json' \
  -d '{"persistent":{"data_streams.lifecycle.retention.default":"14d","data_streams.lifecycle.retention.max":"30d"}}'
curl -s -X PUT 'http://127.0.0.1:9200/_data_stream/dami-runtime-*/_lifecycle' -H 'Content-Type: application/json' \
  -d '{"data_retention":"14d"}'
```

