# ADR 0029 — The daily portrait draws on the subscription, not a key

- **Decision:** `DailyPortraitService` generates through `CodexSubscriptionImageGenerator`
  — the same provider the Host's Gallery uses — with the approved identity anchor attached,
  and writes into the Gallery directory with the Gallery's provenance sidecar. The keyed
  `OpenAiImageGenerator` is no longer registered in the proactive tier.
- **Date:** 2026-09-04
- **Status:** accepted
- **Amends:** ADR-0027 (image generation as a third door)

## Context

Steve, 2026-09-04: *"the daily cron image job is STILL not firing."*

It had never fired. The pass was ported from the three Hermes cron jobs on 2026-08-31
(ADR-0027) behind `DailyPortrait:Enabled`, off by default, and wired to
`OpenAiImageGenerator`, which needs an API key. Neither the flag nor a key was ever put in
the proactive tier's drop-in, so the service was not even registered
(`Disabled_DailyPortrait_Should_Not_Be_Scheduled`, N10). Meanwhile the Host grew a
subscription-backed generator with no key and an identity anchor (G24/G26, 2026-09-01),
and the Gallery became where portraits live. The proactive tier was still pointed at the
door nobody had a key for.

Enabling it as it stood would have produced a portrait of nobody in particular in a
directory nothing reads.

## Alternatives considered

| Option | Strengths | Weaknesses | Why not chosen |
|---|---|---|---|
| Enable it as is, add an OpenAI key | No code change | A second billed provider for the same picture the Gallery already makes for free on the subscription; and no identity anchor, so not Dami | Wrong provider, wrong picture |
| A `ScheduledJob` (G16) of kind Command calling `POST /gallery/generate` on the Host | Literally a cron job; reuses the Gallery path end to end | Couples the proactive tier to the interactive Host's uptime; the G16 jobs table is empty and its planner is a conversational interview, not an operator config | Wrong tier for unattended work |
| Move `GalleryImageGenerator` and `ImageGallery` into a shared assembly | One code path | `ImageGallery` is Host UI plumbing; the proactive tier needs one file and one sidecar, not the import/seed/list surface | More coupling than the job needs |
| Point the proactive pass at the subscription generator, attach the anchor, write the sidecar (chosen) | The tier already has every dependency the generator needs; the identity text is shared through one constant | The five-field sidecar shape is now written in two places | Smallest change that makes the picture right and visible |

## Evidence

- `dami.proactive_runs` has no row for `daily-portrait` as of 2026-09-04 19:41 CDT.
  `dami.scheduled_jobs` is empty. `/home/steve/.local/share/dami/portraits` does not
  exist. `systemctl cat dami-proactive` shows no `DailyPortrait__` or `Codex__` lines.
- `ProactiveComposition.AddDailyPortrait` registered `OpenAiImageGenerator`;
  `Dami.Host/Program.cs:155` registers `CodexSubscriptionImageGenerator`.
- `Enabled_DailyPortrait_Should_Draw_On_The_Subscription_Not_A_Key` resolves the enabled
  tier and asserts the generator type. `Should_Send_The_Identity_Anchor_When_One_Is_Configured`
  and `Should_Write_The_Gallery_Sidecar_Beside_The_Image` cover the picture and its
  provenance. `Should_Report_A_Missing_Anchor_Rather_Than_Draw_A_Stranger` stops a
  misconfigured path from spending a generation on the wrong subject.
- **Proof run, 2026-09-04 19:56 CDT**, from the staged Release binary with the four
  drop-in values in the environment: `codex exec` produced
  `/home/steve/Data/dami-gallery/dami-2026-09-04-evening.png`, a valid 1024×1536 PNG of
  2,064,638 bytes, with its sidecar. The process then died with SQLSTATE 23514:
  migration 035's `proactive_runs_cadence_known` did not include `EightHourly` — the
  trap Handoff.md and N10 had named, which N10 closed only for the disabled case.
  Migration 038 widens the check; applied to `dami-data` at 20:00 CDT. The re-run
  reported "evening portrait already exists" (idempotent per slot, no second
  generation), exited 0, and `proactive_runs` holds
  `daily-portrait | 2026-09-04 20:00:23 | Completed | EightHourly`.
  `RecordAsync_Should_Accept_Every_Cadence_The_Runtime_Can_Declare` now records one run
  per enum value against the test database so the list cannot fall behind again.

## Consequences

- Turning the pass on is now four drop-in lines and no secret: `DailyPortrait__Enabled`,
  `DailyPortrait__ReferencePath`, `DailyPortrait__OutputDirectory` (the Gallery
  directory), and `Codex__Enabled`. The runbook has the block.
- The proactive tier now spawns `codex exec` as `steve`, so it shares the subscription's
  auth in `~/.codex` with the Host. The tier's `Nice=10` applies to that subprocess.
- Three passes a day (`EightHourly`), named morning/midday/evening from the clock, as
  ADR-0027 ported them. "Daily" in the request is the Hermes job's name, not its count;
  cadence is one line to change.
- `PortraitIdentity.PROMPT` in `Dami.Contracts.Models` is now the single description of
  Dami sent with the anchor. The Gallery composer uses the same constant.
- The sidecar `{FileName, CreatedAt, Prompt, Model, IsCanonical}` is written by two
  assemblies. If `ImageGallery` changes its shape, the portrait service must follow.

## Reversal path

`DailyPortrait__Enabled=false` stops it. Re-registering `OpenAiImageGenerator` in
`AddDailyPortrait` restores the keyed door in five lines; the composition test would need
its type assertion changed.
