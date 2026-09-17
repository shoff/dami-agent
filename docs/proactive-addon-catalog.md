# Proactive addon catalog — what other assistants' users love, curated for Dami

**Status: draft for Steve, 2026-09-16, Claude.** Steve said "go for it" the same day: the
suggested first three are built as board items H20 (lessons), H21 (correlation card + nudge)
and H22 (INR cadence) — see `docs/status.md` Phase 4 for what each does and what it showed
live. Everything else here is unclaimed.

## Method

Three web surveys on 2026-09-16, each with URLs per claim: (1) open-source assistant
registries — ClawHub (OpenClaw), the Hermes skills catalog, Khoj, Leon, OpenVoiceOS,
Home Assistant Assist; (2) MCP servers, Claude/ChatGPT/Gemini/Copilot connectors, Alexa
and Google Assistant categories, Raycast and Obsidian install counts; (3) the *proactive*
features users praise unprompted, mined from Home Assistant threads, HN, Exist.io, Oura,
Whoop, Monica, Google Now retrospectives, and the "disliked" genre. Where a number could
not be verified the survey says so; nothing below rests on an unverified one.

Curation filter, in order: D-012 (profile stays in, queries go out), D-020 (propose, do
not act), D-021 (scarce by design; the literature's ceiling is 3–5 interruptions a day),
what Dami already has, what data actually exists on this host (`docs/domains/inventory.md`:
health and the home network are real; civic, estate, workshop, finance have nothing), and
the success sentence — *told him something he did not ask for and is glad to have heard*.

## What the numbers say

- **Memory is the #1 addon category in every AI ecosystem.** ClawHub's top four by
  downloads are memory or self-improvement skills (`self-improving-agent` 479,725
  downloads); the reference MCP `memory` server does 410k npm downloads a month; Hermes
  ships eight memory providers. Almost all of it is *coding* memory. Dami's corpus and
  ledger are the life-memory version, and after H19 the reflection pass can finally read them.
- **The same six integrations dominate everywhere:** mail, calendar, notes, tasks, files,
  chat. Claude's top three connectors are Drive, Gmail, Calendar; Copilot's consumer
  connectors are Outlook, Gmail, Calendar, Drive, Contacts; ClawHub's `gog` (Google
  Workspace) has 194k downloads. **Dami has none of the six.** That is the largest gap, and
  every one of them is a privacy design, not a feature.
- **Proactive scheduling is table stakes, and the morning briefing is universal:** OpenClaw
  wakes its agent every 30 minutes by default; ClawHub has 24 "briefing" skills; Khoj's
  flagship automation is a scheduled brief; Google is re-shipping Google Now as Gemini's
  "Your Day" feed and a "Daily Brief" (I/O 2026). Dami's `today` tool and the runbook's
  morning briefing are the right shape; the feed is the wrong content (see "stop doing").
- **Praised proactive items share three traits:** a single surprising fact *with its
  evidence* ("parcels delivered make me 136% more productive", Exist), admitted uncertainty
  ("sometimes a false alarm", Oura), and no verdict on the person (a trend, not a grade).
- **Almost everything people love is local-only.** Of thirty praised features, only
  time-to-leave (a route query) and package tracking (a tracking number) leave the host
  with anything linkable. D-012 costs Dami very little here.
- **Voice usage is timers, music, weather, questions; third-party skills never took**
  (77% of smart-speaker owners play music weekly, 48% ever tried a skill). Relevant to L2:
  the wake word needs those four before anything clever.

## Tier 1 — build next: data is already on this host, local-only, fits an existing seam

