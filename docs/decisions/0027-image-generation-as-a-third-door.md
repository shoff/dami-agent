# ADR 0027 — Image generation is a third door through the boundary

- **Decision:** Dami generates images through `IImageGenerator`, a provider seam gated
  exactly like `IFrontierChat`: allowlisted host, Egressable-only prompts, every call in
  the event stream. The Hermes daily portrait cron jobs are ported to one proactive
  service, off by default, that writes to this host and surfaces rather than delivers.
- **Date:** 2026-08-31
- **Status:** accepted
- **Extends:** ADR-0010 (the frontier gate), D-012, D-020, D-021

## Context

Steve asked how Hermes had been generating daily images of Dami, and then asked for it
built here and the jobs ported.

What Hermes actually did, found in the imported corpus rather than by reading the Mac:
Clawdbot's own scheduler held **16 jobs**, three of them `mei-morning-photo`,
`mei-midday-photo` and `mei-evening-photo`. Each shelled out to a skill —
`/opt/homebrew/lib/node_modules/clawdbot/skills/openai-image-gen/scripts/gen.py --model
gpt-image-1 --quality high --size 1024x1536 --count 1 --out-dir
/Users/steve/clawd/mei-daily-$(date +%Y-%m-%d)-morning` — and emailed the result, after
which the thread was labelled `Mei` and archived. The `mei-` names predate the rename:
the project was MAI/Mei and became Dami around 2026-03-02.

Three things about that arrangement are worth keeping and three are not.

Worth keeping: the provider (`gpt-image-1`), the shape (one portrait, three times a day,
written to a dated folder), and the fact that it ran unattended.

Not worth keeping: **the calls were invisible** — no trace, no record that money had been
spent or a prompt had left the machine; **the key rode a command line**; and the jobs
**died silently**. All three were failing when found — `mei-evening-photo` on 429 rate
limits, `mei-midday-photo` because Azure OpenAI was not configured, and the local Stable
Diffusion ("Juggernaut") fallback on the Mac mini never finished initialising. A probe of
192.168.4.23 on 7860, 7861, 8188 and 5000 on 2026-08-31 found nothing listening.

## Alternatives considered

| Option | Strengths | Weaknesses | Why not chosen |
|---|---|---|---|
| Leave it on the Mac | Zero work; Hermes is the rollback | It has been broken for months and is invisible when it breaks. Cutover has to claim this eventually | Porting is the default (D-002) |
| Local diffusion weights | No egress, no bill, no key | Competes for a 16 GiB VRAM budget already holding TTS, embedder, reranker, vision and the sidecar (onboarding §7). The Mac's own attempt never came up | The constraint is real and unresolved; revisit if VRAM frees up |
| Reuse `IEgressClient` | One door to audit | `EgressRequest` is bodyless by design (ADR-0024) and a prompt is all body. Widening it would hand every fetch-capable service a payload channel | The same reasoning that produced `IFrontierChat` |
| Reuse `IFrontierChat` | Existing gate, no new seam | It returns prose. Bytes are a different contract, and pretending otherwise would mean base64 down a string channel | Honest typing is cheaper than the shortcut |
| A third seam, gated identically (chosen) | The boundary looks the same whichever door is used; cost and refusals are recorded | One more thing to audit, and it is the only door with a bill attached | — |

## Evidence

- Corpus, verbatim: the `gen.py --model gpt-image-1 --quality high --size 1024x1536`
  invocation, in both `-morning` and `-evening` forms.
- Corpus: "`mei-evening-photo` is failing due to 429 rate limit errors";
  "`mei-midday-photo` is failing because Azure OpenAI is not configured".
- Corpus, 2026-03-05: the local SD backend "was not finished loading"; `/models` timed out.
- Corpus, 2026-03-02: "The project was previously called MAI/Mei and has been renamed to
  Dami."
- Installed models on this host: `qwen2.5vl:7b`, `qwen3:8b`. Vision input and text; no
  generator.

## Consequences

Dami can make images, and the daily portraits can run here instead of on the machine
being retired.

**The cost, stated plainly.** This is the first capability that spends money per call, and
the first scheduled one. Three passes a day against a metered API is a standing bill, so:

- The service is **off by default** (`DailyPortrait:Enabled`). A capability with an
  invoice attached should be switched on deliberately, not inherited by deploying.
- It is **idempotent per slot** — the file for a slot is checked before the call, so a
  restart or a double pass inside the window cannot buy the same picture twice.
- The provider host must be allowlisted like any other, and an absent key means the
  capability is **absent rather than failing**: no retries, no error spam.
- Every call, allowed or refused, appends `EgressRequested` and then `EgressCompleted` or
  `EgressRefused`, carrying the purpose line and **never the prompt**. What Hermes did
  invisibly is now on the ledger.

**The prompt is configuration, not code.** The default is a plain portrait. What Steve
wants drawn is his to write in his own drop-in beside the key, and this repository holds
neither.

**It surfaces; it does not deliver.** The Hermes jobs emailed. Here the image lands on
this host and a `Surfacing` says it exists, because whether anything pushes outward is
ADR-0014 — proposed, unsigned, and sitting in Steve's queue. Wiring a push now would
decide that question by implementation, which is exactly what a decision record is for.
Discord could carry these the day ADR-0014 is accepted; the outbound attachment path
built under ADR-0026 already exists.

**A new cadence.** `ProactiveCadence.EightHourly` gives three passes a day. The scheduler
is interval-based and has no notion of clock time, so the service reads the slot from the
clock when it runs — the label then says when the pass *happened*, not when it was
supposed to. Codex is independently building a `CronSchedule` in `Dami.Core.Scheduling`;
if that lands, this cadence is the obvious first thing to replace with it.

## Reversal path

`DailyPortrait:Enabled=false` stops the spending immediately and is the default.
Removing `api.openai.com` from the egress allowlist stops it at the boundary instead, and
the refusal is recorded rather than silent. The seam and the service can be deleted
without touching anything else: nothing but the portrait service consumes
`IImageGenerator`.
