# Personal-agent landscape and the Dami upgrade list — 2026-09-29

**What this is.** A feature comparison of Dami against the personal agents people actually
run (Hermes Agent, OpenClaw and its forks, Meta Muse, Letta, Agent Zero, the commercial
assistants), then what users report *doing* with them, then every upgrade that evidence
suggests for Dami — deduplicated against what Dami already has and against
`proactive-addon-catalog.md` (cross-referenced as **cat #n**, not repeated).

**Method.** Four research passes on 2026-09-29: a capability inventory of this repository
(`status.md`, runbook §1, ADRs, work-log, `git log`, grep for absences); primary sources
for each agent (GitHub READMEs, docs, changelogs, release notes); user reports from Hacker
News (Ask HN threads 47783940, 46850907, 46838946, 49871556), the Hermes user-stories
page, the OpenClaw showcase, blogs, the ClawHub install API and GitHub issues; and Reddit
through the Arctic Shift archive (reddit.com blocks direct fetches), chiefly r/openclaw,
r/hermesagent, r/clawdbot, r/homeassistant, r/ClaudeAI, r/selfhosted, r/n8n.

**Caveats.** `[R]` means a pattern seen from three or more independent users; `[1]` is one
anecdote. Items marked *unverified* rest on one secondary source. HN commenters warn that
some showcase posts are founders and marketers (HN 47784198, 46839101); Meta Muse is three
weeks old and has incident reports but no verified long-term use; Letta, Goose and Agent
Zero have little first-hand usage outside their own docs.

---

## 1. Which agents, and which "Muse"

| Agent | State on 2026-09-29 | Scale |
|---|---|---|
| **Hermes Agent** (Nous Research) | v0.21.5, 2026-09-24; very active | MIT, ~250k★, 760+ contributors |
| **OpenClaw** (ex-Clawdbot/Moltbot) | 2026.9.6; foundation-run since its creator joined OpenAI (02-2026) | MIT, ~391k★; ClawHub 10.7k skills |
| **NanoClaw** / ZeroClaw / IronClaw / NemoClaw | lean or hardened forks (containers, Rust, WASM-per-tool, network policy) | NanoClaw 30.9k★ |
| **Meta Muse** | launched 2026-09-08, "a turnkey OpenClaw", same SOUL/IDENTITY/MEMORY.md files | proprietary; 83k installs in 2 days |
| Letta Code · Agent Zero · Goose · Vellum · Leon 2.0 · ElizaOS | alive; niche | 3.5k–54.8k★ |
| Khoj | **Cloud shut down 2026-04-15**; self-host only, stagnant | — |
| ChatGPT · Claude (Cowork) · Gemini Spark · Lindy · Manus | commercial; ChatGPT retired Pulse (06-2026) for scheduled tasks | — |

"Muse" is almost certainly **Meta Muse** ([about.fb.com](https://about.fb.com/news/2026/09/introducing-muse-personal-ai-agent/),
[StarkInsider comparison](https://www.starkinsider.com/2026/09/meta-muse-vs-openclaw-personal-ai-agent.html)).
Other hits: `wlsdks/muse-agent` (5★, notable only for grounded citations, abstaining at low
confidence, and draft-first outbound), a Windows gateway and a CLI for muse.ai.

---

## 2. Feature comparison

Dami's column is from this repository; `live` unless marked. Terse on purpose.

| Dimension | **Dami** | **Hermes** | **OpenClaw** | **Meta Muse** | Letta / Agent Zero | ChatGPT · Claude · Gemini |
|---|---|---|---|---|---|---|
| **Channels** | Discord DM; CLI; Avalonia GUI; web view (LAN via TLS proxy) | 20+ (Discord incl. voice channels, Signal, Matrix, email, SMS, HA), desktop app, Android | 20+, Control UI, macOS/iOS/Android nodes | app, WhatsApp, Telegram, web | CLI/desktop/Slack/Telegram · web UI, Telegram, WhatsApp, email | own apps, voice; Claude Dispatch (phone→desktop) |
| **Voice** | STT (whisper) + TTS (Piper) sidecars; no wake word, no voice loop | streaming TTS with barge-in, on-device wake word | Voice Wake, Talk mode | — | — / TTS+STT | voice modes |
| **Models** | frontier = Codex on subscription; local qwen3:8b for retrieval, planning, gate; qwen2.5vl vision; Anthropic adapter dormant | 20+ providers, per-model overrides | any | Muse Spark only | any; memory portable across models | vendor only |
| **Memory** | append-only corpus (~7k Hermes rows + Discord turns), beliefs ledger with as-of diff/correct/retract, bge-m3 + reranker, lessons (quoted, 90-day lapse) | MEMORY.md + USER.md, FTS5 session search, 8 pluggable providers | MEMORY/USER + daily notes, hybrid vector+BM25, **Active Memory** sub-agent, **dreaming** into DREAMS.md, memory-wiki contradictions, import from Hermes/Claude Code | curated file + daily logs + "forget"; compaction drops transcripts | git-versioned self-editing blocks, sleep-time rewrite of own prompt · per-project vector memory | read/edit memory; ChatGPT per-reply "Memory Sources" (*unverified*) |
| **Proactivity** | hourly tier, ~25 services, capped queue, self-tuning threshold (H8), daily Discord check-in, "noticed" rides next message | cron with memory carried between runs; **monitor mode skips the LLM when nothing changed** | HEARTBEAT.md (per-task intervals), cron, **inbound webhooks**, standing orders, Task Flow | cron, event hooks, goal→multi-week plans | heartbeats, crons · scheduler | scheduled-task hubs, change-only alerts; Gemini Daily Brief |
| **Skills / tools** | fixed frontier bundle (~20 tools); capability registry (native, MCP, filesystem skills, sandboxed promoted tools); **no marketplace (by decision)** | SKILL.md, Skills Hub, MCP command center, self-written skills | ClawHub, plugin SDK, skills from past chats | 33 connectors + custom | skills · SKILL.md, Plugin Hub, MCP | connectors, MCP, skill registries |
| **Computer / browser** | none (public-only page reader) | in-app browser | browser, Canvas, device nodes | real Chromium with logins, purchases | — · full XFCE desktop + browser | agent VMs; Claude computer use (~50% in one review) |
| **Integrations** | health timeline, fitness, network, civic, weather, recalls, CVEs, releases, gallery | via MCP/skills | largest catalog (Gmail/Workspace is top-10) | Gmail, GCal, Outlook, Plaid, Hue, Withings, Peloton, Tessie, Spotify, Stripe Link, 1Password | — · email | Workspace, Gmail, calendars |
| **Multi-agent** | CodeWork hands a task to Codex on a branch | subagents with live steering, schema-checked output, cost per delegation, A2A | multi-agent routing, subagents | subagents | subagents · hierarchy | subagents |
| **Security** | egress allowlist + tripwire, **three-way disclosure gate** on every profile line, consent briefs, loopback + auth-gated LAN, sandboxed tools need human promotion | approvals + LLM "smart approval", protected instruction files, 7 sandboxes, vault; ~6 CVEs in 2026 | DM pairing, VirusTotal skill scan; ClawHavoc (824–1,184 malicious skills), CVE-2026-25253, "Claw Chain" | per-user VM, **Sentinel** egress (read vs write, time-limited grants), audit of planned actions; synced 187k Messages lines without permission (appleinsider 09-28) | — · per-project secrets, scoped tool policies | VM sandboxes; Gemini "may purchase without asking" |
| **Observability** | durable replayable traces, `dami trace`, journald→Elasticsearch/Kibana, GUI tab | Langfuse, cost overlay, live cache/latency | diagnostics bus, Langfuse/OTel | action audit log | exact token view · workspace time-travel | — |
| **Learning** | lessons from Steve's quoted words; corrections to the gate become examples; H8 tunes on reactions | writes and refines its own skills | dreaming consolidation | learns priorities | rewrites own prompt | — |
| **Persona** | identity prompt, daily portraits, Gallery | SOUL.md, bot avatars | SOUL.md | named agent, avatar | identity · — | — |
| **Deployment** | one Linux workstation, .NET + Postgres bare metal, GPU sidecars in Docker | Python; VPS/Docker/Modal | Node; Docker/Nix | hosted | npm · Docker | hosted |

**Where Dami is ahead.**
- Privacy is enforced in code: an egress allowlist, a per-line disclosure gate, and consent briefs. No surveyed agent judges each memory line before it leaves the host.
- The skill-marketplace attack surface does not exist, by decision. The top ClawHub installs after "self-improving-agent" are two *security vetters* (`skill-vetter` 12,219 installs, `skillscan` 5,955), which shows what that surface costs its users.
- Tools Dami writes for itself are sandboxed and need Steve to promote them.
- Proactive work is scarce by design: a capped queue, a self-tuning threshold, and a check-in that H8 does not learn from. Users quit OpenClaw over heartbeat cost and noise; that complaint is designed out here.
- Every turn is a replayable trace.
- The beliefs ledger has history (as-of, diff, retract), where the others keep flat files.

**Parity.**
- A chat-app interface.
- Scheduled prompt jobs.
- Research with a private search engine.
- Images.
- Local STT/TTS.
- A morning brief (`today`, the check-in).
- Correction capture (lessons).
- Coding hand-off.

**Behind.** These are the upgrade list's main sources:
- Dami has no email, calendar, browser, second chat channel, voice loop, smart home or mobile quick-capture.
- Memory is not exportable, and not viewable per reply.
- There is no change-only gating for jobs.
- There is no "is everything actually running" page. That gap is what user churn is made of (§3).

---

## 3. What users actually do — and why they quit

Merged from ~150 reports; counts are rough mentions across HN, blogs and Reddit.

| # | Use | Mentions | Notes |
|---|---|---|---|
| 1 | Morning/evening brief on a schedule (weather, calendar, mail, news, markets), often as audio | ~20 | the first thing everyone builds **and the most abandoned** — "broke every other morning", "$40–50 the first week", "said it fixed itself" (HN 47785456) |
| 2 | Email: triage, drafts-never-sends, forwarding mail *to* the agent's own address | ~14 | the durable pattern is a dedicated agent inbox fed by forwards (school/PTA, travel confirmations, invoices, vet mail) |
| 3 | Calendar, especially the family calendar: events from texts, screenshots, school emails | ~13 | "promise detection" in messages every 15 min (brandon.wang) |
| 4 | Reminders, to-dos, ADHD capture and nudges | ~12 | 28 cron nudges (Hermes docs); rollovers that ran twice or not at all on NanoClaw (HN 47791091) |
| 5 | Digests and watchers: HN/X/YouTube, niche news, fuzzy-criteria monitors (apartments, event pages, kids' activities, prices) | ~12 | "notify only if new activities start in March" (HN 47428959) |
| 6 | Homelab / Home Assistant ops: configs, *arr, AdGuard, uptime, camera Q&A | ~10 | "3 minutes vs 30" |
| 7 | Remote-control coding agents from the phone | ~10 | |
| 8 | Receipts, bookkeeping, subscriptions ("chief money officer") | ~8 | receipt photo → ledger is the most repeated finance item |
| 9 | Food/calorie/workout logging by chat or photo | ~8 | "months of data as MD files" |
| 10 | Notes / second brain — Obsidian vault in git as memory | ~8 | chosen because it is readable and model-portable |
| 11 | Family/household shared assistant | ~8 | own Gmail, shared calendar, sandboxed from the personal agent |
| 12 | Groceries, meal plans, pantry from photos | ~7 | |
| 13 | Trading and portfolio analysis | ~7 | |
| 14 | Small-business ops (work orders → proposals → invoices) | ~6 | a gardener: "freed up my home life" (HN 47784724) |
| 15 | Companion / persona | ~5 | divisive: "too weird", "burns tokens anthropomorphising" |

**Why setups last.** They are boring, scheduled and checkable: receipts, reminders, the
family calendar, one brief. Deterministic collection runs in a script and the model only
judges (Hermes monitor mode, NanoClaw script gates, the two-tier email pipeline).

**Why people quit.** The causes are overwhelmingly operational, not a lack of use cases:
- **Silent failure.** Crons that don't deliver, and agents that "claim work they never did".
- **Babysitting.** Updates break things; "stable version > new features" (OpenClaw #5799, 52 👍).
- **Cost shock.** ~30k tokens of context per turn, 180M tokens a month.
- **Memory that forgets** unless told to look.
- **Incidents.** An agent answered all of a user's iMessages overnight. 200+ emails were deleted after compaction dropped a "confirm first" instruction. An update deleted 1,617 files.

**What the most-installed skills say.** ClawHub by installs:
1. `self-improving-agent`
2. `skill-vetter`
3. `github`
4. a second self-improvement skill
5. `ontology` (knowledge-graph memory)
6. `proactive-agent`
7. `weather`
8. `skillscan`
9. multi-search
10. `gog` (Google Workspace)

People install *memory, learning and safety* first, and task skills second.

**Top feature requests on GitHub.** Native desktop apps; a stabilisation mode; more model
providers; external memory and dreaming; multi-agent; backup and versioning of agent data
(Hermes #12238); real-time voice (OpenClaw #7200); token-overhead cuts such as lazy tool
schemas; guardrails (exec denylist, masked secrets, egress firewall, filesystem sandbox).

---

## 4. The upgrade list

Every upgrade the evidence supports, grouped by area. **Effort**: S ≈ a session, M ≈ a few
sessions, L ≈ a phase. **Hook** is the existing Dami seam it attaches to. ⚠ marks a
privacy or security risk that needs a design note or ADR before building. Items already in
`proactive-addon-catalog.md` are referenced, not restated.

### A. Reliability and trust — the reason users quit

| ID | Upgrade | Evidence | Hook | Effort |
|---|---|---|---|---|
| A1 | **"Is Dami OK" page and daily line**: every service, job and sidecar with last success, last failure, next run; the check-in names anything that failed silently | #1 churn cause; "runs for weeks without checking" wish (Reddit oc/1s7wfqr) | `proactive_runs`, `scheduled_jobs`, web view | S |
| A2 | **Job delivery audit**: each scheduled job ran exactly once and delivered; misses and duplicates surfaced | NanoClaw double/forgotten rollovers (HN 47791091); "crons not delivered" [R] | G16 jobs, run history | S |
| A3 | **Claims need evidence**: a job or turn that says "done" must have a successful tool span for it in the trace, else it is flagged | "claims work it never did", "said it fixed itself" [R] | traces | M |
| A4 | **Sidecar-down alert to Discord** (a smoke-detector class under ADR-0014 §2, not a muse push) when `dami-host` is up but the GPU sidecars are not | today's 40-hour outage; guard only notifies the desktop | `dami-llm-guard`, egress channel | S |
| A5 | **Gate resilience**: when the local gate's reply is unreadable, retry once with a smaller batch before withholding the whole turn | 2026-09-29 14:43: all 23 lines withheld on one unparseable reply | `LocalDisclosureGate` | S |
| A6 | **Weekly reliability report**: turn failures, gate withhold/unreadable rates, job misses, sidecar restarts | would have caught both 09-29 defects early | ES/Kibana, `disclosure_decisions` | S |
| A7 | **Quota and cost meter**: Codex subscription consumption per turn/job/day, with a warning before the weekly cap | a Dutch-practice skill burned 20% of a Codex weekly quota (HN 47786207); cost shock [R] | C5 budget alarm, traces | S–M |
| A8 | **Off-host encrypted backup** (board A4) and **versioned export of Dami's state** | Hermes #12238 (26 👍); platform-risk stories [R] | ADR-0003 | M |
| A9 | **Soak periods**: a rule that a deploy with new behaviour gets 48 h of `dami health` + A1 green before the next feature | OpenClaw #5799 "stabilisation mode" (52 👍) | process, `deploy.sh` | S |
| A10 | **Kill switch**: one Discord command and one desktop button that pauses Dami (services and jobs) and says so | friends'-chat bot with a Tailscale kill switch (HN 46849973) | `tools/dami-down` | S |

### B. Memory

| ID | Upgrade | Evidence | Hook | Effort |
|---|---|---|---|---|
| B1 | **Per-reply sources**: a 🔎 reaction (or `dami why <trace>`) lists which beliefs, observations and lessons the turn used and which the gate withheld | ChatGPT "Memory Sources" (*unverified*); Claude/ChatGPT memory transparency is now the norm | traces, `disclosure_decisions` | S–M |
| B2 | **"Forget that" in chat**: retract a belief or tombstone an observation from Discord, with the ledger entry | Muse "forget"; Claude/ChatGPT edit [R] | retract, `observation_curations` | M |
| B3 | **Memory page** in the web view: browse, edit, retract beliefs and lessons; see what reflection learned this week | memory transparency [R] | G31 About page | M |
| B4 | **Nightly consolidation diary** ("dreaming"): extract decisions, open loops, mistakes and new facts from the day into one reviewable note; reflection stays weekly | OpenClaw DREAMS.md, Letta sleep-time, Hermes 3am job | reflection, corpus | M |
| B5 | **Daily note + rollups**: one note per day, indexed not injected; weekly and monthly summaries | OpenClaw/Muse daily logs; HN 47786020 | corpus, curator | S–M |
| B6 | **Markdown export to a git vault** (Obsidian-readable): beliefs, lessons, daily notes; nightly | top HN memory pattern: readable, model-portable [R] | beliefs ledger, lessons | S–M |
| B7 | **Import**: ChatGPT/Claude memory exports and an Obsidian vault into the corpus | OpenClaw imports from Hermes/Claude Code | corpus import (as B3 Hermes) | M |
| B8 | **Hybrid recall**: Postgres full-text (BM25-like) fused with pgvector before the reranker | OpenClaw, Vellum | `recall`, ContextBuilder | M |
| B9 | **Contradiction detection**: a new observation that conflicts with an active belief surfaces "this contradicts what I believed since …" | OpenClaw memory-wiki | beliefs ledger, reflection | M |
| B10 | **Per-kind staleness**: location, plans and prices lapse faster than preferences; lapsed beliefs drop out of context | Vellum's eight memory types with staleness windows | conclusions | M |
| B11 | **Lessons review**: a monthly check-in "these N lessons steer me — keep or drop?" | Hermes' self-improvement rot (H20 follow-on) | lessons, check-in | S |
| B12 | **Hard rules**: a class of standing order ("never send without asking") enforced at the tool layer, not just in the prompt | 200+ emails deleted after compaction dropped "confirm first" | tool bundle, approvals | M |
| B13 | **Recall eval**: plant facts, measure whether recall and the gate return them weeks later | "remembers trivial details" is the most-praised value; "forgets unless told" the complaint | eval tooling (as ADR-0015) | M |

### C. Proactivity and scheduling

| ID | Upgrade | Evidence | Hook | Effort |
|---|---|---|---|---|
| C1 | **Change-only gates for jobs**: a deterministic pre-check (fetch, hash, compare) and the model runs only on change | Hermes monitor mode, NanoClaw script gates, ChatGPT change-only alerts [R] | `ScheduledJobKind`, research reader | M |
| C2 | **Job memory**: a scheduled job sees its own last N outputs ("don't repeat yesterday's") | Hermes cron memory carryover | G16 jobs | S |
| C3 | **Watch tasks**: "tell me if X changes / appears / drops below Y" on a URL or search, conversationally created | fuzzy monitors [R]; cat #8 is the price subset | `schedule` tool, SearXNG, D-012 allowlist ⚠ per host | M |
| C4 | **Event-conditioned reminders**: "remind me when the INR result is in / when the package ships" | OpenClaw intents | jobs + observations trigger | M |
| C5 | **Goals → plan with check-ins**: a stated goal becomes a multi-week plan whose milestones ride the check-in | Muse goal plans; ADHD accountability wish | board, check-in | M |
| C6 | **Voice check-in**: attach the daily check-in as a Piper voice note | audio briefs [R] | `dami-tts`, Discord attachments | S |
| C7 | **Evening prep**: 20:00 "tomorrow" preview (needs D1) | brandon.wang | `today`, calendar | S |
| C8 | **Actionable weather rules**: gusts over N → "bring the furniture in"; pollen; frost | [R] wind/pollen alerts | weather-window | S |
| C9 | **Inbound webhooks** with per-source tokens: Home Assistant, GitHub, forms can wake Dami | OpenClaw webhooks | Host endpoint, auth ⚠ | M |
| C10 | **Urgent-attention escalation**: a named class may call or SMS | "reliable way to get my attention" wish | ADR-0014 §2 rule, Twilio egress ⚠ | M |
| C11 | **Weekly "what I did for you"** (cat #5 Wrapped covers the Steve-side half) | weekly agent blog (HN 47224820) | traces, surfacings | S |

### D. Integrations (data in)

| ID | Upgrade | Evidence | Hook | Effort |
|---|---|---|---|---|
| D1 | **Calendar read** (cat #10) → `today`, check-in, reflection | #3 use overall | new collector ⚠ | M |
| D2 | **Dami's own mailbox**: Steve *forwards* things (confirmations, receipts, vet, school, invoices); a tool-less local model extracts; nothing is ever sent | the durable email pattern [R]; avoids giving it Steve's inbox | IMAP collector, quarantine ⚠ | M |
| D3 | **Email triage of Steve's inbox**, read-only, drafts only (cat #11) | #2 use; highest injection risk | quarantine ⚠⚠ | L |
| D4 | **Receipt photo → expense ledger** (Discord photo, local vision) | most repeated finance item [R] | `DiscordVision`, domain facts | S–M |
| D5 | **Bank CSV/OFX + subscription watch** (cat #13) | "chief money officer" [R] | new domain | M |
| D6 | **Food photo → calories/protein** beside the fitness log | [R] | fitness domain, vision | S–M |
| D7 | **Wearables** (Garmin/Withings/Apple Health export): sleep and HRV into the check-in and correlation card | "your calendar doesn't know you slept badly" | health timeline, H21 sources ⚠ | M |
| D8 | **Paperless-ngx / Immich / document folder Q&A** ("how old is the coffee machine?") | [1]×2 | local MCP or collector | M |
| D9 | **Obsidian vault read/write** (pairs with B6) | [R] | filesystem skill | M |
| D10 | **Home Assistant** (cat #15) incl. **camera Q&A** ("when did the package come?") via Frigate events | [R] | MCP (HA ships a server) ⚠ | M |
| D11 | **Package and flight tracking** from forwarded confirmations (via D2) | replaces Parcel/Flighty (brandon.wang) | D2, watch tasks | M |
| D12 | **RSS/blog feed list** beside the HN scout | `blogwatcher`, `news-summary` in ClawHub top 30 | interest-scout | S |
| D13 | **YouTube/podcast → local transcript → summary + reading list** | [R] | `dami-stt`, research library | M |
| D14 | **GitHub digest** of this repository (PRs, CI-less gate results, Codex branches) | `github` is #3 on ClawHub | CodeWork, repo-hygiene | S |
| D15 | **Pantry/fridge from photos → shopping list** | [R] | vision | M |
| D16 | Contacts / stay-in-touch (cat #12) · Apple bridge (cat #14) · living-alone check (cat #16) | — | — | — |

### E. Channels and interfaces

| ID | Upgrade | Evidence | Hook | Effort |
|---|---|---|---|---|
| E1 | **Discord voice notes in**: an audio attachment → `dami-stt` → the turn | voice-note assistants [R] | Discord attachments, STT | S |
| E2 | **Voice replies out** on request or for the check-in (C6) | [R] | `dami-tts` | S |
| E3 | **Per-purpose Discord channels**: `#inbox` (anything pasted is filed), `#receipts`, `#bookmarks` | Discord-channel-per-skill (r/hermesagent) | gateway routing | S |
| E4 | **Phone quick capture**: installable web view (PWA) with an Android share target → Dami | phone handoff [R]; no mobile app today | G31 web view, LAN proxy | M |
| E5 | **Desktop quick-entry hotkey** (Avalonia global hotkey → one-line capture) | Hermes | GUI | S |
| E6 | **Wall / e-ink "today" display** | TRMNL (HN 47224820); fridge calendars [R] | web view page | S |
| E7 | **Real-time voice**: wake word + satellite (board L2/L5) | OpenClaw #7200; HA voice [R] | STT/TTS; needs a mic | L |
| E8 | **Discord voice channel** conversation with barge-in | Hermes | voice pipeline | L |
| E9 | **A second chat channel** (Signal or Telegram) | Telegram is the most-used channel overall | new gateway ⚠ egress | M |

### F. Actions, with approval

| ID | Upgrade | Evidence | Hook | Effort |
|---|---|---|---|---|
| F1 | **Discord approve/deny buttons** for consequential actions, with "allow once / always for this" | Manus/Claude grants; muse-agent draft-first | G7 approvals, D-020 | M |
| F2 | **Draft-first outbound**: any email or message Dami composes is a draft Steve sends | [R] "drafts, never auto-send" | F1 | M |
| F3 | **Sandboxed read-only browser** for JS-rendered pages (throwaway profile, no logins) | browser use everywhere; Comet injections ⚠ | bubblewrap tier | L |
| F4 | **Form-fill / booking** behind F1 approval (reservations, check-ins) | brandon.wang; flaky frames | F3 ⚠⚠ | L |

### G. Skills and extensibility (without a marketplace)

| ID | Upgrade | Evidence | Hook | Effort |
|---|---|---|---|---|
| G1 | **SKILL.md compatibility**: read agentskills.io/ClawHub-format skills *from a local folder Steve curates*, through F3 filesystem skills | SKILL.md is the de-facto standard across Hermes, OpenClaw, Claude, Leon, Agent Zero | capability registry ⚠ vet | M |
| G2 | **Skill vetting**: static scan (network calls, shell, credential reads) before a skill or tool can be promoted | `skill-vetter`/`skillscan` are ClawHub #2 and #8 | F-registry promotion | S |
| G3 | **Self-authored skills from repetition**: a request made three times proposes a skill for Steve to promote | Hermes' most-praised loop; its failure is self-approval, which Dami's gate avoids | tool proposals (F4/F5) | M |
| G4 | **MCP health and cost per server** | Hermes MCP command center | registry | S–M |

### H. Security and privacy

| ID | Upgrade | Evidence | Hook | Effort |
|---|---|---|---|---|
| H1 | **Untrusted-input taint** generalised: anything from mail, web, invites or webhooks marks the turn, and tools with side effects are refused after it (today only research does this) | Comet calendar-invite exfiltration; Cowork injection | tool bundle | M |
| H2 | **Read vs write egress with time-limited grants** | Muse Sentinel | egress allowlist | M |
| H3 | **Secrets never reach a model**: audit that no credential can enter a prompt; inject at request time | NanoClaw/Vellum credential gateways | providers, tools | S–M |
| H4 | **Secret redaction** in traces, journald and Elasticsearch | Hermes deep redaction; OpenClaw #10659 | logging | S |
| H5 | **Weekly self-audit**: listening ports vs runbook §1, LAN proxy refuses anonymous, allowlist drift, CodeWork reach | ~21k–500k exposed claw instances | proactive service | S |
| H6 | **Second opinion on risky actions**: a separate check before any irreversible tool call | Hermes smart approval | F1 | M |
| H7 | **Planned-actions audit view**: what Dami intends to do next (jobs, pending approvals), not only what it did | Muse audit trail | traces, jobs | S |

### I. Multi-agent and models

| ID | Upgrade | Evidence | Hook | Effort |
|---|---|---|---|---|
| I1 | **Subagents with schema-validated output and cost per delegation** for research and long jobs | Hermes | frontier tool loop | M |
| I2 | **Provider fallback** when the subscription quota or Codex is down (Anthropic adapter is built and dormant) | platform-risk stories [R]; D5 deliberately not built | ADR-0011/0028 decision | M |
| I3 | CodeWork "done" notification with the diff summary on Discord | phone-remote coding [R] | ADR-0036 | S |

### J. Learning, creative, persona

| ID | Upgrade | Evidence | Hook | Effort |
|---|---|---|---|---|
| J1 | **Spaced repetition** from notes, the research library and beliefs (cat #19 is the quiz form) | nightly flashcards (HN 47784422), Anki [R] | research library | S–M |
| J2 | **Language tutor** mode with STT/TTS | [R] | voice | M |
| J3 | **Animated avatar** (board L6) | persona faces [1]×2 | Gallery | L |
| J4 | **Persona restraint metric**: track how much of an answer is persona vs content | "burns tokens anthropomorphising" | traces | S |

### K. Household (only if Dami serves more than Steve)

The family-assistant pattern is common [R], but a shared agent conflicts with the
single-user decision, and `proactive-addon-catalog.md` #16 assumes Steve lives alone. Listed
for completeness; each needs a decision first.

- K1: a separate family persona with isolated memory and its own mailbox.
- K2: a shared shopping list.
- K3: a household wall calendar.
- K4: household announcements through a speaker.

### Considered and not recommended (conflicts with a decision, or the evidence says no)

- **A skill marketplace** (onboarding §4; ClawHavoc showed what one costs).
- **Multi-user RBAC** (single-user by decision).
- **Purchases or payments by the agent** (Gemini "may purchase without asking"; no evidence of value worth the risk).
- **Auto-send of email or messages** (the iMessage-overnight and deleted-email incidents).
- **Always-on heartbeat that replays the whole session** (the OpenClaw cost complaint).
- **Inferred-interest feeds** (ChatGPT Pulse was retired for exactly this).
- **Persona "mood files" that simulate a life** ("too weird" was a common reason users stopped).

---

## 5. Suggested order

Ranked by (evidence × fit) ÷ effort, with reliability first, because that is why people quit:

1. **A1 + A6** — "Is Dami OK" page, daily line and weekly reliability report (S). Today's two defects (the stranded sidecars and the gate memo) were both silent.
2. **E1 + C6/E2** — voice notes in and a voice check-in out (S). Both sidecars already run.
3. **A5** — gate retry before withholding everything (S).
4. **D4** — receipt photo → ledger (S–M), the most repeated durable finance use.
5. **B1** — per-reply sources (S–M), Dami's trace advantage made visible.
6. **C1 + C2** — change-only gates and job memory (S–M), the lesson from every flaky briefing.
7. **D2** — Dami's own forward-to mailbox, read-only and quarantined (M). It unlocks D11, receipts by mail, school, travel and vet.
8. **D1** — calendar read (M), the single most-used integration.
9. **B6 + B4** — markdown vault export and the nightly consolidation diary (M).
10. **C3** — watch tasks (M), covering prices, pages and apartments in one mechanism.

---

## Sources

Agents:
- Hermes: [github.com/NousResearch/hermes-agent](https://github.com/NousResearch/hermes-agent), [docs](https://hermes-agent.nousresearch.com/docs/), [user stories](https://hermes-agent.nousresearch.com/docs/user-stories)
- OpenClaw: [github.com/openclaw/openclaw](https://github.com/openclaw/openclaw), [memory](https://docs.openclaw.ai/concepts/memory), [heartbeat](https://docs.openclaw.ai/gateway/heartbeat), [showcase](https://openclaw.ai/showcase)
- NanoClaw: [github.com/qwibitai/nanoclaw](https://github.com/qwibitai/nanoclaw)
- Letta Code: [github.com/letta-ai/letta-code](https://github.com/letta-ai/letta-code)
- Agent Zero: [github.com/agent0ai/agent-zero](https://github.com/agent0ai/agent-zero)
- Goose: [github.com/aaif-goose/goose](https://github.com/aaif-goose/goose)
- Vellum: [github.com/vellum-ai/vellum-assistant](https://github.com/vellum-ai/vellum-assistant)
- Home Assistant: [2026.9 changelog](https://www.home-assistant.io/changelogs/core-2026.9/)
- Meta Muse: see §1
- ChatGPT scheduled tasks: [the-decoder](https://the-decoder.com/chatgpt-keeps-creeping-toward-becoming-your-ai-personal-assistant-with-new-scheduled-task-controls/)
- Claude memory: [TechCrunch 2026-08-25](https://techcrunch.com/2026/08/25/claude-cowork-finally-remembers-what-you-told-the-app-in-chat/)
- Gemini Spark: [Decrypt](https://decrypt.co/368389/google-gemini-spark-ai-agent-challenge-hermes-openclaw)

Security:
- ClawHub malware: [The Hacker News](https://thehackernews.com/2026/02/openclaw-integrates-virustotal-scanning.html)
- Claw Chain: [CSA](https://labs.cloudsecurityalliance.org/research/csa-research-note-openclaw-claw-chain-cve-20260517-csa-style/) (*unverified* severity)
- Hermes CVEs: [SentinelOne CVE-2026-10223](https://www.sentinelone.com/vulnerability-database/cve-2026-10223/)

User reports:
- Hacker News: item ids as cited, at `news.ycombinator.com/item?id=<id>`.
- Blogs: [brandon.wang/2026/clawdbot](https://brandon.wang/2026/clawdbot), [aaronstuyvenberg.com — clawd bought a car](https://aaronstuyvenberg.com/posts/clawd-bought-a-car), MacStories.
- Reddit, by thread id: r/openclaw 1v2e5kr, 1u69jpj, 1qycudu, 1sekzv1, 1s7wfqr, 1rl9ave, 1todajf, 1ty5f4l, 1souzrc, 1sry4wm, 1ts1w9w, 1sztqiv; r/hermesagent 1w2ig8h, 1wra98z, 1whtz3n, 1wbzu62, 1wijyx0; r/homeassistant 1safuxi, 1sa0rcm; r/ClaudeAI 1t22v8r, 1uuprpu; r/selfhosted 1s9786d, 1s1i1f8; r/n8n 1nf7z8b.

ClawHub installs: `clawhub.ai/api/v1/skills?sort=installs`, read 2026-09-29.