| # | Feature | What it is | Why people love it | Data on host | Seam in Dami | Size |
|---|---|---|---|---|---|---|
| 1 | **Cross-domain correlation card** | Once a week, one correlation with its n and confidence: gym volume, sleep, commits, Discord activity, weather, late nights | Exist.io's flagship; the surprise is the feature ("on days I rate mood 2/5 I have no coffee or two") | `fitness_*`, `domain_facts` (weather), git log, `conversation_turns`, `execution_events`, `health_events` | `ReflectionService` — a second, numeric pass beside the belief pass; surfaces at most one card | M |
| 2 | **"Lowest in N weeks" single-fact nudge** | "Yesterday was your lowest gym volume in six weeks" / "most commits in a month"; trend only, never a grade | Exist users cite exactly this as the nudge that worked | same daily series | a small nightly service over one table of daily series; D-021 cap of one | S |
| 3 | **On This Day / random old note** | Resurface a corpus row from this date in prior years, or a random one on a quiet day, with a suppression list | Day One users "look forward to it daily"; Obsidian's Random Note; the grief trap is real (Eric Meyer's Year in Review) — suppression list is mandatory | the 7,000-row corpus (`occurred_at`; epoch-zero survivors say "undated") | rides the Discord "noticed" slot or `today`; local only | S |
| 4 | **Lessons ledger (self-improving loop)** | Every correction Steve makes ("I am not concerned about privacy of my workouts") becomes a `lesson` observation the frontier sees on every turn, and a monthly "what I got wrong" review | ClawHub #1 skill by a wide margin; Hermes `/learn`; the one addon users install first | `surfacing` feedback, `disclosure_corrections`, `pushbacks`, `bad` ratings — already recorded, never fed back as instructions | a `lessons` source in the corpus + a bounded block in the frontier prompt (ADR-0034 shape) | S |
| 5 | **Your week / quarterly Wrapped** | Sunday: commits, gym, conversations, surfacings rated good, beliefs added, portraits — a page, and once a quarter an image | Wrapped hit 200M users in a day; the self-hosted world has cloned it for Jellyfin, Actual, Trakt | everything above | `FitnessReviewService` already does "your week in the gym"; widen it; the quarterly image goes through the existing gallery generator | M |
| 6 | **INR / medication cadence** | "Your last INR was noted 2026-08-30; the interval you have kept is ~3 weeks" — a tender, evidence-carrying reminder, never a nag | Medication reminders are a top ClawHub health skill; Oura's "heads-up" framing is the one users forgive when wrong | `health_events` (84 facts incl. INR, warfarin) | `HealthCollectorService` timeline → one surfacing when a cadence lapses; disclosure gate already passes his own health facts | S |
| 7 | **Behaviour-to-recovery attribution** | Monthly: which tagged behaviours (late night, alcohol, skipped day) moved next-day gym performance | Whoop Journal's monthly assessment; users report finding the two-drinks-before-bed effect themselves | gym log + `chat` timestamps (late nights are visible); behaviours need a two-word tag via Discord | `log_sets` gains an optional `notes` → tags; `FitnessInsights` gets a monthly comparison | M |
| 8 | **Price watch on a hobby list** | A list of product URLs and target prices (kits, airbrush parts, GPUs); surfaces only on a drop | camelcamelcamel "never asks for money"; a mourned Google Now card; Hermes bundles `product-price-monitor` | a list Steve gives; public product pages | new nightly collector on the D-012 split: egress fetch of a public URL, local compare; hosts allowlisted one by one | S |
| 9 | **Raise the scout's bar** | The HN scout delivers at 0.50–0.61 and is 65 of 173 surfacings in 30 days; the praised version "drops anything below an 8" | Feed drift is why Google Now died; a 3–5/day ceiling is the 2026 consensus | none | `InterestScout__SurfaceThreshold` and `MaxItemsPerPass` — a config change plus the tuner's floor | XS |

## Tier 2 — needs a source or a decision from Steve

| # | Feature | Why | What Steve must decide |
|---|---|---|---|
| 10 | **Calendar** (CalDAV or Google) → day preview in `today`, time-to-leave, the week's shape in reflection | The single most-used integration in every ecosystem; `ovos-skill-alerts` and HA `local_calendar` are the local-first versions | Which calendar; whether Google credentials may live on the host; time-to-leave needs a route query (round the origin) — ADR |
| 11 | **Email triage** (IMAP, local) — unanswered-sent follow-ups *with a drafted reply*, and a morning "three things need you" | inbox-zero 12k stars; Gmail Nudges are liked only when they draft, not when they only remind | Which account; IMAP read-only first; drafts never sent (D-020) — ADR |
| 12 | **Stay-in-touch prompts** — "you have not spoken to X in three months", framed as a prompt to Steve, never a script | Monica's "killer feature" per HN; same thread's "fedora and trenchcoat" objection says: never automate the contact | Source of contact metadata: Discord DMs, iMessage via the Mac bridge; and whether he wants it at all |
| 13 | **Ledger anomalies** — forgotten subscriptions, price increases, duplicate charges, over an imported CSV/OFX | Rocket Money's whole pitch ("saved me $347 in month one"); Monarch is criticised for making it visible only "when you look" | An export he is willing to put on the host; finance is a domain with zero corpus signal today |
| 14 | **Apple bridge via the Mac** — Reminders, Notes, Find My, iMessage | Both OpenClaw and Hermes bundle these in their top tier; onboarding §7 already permits the Mac as a bounded Apple-services bridge | Scope of the bridge; no second authoritative Discord gateway |
| 15 | **Home Assistant** — left-open sentinel, appliance-finished, leak → alert, bin night, sleep-mode arms the house | The most-praised HA automations, all local; HA is the only home/health/finance integration with real MCP traction (4.8k stars) | Whether HA exists in the house at all (inventory says no home data) |
| 16 | **Living-alone check** — no keyboard, Discord, or phone activity by a set hour → one message to him, then to a named person | The one safety feature nobody else provides for a single-user agent; hobbyist builds exist for parents | The named person, the hour, and consent: the escalation must leave the host, so D-020 does not cover it — ADR |

