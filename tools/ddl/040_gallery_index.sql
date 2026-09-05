-- 040 — the Gallery knows what it has.
--
-- The Gallery was a folder of PNGs with a small JSON sidecar on some of them. This is
-- its index: one row per file, the prompt and provenance the sidecar carried, and what
-- the local vision model saw in the picture — caption and tags — written by the
-- gallery-curator pass. The folder stays the record of the bytes; this is the record
-- of meaning, and like the other embedding tables it is rebuildable from the folder.
--
-- Nothing in here leaves the host: captions come from qwen2.5vl on loopback and vectors
-- from the TEI embedder on loopback.

create table dami.gallery_images (
    file_name     text primary key,
    created_at    timestamptz not null,
    source        text not null check (source in ('chat', 'discord', 'scheduled', 'proactive', 'imported', 'unknown')),
    prompt        text not null default '',
    model         text not null default '',
    is_canonical  boolean not null default false,
    caption       text,
    tags          jsonb,
    caption_model text,
    captioned_at  timestamptz,
    derived_from  text references dami.gallery_images (file_name),
    favourite     boolean not null default false,
    hidden        boolean not null default false,
    indexed_at    timestamptz not null default now()
);

comment on table dami.gallery_images is
    'Index over /home/steve/Data/dami-gallery: provenance from the sidecar, what the local vision model saw. Rebuildable from the folder.';

create index gallery_images_created on dami.gallery_images (created_at desc);
create index gallery_images_uncaptioned on dami.gallery_images (indexed_at) where caption is null and not hidden;

create table dami.gallery_image_embeddings (
    file_name       text not null references dami.gallery_images (file_name) on delete cascade,
    embedding_model text not null,
    embedded_at     timestamptz not null default now(),
    embedding       vector(1024) not null,
    primary key (file_name, embedding_model)
);

comment on table dami.gallery_image_embeddings is
    'Derived data: vectors over gallery captions. Deletable and rebuildable at any time.';

create index gallery_image_embeddings_hnsw
    on dami.gallery_image_embeddings using hnsw (embedding vector_cosine_ops);

grant select, insert, update, delete on dami.gallery_images to dami_app;
grant select, insert, update, delete on dami.gallery_image_embeddings to dami_app;
