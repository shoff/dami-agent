# ADR 0014 — The muse waits at the door; it does not knock

- **Decision (proposed):** The surfacing queue stays the single canonical channel. Nothing pushes. The one concession to presence is *held-until-adjacent-opening*: when Steve opens a session anyway (any `dami` command, later the GUI), a single unobtrusive count line may appear — never the items themselves, never more than once per day. Notifications (desktop, phone, Discord DM) are rejected as a default and remain possible per-item only through a future explicit rule Steve writes.
- **Date:** 2026-08-24; amended and accepted 2026-09-29
- **Status:** **accepted as amended** by Steve on 2026-09-29 — see "Amendment" below. The original proposal is kept as written for the record.
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

## Amendment — 2026-09-29, accepted by Steve

Steve chose one push a day over the pure-queue posture, after a review of current
personal agents (ChatGPT Pulse was retired in June 2026 for an inferred feed nobody read;
the check-ins that survive are few, explicit, and cheap). He chose it knowing this ADR's
objection, and the amendment answers it rather than ignoring it:

1. **One check-in a day, only the strongest item.** From 09:00 America/Chicago
   (`Discord:CheckInHour`, `Discord:CheckInTimeZone`), `DiscordDailyCheckIn` sends the
   single highest-confidence pending surfacing to Steve's DM
   (`Discord:CheckInConversationId`; empty means off). A day with nothing pending sends
   nothing. Everything else stays in the queue and rides his next message as before.
2. **It is a frontier turn, not a paste.** The surfacing travels as local context, so the
   disclosure gate judges it exactly as it does on a message Steve sends; a LocalOnly
   finding (health) is withheld and the DM says only that something is waiting.
3. **H8 is protected.** The push is recorded on the surfacing (`delivered_via =
   'discord-dm'`, migration 042), and `ReactionsForServiceAsync` — the threshold tuner's
   only input — leaves reactions to pushed surfacings out. The "bad would start meaning
   'you pinged me at a bad time'" objection above no longer reaches the tuner.
4. **No retry storms.** A failed check-in is explained once and not retried until the next
   day; the day's push is read from the database, so a restart does not send a second.

Reversal: clear `Discord__CheckInConversationId`. The column is inert without it.
