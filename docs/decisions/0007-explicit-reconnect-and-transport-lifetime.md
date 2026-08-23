# ADR 0007 — Explicit reconnect and transport lifetime

- **Decision:** Make `ITransport` an async-disposable connection lifetime and provide an explicit connector that creates a fresh transport for each connection attempt; do not transparently replay messages after an ambiguous failure.
- **Date:** 2026-08-22
- **Status:** accepted
- **Supersedes:** ADR-0006 lifetime ownership only

## Context

Architecture §7.5.5 requires reconnect. ADR-0004 defines sequence numbers as
connection-scoped and explicitly resets tracking on a new transport. The current
`ITransport` exposes send and receive but not disposal even though every real carrier
owns sockets, pipes, timers, or queues. Returning that abstraction from a connector
would hide the only safe way to release the connection.

The charter also says reconnect must not duplicate or lose work. At the frame layer, a
connection failure during send is ambiguous: without a peer acknowledgement, the sender
cannot know whether the peer accepted the frame. Retrying automatically can duplicate;
not retrying can lose. The durable event store is idempotent on event ID, but no transport
acknowledgement or session-resume protocol exists yet.

## Alternatives considered

| Option | Strengths | Weaknesses | Why not chosen |
|---|---|---|---|
| Explicit `ITransportConnector.ConnectAsync` returning a new owned `ITransport` | Honest failure boundary; fresh sequence state; callers can dispose through the abstraction | Session layer must decide when and what to resume | Chosen; it implements connection recovery without inventing delivery guarantees |
| Transparent reconnect and resend inside `ITransport` | Simple caller API | Cannot distinguish delivered from undelivered frames; can duplicate consequential work | Rejected until acknowledgements and idempotency keys exist end to end |
| Connector returns `ITransport` without a disposal contract | Minimal interface change | Leaks socket/pipeline lifetime through concrete types | Rejected as an LSP and resource-ownership defect |
| Connector returns a separate transport/lifetime tuple | Keeps `ITransport` narrow | Two handles for one connection can be disposed inconsistently | The connection itself has one cohesive lifetime |

## Evidence

- Architecture §7.5.5 build-order item 4 requires reconnect.
- ADR-0004 makes a new transport a new sequence lifetime and does not define replay.
- The 2026-08-22 adversarial audit recorded disposal ownership as an unresolved public
  `ITransport` contract gap.
- Repository search on 2026-08-22 found no production consumer of `ITransport`, so the
  lifecycle correction is cheapest now.
- PostgreSQL execution-event append is already idempotent on event ID, but transport
  frames have no acknowledgement field or resend ledger.

## Consequences

- Every `ITransport` implementation and decorator must implement idempotent asynchronous
  disposal and release what it owns.
- A decorator owns the transport it wraps. This supersedes ADR-0006's earlier statement
  that `HeartbeatTransport` did not own its inner transport.
- Each connector call returns a distinct connection with outbound sequence starting at
  zero and inbound sequence establishing a new baseline.
- A failed connection is never silently reused. The caller disposes it and explicitly
  asks the connector for another.
- Reconnect does not replay frames, resume a session, or claim exactly-once delivery.
  Those require an acknowledgement/resume design above this connection primitive.

## Reversal path

Adding acknowledgement and session-resume contracts later can build on the connector:
connect a fresh transport, negotiate the last durable event, then replay only identified
missing work. If a different carrier replaces TCP, it implements the same connector and
transport lifetime. Removing disposal from `ITransport` would reintroduce hidden
ownership and is not a supported reversal.
