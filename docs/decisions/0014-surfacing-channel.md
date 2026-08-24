# ADR 0014 — The muse waits at the door; it does not knock

- **Decision (proposed):** The surfacing queue stays the single canonical channel. Nothing pushes. The one concession to presence is *held-until-adjacent-opening*: when Steve opens a session anyway (any `dami` command, later the GUI), a single unobtrusive count line may appear — never the items themselves, never more than once per day. Notifications (desktop, phone, Discord DM) are rejected as a default and remain possible per-item only through a future explicit rule Steve writes.
- **Date:** 2026-08-24
- **Status:** proposed — the register says this decision "shapes the muse more than model choice does", which makes it Steve's; nothing changes until accepted
- **Supersedes:** none. Completes the D-021 posture the cap and suppression began.

## Context

Everything upstream is built: passes conclude quietly (D-019), a capped queue
holds at most a few surfacings a day with suppressions stored (D-021), reactions
feed a taste model and now tune the threshold itself (H8). What was never decided
is the *delivery posture* — does Dami interrupt, wait, or wait visibly?

## The three candidates

| Channel | What it optimizes | What it costs |
|---|---|---|
| Pure queue (today's behavior) | Steve's attention is never taken, only offered | Discoveries can sit unread for days; the muse is effectively mute if `dami inbox` isn't a habit |
| Notification push | Timeliness | Every push is an interruption Dami chose for Steve; the charter's "a muse that talks constantly is an infestation" applies to *pings*, not just items — and taste feedback would start measuring annoyance, poisoning H8's signal |
| Held-until-adjacent-opening | Timeliness *inside* attention Steve already chose to give | Requires a session hook; invisible to someone who never opens one |

## Proposal

Queue canonical + adjacent-opening presence line:

1. `dami` (any verb) may print at most one line — `3 surfacings waiting · dami inbox`
   — and only if there are unread items and the line hasn't shown today. The items
   themselves never auto-print.
2. No process ever pushes to a device. If a class of finding ever justifies it
   (smoke detector, not muse), that is a *rule Steve writes* naming the class —
   not a default any service can reach for.
3. The GUI (J3) inherits the same posture: a badge, not a toast.

Why: the interruption cost of push lands on the exact resource — Steve's focus —
this whole system exists to protect, and it corrupts the feedback loop: `bad`
would start meaning "you pinged me at a bad time", not "this was a bad find".
The adjacent-opening line spends attention Steve has already decided to spend.

## Evidence

D-021's cap observed live (suppressions stored, auditable). H8's tuner reads
reaction lean; its anti-gaming argument assumes reactions rate the *finding* —
push would break that assumption. The count line costs one queue query in
commands that already open a database connection.

## Consequences

If accepted: one small CLI hook (unread count + a last-shown-date marker), a
GUI posture note in the J3 item, and the register's channel question closes.
If Steve prefers pure queue, delete the hook — nothing else depends on it.

## Reversal path

The hook is one method; remove it and the queue remains exactly what it is today.
