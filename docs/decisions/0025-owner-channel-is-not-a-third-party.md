# ADR 0025 — The owner's own channel is not a third party

- **Decision:** Content addressed to Steve himself may cross an egress channel whatever
  its provenance. Refusal applies to recipients who are not the data subject.
- **Date:** 2026-08-30
- **Status:** accepted
- **Supersedes:** the default-refuse clause of ADR-0024 (the channel mechanism stands)

## Context

ADR-0024 refused profile-derived content on every channel. In practice it refused
everything, including `hi there`, because the test was on the wrong thing: it asked
whether retrieval had returned memories, and retrieval returns memories on every turn.
An answer containing nothing personal was refused because personal things were *available*
while it was written.

The deeper error was whose interest the rule served. D-012 reads "profile stays in,
queries go out", and its purpose is that Steve's profile is not disclosed **to others**.
The recipient on this channel is Steve. A rule that refuses to tell him what it knows
about him protects nobody and defeats the product; the first live session produced a
gateway that answered a greeting with a citation of its own decision record.

## Alternatives considered

| Option | Strengths | Weaknesses | Why not chosen |
|---|---|---|---|
| Delete ADR-0024 | Fastest | Loses `IEgressChannel`, the holder allowlist and the architecture test — all sound and all independent of the refusal rule | The mechanism was never the problem |
| Classify the answer with the local model | Precise; no personal text leaves unnoticed | A model call per reply, and a false negative is silent and unrecoverable | Kept as a future option for genuinely shared channels |
| Config flag on the old rule | Trivial | ADR-0024 explicitly refused to make this a setting, and it would still be wrong for the owner's own channel | Wrong question; the rule is misaimed, not too strict |
| Recipient-based rule (chosen) | Matches what D-012 protects; makes the gateway useful | Moves the residual risk to the transport, which must be stated rather than hidden | — |

## Evidence

- Trace `c926c5cf`: `ContextRetrieved — 13 memories, 2 beliefs`; `CapabilitySelected —
  routed Local`. Both refusal triggers fire on an ordinary question.
- Trace `baebb2e8`: the message was `hi there`. Refused.
- `ChannelDisclosurePolicy.ShouldAnswer` already drops every inbound message that is not
  from `OwnerUserId`, so the only conversation the gateway ever replies into is Steve's.
- D-012: "Personal photos and media … health/finance/relationship data" are LocalOnly.
  The clause names disclosure to outside parties, not display to Steve.

## Consequences

The gateway becomes useful: Dami answers Steve on his phone with what it knows.

**The cost, stated plainly: Discord Inc. receives, stores, and can read every answer sent
over this channel, including memory-derived ones.** The host no longer refuses it, so this
is the only thing standing between the profile and a third-party server. That is Steve's
decision as the data owner, taken on 2026-08-30 with the exposure understood.

What stays from ADR-0024: `IEgressChannel` as D-012's second mechanism, the declared-holder
allowlist, the architecture test, and the rule that a local-only service never holds a
channel. Only the refusal rule changes.

What this locks in: any *future* channel with a recipient who is not Steve — a shared
guild, a family calendar, a public bot — refuses profile-derived content and must justify
otherwise in its own ADR. The permission is to the person, never to the transport.

## Reversal path

One boolean at one call site: `ChannelDisclosurePolicy.EnsureMayLeave` takes whether the
recipient is the data subject. Passing false everywhere restores ADR-0024's behaviour
exactly, and the tests for both readings are kept side by side.
