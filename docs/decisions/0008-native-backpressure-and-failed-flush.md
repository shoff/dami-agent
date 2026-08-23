# ADR 0008 — Native backpressure and failed-flush handling

- **Decision:** Propagate backpressure through pending `SendAsync` calls using bounded channels, `PipeWriter.FlushAsync`, and TCP flow control; after any frame flush fails, reject further sends on that connection and require explicit reconnect.
- **Date:** 2026-08-23
- **Status:** accepted
- **Supersedes:** none

## Context

Architecture §7.5.5 step 5 requires backpressure and flow control. Loopback already uses
a bounded channel. `PipeTransport` writes exactly one frame under a send gate and awaits
`PipeWriter.FlushAsync`; TCP then supplies byte-window flow control below the pipelines
adapter. Receive is pull-based: while the application is paused at an async-enumerable
yield, no additional socket read is requested.

A failed flush is different from a send canceled before it starts. `FrameCodec.Write`
has already advanced the `PipeWriter`; pipelines provide no rollback. If the transport
keeps sending without advancing its sequence, a later successful flush can emit both
the staged frame and a new frame with the same sequence. Advancing the sequence instead
would still pretend the failed send was accepted. The connection is ambiguous and must
not be reused.

## Alternatives considered

| Option | Strengths | Weaknesses | Why not chosen |
|---|---|---|---|
| Await native bounded channel/pipe/TCP flow control and poison on failed flush | No double buffering; caller directly observes pressure; prevents duplicate sequence after ambiguous flush | Callers must await sends and reconnect after failure | Chosen; matches the transports already in use and ADR-0007's explicit recovery boundary |
| Add a second bounded outbound message queue | Central queue depth | Duplicates pipeline buffering; hides delivery failure behind enqueue success; requires another worker lifetime | Adds state without stronger delivery semantics |
| Continue after failed flush without advancing sequence | Appears resilient | Buffered failed bytes can later produce duplicate sequence values | Protocol corruption |
| Advance sequence after failed flush | Avoids duplicate sequence | Creates a gap if the frame never left and falsely reports acceptance state | Still ambiguous without acknowledgements |
| Add application credit/window frames now | Explicit peer flow control | Requires acknowledgements, control-frame negotiation, and fairness policy | Defer until multiplexing or UDP proves TCP flow control insufficient |

## Evidence

- `LoopbackTransport` uses `Channel.CreateBounded` with `FullMode.Wait`.
- `PipeTransport.SendAsync` awaits `FlushAsync` while holding the one-writer gate.
- Existing deterministic tests prove a send remains pending at a pipeline pause threshold
  and disposal unblocks it.
- Code inspection on 2026-08-23 found that failed flush left sequence unchanged while
  allowing later sends, despite bytes already being staged in the writer.
- No measurement shows a need for a second application-level queue or custom TCP credit
  protocol.

## Consequences

- `SendAsync` completion is the producer's flow-control signal. Callers must await it;
  fire-and-forget fan-out defeats boundedness outside the transport's control.
- Cancellation while waiting to enter the send gate does not poison the connection
  because no bytes have been written.
- Cancellation, completion, or exception after a frame is staged poisons outbound send.
  Later sends fail locally; the caller disposes and reconnects through ADR-0007.
- Inbound and outbound flow remain independent; a poisoned outbound half does not hide
  frames already received, but the connection should normally be replaced.
- There is no per-stream priority or fairness yet. Version 1 is one ordered frame stream.

## Reversal path

If measurements show TCP/pipeline flow control is insufficient, add negotiated credit
frames under a new protocol version and keep `ITransport` unchanged. A queued transport
decorator can also be introduced later, but its send completion must mean peer-level
acceptance or explicitly expose enqueue-only semantics.
