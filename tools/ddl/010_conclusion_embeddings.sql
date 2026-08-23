-- Belief embeddings (D-009's second half): ONLY currently-active conclusions are
-- embedded. Rows here are derived data keyed to the conclusion; retraction or
-- supersession must remove the row, or a dead belief stays semantically retrievable -
-- the exact failure D-009 exists to prevent. Enforced by trigger, not convention.

create table dami.conclusion_embeddings (
    conclusion_id   uuid primary key references dami.conclusions (conclusion_id),
    embedding_model text not null,
    embedded_at     timestamptz not null default now(),
    embedding       vector(1024) not null
);

create index conclusion_embeddings_hnsw
    on dami.conclusion_embeddings using hnsw (embedding vector_cosine_ops);

-- The tombstone-respect mechanism: a conclusion leaving the active set takes its
-- vector with it, atomically, regardless of which code path retracted it.
create or replace function dami.drop_conclusion_embedding() returns trigger
language plpgsql as $$
begin
    if new.retracted_at is not null and old.retracted_at is null then
        delete from dami.conclusion_embeddings where conclusion_id = new.conclusion_id;
    end if;
    return new;
end;
$$;

create trigger conclusions_retract_drops_embedding
    after update on dami.conclusions
    for each row execute function dami.drop_conclusion_embedding();

grant insert, select, delete on dami.conclusion_embeddings to dami_app;
