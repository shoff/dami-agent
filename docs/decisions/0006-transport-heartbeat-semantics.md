# ADR 0006 — Transport heartbeat semantics

- **Decision:** Wrap an `ITransport` with connection-scoped heartbeat control that, while its single receive enumeration is active, reserves message type `0`, sends an empty heartbeat after each configured interval, filters valid inbound heartbeats, and fails receive after a configured interval of inbound silence.
- **Date:** 2026-08-22
- **Status:** accepted
- **Supersedes:** none

## Context

Architecture §7.5.5 requires heartbeat as part of one-connection lifecycle handling.
ADR-0004 requires every frame, including heartbeat, to advance the same connection
sequence. ADR-0005 therefore moved outbound version and sequence ownership into the
transport before heartbeat was added.

The architecture does not define a control-message namespace, heartbeat payload, what
counts as liveness, or whether heartbeat policy belongs in the pipelines codec. Those
choices affect every future transport implementation and must be explicit before wire
behavior exists.

## Alternatives considered

| Option | Strengths | Weaknesses | Why not chosen |
|---|---|---|---|
| `HeartbeatTransport` decorator over `ITransport` | Reuses the same outbound sequence allocator; independently testable; leaves framing and socket classes focused | Adds one lifecycle layer | Chosen; heartbeat is connection policy, not binary framing or socket I/O |
| Build heartbeat directly into `PipeTransport` | Fewer types | Couples policy to one carrier and gives `PipeTransport` multiple reasons to change | Violates the `ITransport` reversal boundary |
| Application service sends heartbeat messages | No transport-layer timer | Leaks wire control and connection lifetime into runtime code; competes for sequence ownership | Contradicts ADR-0005 |
| Send only when otherwise idle | Avoids redundant traffic | Requires an additional outbound-activity clock and more concurrency state | Deferred until measurement shows periodic traffic is material |
| Reset timeout only on heartbeat | Simple mental model | Declares an active connection dead even while application frames arrive | Any valid inbound frame proves liveness |

## Evidence

- Architecture §7.5.5 build-order item 4 explicitly requires reconnect, heartbeat, and
  sequence-gap detection.
- ADR-0004 states that heartbeat frames participate in the one contiguous per-connection
  sequence.
- ADR-0005 gives the inner transport exclusive authority to allocate that sequence.
- The user accepted transport-owned outbound sequencing on 2026-08-22.
- The exact default timing values have no production measurement yet. Callers must
  supply both values; choosing operational defaults would pretend evidence exists.

## Consequences

- Message type `0` is protocol-reserved. Application sends using it are rejected.
- A valid heartbeat has `Guid.Empty`, `FrameFlags.None`, and an empty payload. Any frame
  with message type `0` and different control fields is a protocol error.
- Heartbeats are sent through the wrapped transport, so they consume the same outbound
  sequence as application messages.
- Every valid inbound frame resets the silence window; valid heartbeat frames are not
  exposed to application consumers.
- The send interval and inbound-silence timeout must both be positive, and the interval
  must be shorter than the timeout.
- `TimeProvider` is injected so time is deterministic in tests and ambient time remains
  banned.
- Heartbeat work starts on the first `MoveNextAsync` of the receive enumeration and is
  joined when that enumeration ends. The decorator has no constructor-started task and
  does not own the wrapped transport; the composition root remains responsible for the
  carrier's lifetime.
- This ADR does not define reconnect, replay, session resumption, or UDP keepalive.

## Reversal path

Before another process implements protocol version 1, change the reserved message type
and validation rules with this ADR. After interoperability exists, changing heartbeat
wire semantics requires a new protocol version or a compatibility path. The decorator
can be removed without changing application callers or carrier implementations because
both sides depend only on `ITransport`.
