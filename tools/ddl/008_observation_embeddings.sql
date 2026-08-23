-- Observation embeddings under the interim model (ADR-0009). The model is versioned
-- per row - D-010's eval still chooses the production embedder, and re-embedding under
-- a different name is the designed migration, not an incident.

create table dami.observation_embeddings (
    observation_id  uuid primary key references dami.observations (observation_id),
    embedding_model text not null,
    embedded_at     timestamptz not null default now(),
    embedding       vector(1024) not null
);

comment on table dami.observation_embeddings is
    'Derived data: vectors over the corpus. Deletable and rebuildable at any time; the observations are the record, these are an index of meaning.';

create index observation_embeddings_hnsw
    on dami.observation_embeddings using hnsw (embedding vector_cosine_ops);

grant insert, select, delete on dami.observation_embeddings to dami_app;
