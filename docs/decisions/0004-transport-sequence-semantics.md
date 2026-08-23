# ADR 0004 — Transport sequence semantics

- **Decision:** Treat every frame sequence as one connection-scoped contiguous `uint32`; accept any first value, require each later value to equal the previous value plus one with wraparound, and reset tracking on a new connection.
- **Date:** 2026-08-22
- **Status:** accepted
- **Supersedes:** none

## Context

Architecture §7.5.5 requires sequence-gap detection but does not define whether sequence
numbers belong to a TCP connection, a correlation, or a message stream. It also leaves
duplicates, backward movement, wraparound, and reconnect behavior unspecified. Those
choices must be fixed before heartbeat and reconnect work because they affect what each
peer treats as a protocol violation.

TCP already provides ordered, reliable bytes. A sequence mismatch therefore indicates
a producer defect, an incorrect reconnect boundary, or a framing implementation error;
it is not ordinary packet loss to be repaired inside the frame reader.

## Alternatives considered

| Option | Strengths | Weaknesses | Why not chosen |
|---|---|---|---|
| One contiguous sequence per connection | Constant state; detects every missing, duplicate, and reordered frame; independent of payload serialization | Does not resume transparently across connections | Chosen; reconnect starts a new transport lifetime and no resume protocol exists |
| One sequence per correlation ID | Independent correlated streams | Unbounded tracker state; misses loss between correlations; requires an eviction/completion policy | Correlation identifies logical work, not the reliability domain |
| Require only monotonic increase | Tolerates intentional skips | Cannot distinguish a gap from valid behavior | Gap detection would become observational rather than enforcing |
| Preserve sequence across reconnect | Can detect loss across connection replacement | Requires a negotiated resume point and replay buffer | No replay/resume protocol exists; adding implicit state would be dishonest |

## Evidence

- Architecture §7.5.3 places one `uint32` sequence in every frame, outside payload
  serialization and beside connection-level framing fields.
- Architecture §7.5.5 orders sequence-gap detection with reconnect and heartbeat but
  specifies no cross-connection replay protocol.
- TCP guarantees in-order byte delivery for one connection, so a decoded frame gap is
  evidence of endpoint behavior rather than network reordering.
- The user accepted this recommendation on 2026-08-22.

The choice is a protocol judgment, not a benchmark result. Tests provide behavioral
evidence that the implementation follows it; they do not prove it is the only viable
protocol.

## Consequences

- The first decoded frame establishes the baseline and may use any sequence value.
- Every subsequent frame, including future heartbeat frames, advances the same counter.
- `uint.MaxValue` followed by `0` is valid wraparound.
- A duplicate, backward value, or forward gap is a protocol error and ends that receive
  enumeration. Continuing would leave the consumer unable to know what it missed.
- Constructing a new `PipeTransport` resets tracking. Reconnect therefore starts a new
  sequence lifetime until an explicit replay/resume protocol is designed.
- Tracking requires constant memory and does not retain correlation identifiers.

## Reversal path

Before another implementation ships, change the tracker and this ADR together. After
interoperability exists, changing sequence scope or reconnect continuity requires a new
protocol version and a compatibility path; silently changing version 1 semantics is not
acceptable.
