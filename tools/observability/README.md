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
