# ADR 0031 — The Gallery knows what it has

- **Decision:** The Gallery gets an index (migration 040): one row per picture with its
  provenance, what the local vision model saw in it (caption and tags), and a vector over
  that. A proactive `gallery-curator` pass keeps it current. The folder stays the record of
  the bytes; the index is the record of meaning, and is rebuildable from the folder.
- **Date:** 2026-09-05
- **Status:** accepted
- **Extends:** ADR-0027/0029 (image generation), ADR-0030 (the frontier's tool bundle)

## Context

Steve, 2026-09-05: *"investigate the gallery functionality in the gui and let's make it a
5 star AI integrated super fucking cool feature. Maybe creating images, editing images,
searching images…"*

What existed: a folder of 79 pictures, 68 with a five-field JSON sidecar (prompt, model,
date), the rest imported with nothing; a GUI tab that lists them newest-first with a
generate box and an import button. No search, no edit, no favourites, no idea what is in
any picture. Meanwhile the host runs a vision model, an embedder, a reranker and pgvector
that the Gallery never touched. Everything proposed in the brainstorm — search, "similar
to this", edit-from-selected, the `gallery` tool, a daily captioned page — stands on one
thing: the Gallery knowing what each picture is. This is that thing.

## Alternatives considered

| Option | Strengths | Weaknesses | Why not chosen |
|---|---|---|---|
| Extend the sidecar JSON with caption and tags | No schema, no store | Search means reading every file; vectors do not belong in JSON beside a PNG; the Host and the proactive tier would both write the same file | The sidecar is provenance, not an index |
| Caption on save in the Host | Captions appear instantly | Puts a 5–10 s vision call inside a chat turn and a GUI click, and leaves the 79 existing files uncaptioned | Wrong tier for unattended work |
| Image embeddings (CLIP-style) rather than caption embeddings | Search by visual similarity | Needs another model resident on a full card; bge-m3 is already there and captions are searchable by the same words Steve would type | Revisit if caption search proves too coarse |
| An index in PostgreSQL, curated by a proactive pass (chosen) | The tier already exists; loopback only; idempotent, bounded, rebuildable | A third-party model never sees the pictures, so quality is qwen2.5vl's | That is the privacy design, not a cost |

## Evidence

- `/home/steve/Data/dami-gallery`: 147 files, 79 images, 68 sidecars, as of 2026-09-05.
- **Live pass from the staged binary, 2026-09-05** (`--run gallery-curator`, cap 6): 79
  indexed, 6 captioned, 6 embedded, exit 0. Captions written by qwen2.5vl on loopback,
  e.g. *"A woman stands on a balcony overlooking a cityscape at dusk, leaning on the
  railing and adjusting her hair"* with tags `balcony, cityscape, evening, outdoors,
  green top, …`. At the default cap of 40 per eight-hourly pass the backlog clears in two
  passes.
- `PostgresGalleryIndexTests` run against the live database: upsert keeps a caption,
  uncaptioned excludes described and hidden, nearest ranks by cosine and skips hidden,
  unembedded is per model. `GalleryCuratorServiceTests`: index/caption/embed, idempotent
  second pass, cap and resume, anchor marked canonical, disabled and missing folder quiet.
  `GalleryCaptionTests`: JSON and prose replies both index. `GallerySidecarTests`: source
  by naming convention.
- Full solution: 0 warnings, 0 errors, 1,671 tests in 21 assemblies.

## Consequences

- `dami.gallery_images` and `dami.gallery_image_embeddings` exist; `IGalleryIndex` in
  Contracts is the seam the GUI, the Host and the tool bundle will read. Source is
  inferred from the writers' naming conventions (`dami-YYYY-MM-DD-slot.png` is proactive,
  `dami-YYYYMMDD-HHmmss-<guid>.png` with a sidecar is chat, no sidecar is imported).
- The curator runs every eight hours, on by default, `GalleryCurator:Enabled=false` to
  stop it. Captioning is capped per pass (`MaxCaptionsPerPass`, 40) because the vision
  model shares the card. Nothing it does leaves the host.
- The `EightHourly` cadence now has two users; the 038 constraint already allows it.
- What this unlocks, in the order planned: search and filters in the GUI, a `gallery`
  tool in the bundle, "similar to this", edit-from-selected with a `derived_from` link
  (the column exists and is unused), favourites and hidden (columns exist, no UI yet).

## Reversal path

`drop table dami.gallery_image_embeddings, dami.gallery_images` loses nothing that is
not in the folder; the curator rebuilds it on its next pass. Removing the pass is one
registration in `ProactiveComposition`.
