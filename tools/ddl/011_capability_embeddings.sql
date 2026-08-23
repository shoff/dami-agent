-- Derived semantic index over capability descriptions (D-015). Capability metadata
-- remains in the source-neutral registry; these vectors are rebuildable lookup data.

create table dami.capability_embeddings (
    capability_id      uuid not null,
    capability_version text not null check (length(capability_version) > 0),
    embedding_model    text not null check (length(embedding_model) > 0),
    embedded_at        timestamptz not null default now(),
    embedding          vector(1024) not null,
    primary key (capability_id, embedding_model)
);

comment on table dami.capability_embeddings is
    'Derived vectors over capability descriptions. Rebuildable from the runtime registry; never part of personal observation memory.';

create index capability_embeddings_hnsw
    on dami.capability_embeddings using hnsw (embedding vector_cosine_ops);

create index capability_embeddings_model
    on dami.capability_embeddings (embedding_model);

grant select, insert, update, delete on dami.capability_embeddings to dami_app;
