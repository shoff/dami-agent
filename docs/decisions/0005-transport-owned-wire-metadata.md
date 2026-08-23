# ADR 0005 — Transport-owned wire metadata

- **Decision:** `ITransport.SendAsync` accepts an application-owned `TransportMessage`; each transport assigns protocol version and connection-scoped sequence when it creates the wire `TransportFrame`.
- **Date:** 2026-08-22
- **Status:** accepted
- **Supersedes:** none

## Context

ADR-0004 makes sequence numbers contiguous across every frame on one connection. The
next architecture §7.5.5 behavior is heartbeat. A heartbeat is connection control traffic
and must participate in that same sequence, but the current `ITransport.SendAsync`
accepts a caller-created `TransportFrame` containing both protocol version and sequence.
That gives every application producer authority over shared connection state and leaves
no honest owner for heartbeat sequence allocation.

The choice must be made while the transport has no application callers. Delaying it
would spread wire-protocol responsibilities into the runtime, gateways, and proactive
services.

## Alternatives considered

| Option | Strengths | Weaknesses | Why not chosen |
|---|---|---|---|
| Transport owns version and sequence | One allocator per connection; heartbeat and application traffic cannot collide; callers cannot emit unsupported versions | Send and receive use different contract types | Chosen; the asymmetry reflects real ownership rather than hiding it |
| Caller owns the full frame | Existing signature remains | Every producer coordinates shared mutable protocol state; heartbeat needs the same allocator; unsupported versions can be emitted | Leaks transport internals and violates SRP |
| Keep `TransportFrame` but silently overwrite fields | Source-compatible | The API accepts values it deliberately ignores | A misleading contract is worse than a breaking change now |
| Inject a shared sequence allocator into every producer | Explicit coordination | Couples application services to connection lifetime and reconnect behavior | Moves a transport concern outward |

## Evidence

- ADR-0004 defines one contiguous sequence across every frame on a connection.
- Architecture §7.5.5 places heartbeat and sequence-gap detection in the same connection
  lifecycle step.
- `ITransport` has no application consumers yet; repository search on 2026-08-22 found
  only the two transport implementations and their tests.
- The user explicitly accepted transport-owned outbound sequencing on 2026-08-22.

## Consequences

- `TransportMessage` contains message type, correlation ID, flags, and payload only.
- `SendAsync` snapshots payload bytes before successful completion, as before.
- A new transport lifetime starts outbound sequence `0`; successful sends advance it by
  one with `uint32` wraparound.
- Send serialization order is wire sequence order. Loopback must serialize overlapping
  sends as well as `PipeTransport` so it remains a substitutable reference implementation.
- `ReceiveAsync` continues to yield `TransportFrame`, preserving observed protocol
  version and sequence for diagnostics and gap enforcement.
- Heartbeat can use the transport's allocator without exposing connection state to
  application code.

## Reversal path

Before runtime consumers exist, change both contracts and their tests. After consumers
ship, reversing ownership is a public API break and likely a protocol-version change.
Do not restore caller-owned metadata through an overload that recreates two authorities.
