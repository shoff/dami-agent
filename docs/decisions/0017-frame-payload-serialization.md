# ADR 0017 — Frame payload serialization: still deferred, now with a number

- **Decision (proposed):** Keep deferring the in-frame serialization format, and stop calling it an open question. JSON over HTTP/SSE carries every payload this system currently has, by a margin measured below. Revisit **only** when a consumer appears that exceeds ~1,000 events/second or carries audio frames — that is, when voice (E3/L-phase) or a frame-rate avatar lands. If nothing ever crosses that line, the correct answer is that this decision is never made.
- **Date:** 2026-08-24
- **Status:** proposed
- **Supersedes:** none. Closes the register's "payload serialization inside the transport frame" as *deferred-with-a-trigger* rather than open.

## Why it looked urgent and is not

§7.5 lists two real justifications for a custom binary transport: **voice** (Opus
frames over UDP, where HTTP is genuinely poor) and **the GUI event stream** (token
streaming plus execution events plus avatar state at frame rate, where compact framing
beats JSON-over-WebSocket "by a real margin"). Both are true. Neither exists yet.

The frame codec deliberately treats payloads as opaque bytes (ADR-0005: framing must
not depend on the format), so nothing is blocked by leaving this open. And nothing
outside `Dami.Transport` currently constructs a frame at all — the runtime API is
HTTP+JSON by D-005, and the web view streams turns over SSE. The transport is complete,
tested (58 tests), and waiting for the consumers §7.5 named. That is the plan working,
not a component going stale.

## The measurement

Taken from the live `/events` feed and the real event stream:

| | value |
|---|---|
| JSON, as served today | **413 bytes/event** |
| compact binary, estimated (3 GUIDs, 2 enums, timestamp, sequence, label) | ~102 bytes/event |
| ratio | **4.1× smaller** |
| at a hypothetical 60 fps | 24.2 KB/s JSON vs 6.0 KB/s binary |
| **observed event rate, last 6 hours** | **1.22 events per minute** |

At the rate this system actually produces events, JSON costs about **8 bytes per
second**. A 4× saving on 8 bytes per second is not an engineering problem. The
frame-rate argument in §7.5 is about *audio and avatar state*, not execution events,
and it will stand or fall on its own evidence when those exist.

## The trigger, so this is not re-litigated from taste

Revisit when any one of these becomes true, and not before:

1. A consumer streams **audio frames** (E3/L-phase voice). Opus over UDP needs its own
   framing regardless of what the execution stream does.
2. A GUI needs **sustained >1,000 events/second** — roughly 50,000× today's rate, which
   only avatar state at frame rate would produce.
3. The event payload grows structure JSON handles badly (nested binary blobs).

Until one of those holds, adding MemoryPack or a hand-rolled span writer buys a
measurable nothing and costs a schema, a versioning story, and a debugging tool that
`curl` currently provides for free.

## When it is revisited

The candidates remain MemoryPack (fast, source-generated, schema-versioned) and a
hand-rolled span writer (no dependency, total control, and consistent with §7.5's
learning objective). The decision should be made against the *voice* payload, because
that is the consumer with a real budget — not against execution events, which are
cheap in any format.

## Consequences

None today, which is the point. The register item stops being an open question and
becomes a tripwire with a number attached.