## Tier 3 — fun, cheap, zero information

| # | Feature | Why | Seam |
|---|---|---|---|
| 17 | **Entrance theme / morning line on the speaker** | An HA config author's "everyone's favorite feature"; pure delight | `dami-tts` (Piper) exists; needs a presence signal (first keyboard activity, or the L2 wake word) |
| 18 | **Portrait that tracks the week** | Garmin's Pokémon Sleep tie-in: "a tamagotchi for adults that emotionally blackmailed me into a better bedtime" | `DailyPortraitService` already runs 3×/day; the scene takes the week's facts (gym streak, late nights) through the disclosure gate |
| 19 | **Belief quiz** — "true or false: you build momentum by shipping vertical slices" | Trivia is Alexa's largest category; this version doubles as the F-09 ledger correction loop | `conclusions` + `dami good\|bad`; one question a week in the Discord "noticed" slot |
| 20 | **Photo memories** from the local gallery | Google Photos Memories: 3.5B views a month; local vision only | Phase 6 media librarian, already planned; `gallery_images` has captions and vectors |
| 21 | **Resurface what he rated good** | Readwise Daily Review: spaced resurfacing compounds over a year | `surfacings` where feedback = good, on an interval, not an anniversary |

## Stop doing, or do not start (the disliked list, with Dami's own numbers)

- **Piggybacked suggestions.** Alexa's "by the way" spawned a genre of how-to-silence
  articles. The Discord "noticed" slot appends up to five items to an unrelated answer —
  that is the same pattern. Cap it at one, or move surfacings to their own message.
- **Guilt-based nagging.** Duolingo's escalation works on retention and breeds resentment.
  `repo-hygiene` has 17 surfacings in 30 days, one rated `bad`, none `good`; "33 commits not
  pushed" is a Duolingo notification. Once a week, or only on change.
- **Nightly grades.** 30.9% of sleep-tracker users screen positive for orthosomnia risk.
  Report trends; never score a day.
- **Feed drift.** Google Now was loved until it became a news feed. Three portraits and three
  HN links a day is the drift. Tier 1 #9 and a 3–5/day budget across *all* services.
- **Record-everything capture.** Microsoft Recall's backlash was about the concept, not the
  bug. Capture scope stays explicit and per-source; the corpus never gets screenshots.
- **Relationship-as-database.** Tier 2 #12 is a prompt to Steve, never an automated message.

## Suggested first three

1. **#4 lessons ledger** — smallest change with the largest effect on "feels sterile"; it is
   the addon users install first everywhere.
2. **#1 correlation card + #2 single-fact nudge** — the reflection pass finally has a window;
   give it numbers as well as prose. This is the Phase 4 exit sentence in service form.
3. **#6 INR cadence** — the one item on this list that is specifically about Steve, from
   data already extracted, that he would be glad to have heard.

Then the Tier 2 decisions in the order calendar, email, living-alone check — each an ADR.

## Sources

The three survey reports, with per-claim URLs, are in the 2026-09-16 work-log entry's
scratchpad reference; the key ones: https://clawhub.ai/api/v1/skills?sort=downloads ·
https://hermes-agent.nousresearch.com/docs/reference/skills-catalog · https://www.pulsemcp.com/servers?sort=popular-total-desc ·
https://claude.com/connectors · https://claude.com/plugins · https://www.raycast.com/store/popular ·
https://raw.githubusercontent.com/obsidianmd/obsidian-releases/master/community-plugin-stats.json ·
https://analytics.home-assistant.io/data.json · https://exist.io/blog/four-years-tracking-with-Exist/ ·
https://community.home-assistant.io/t/what-is-your-most-useful-automation/648543 ·
https://news.ycombinator.com/item?id=25270001 (Monica) · https://news.ycombinator.com/item?id=4171274 (Google Now) ·
https://news.ycombinator.com/item?id=47296664 (scored digest) · https://www.droid-life.com/2017/11/28/else-misses-google-now/ ·
https://9to5google.com/2026/04/13/gemini-your-day-feed/ · https://pmc.ncbi.nlm.nih.gov/articles/PMC11592250/ (orthosomnia) ·
https://tianpan.co/blog/2026-05-13-background-agents-notification-budget-attention-economy
