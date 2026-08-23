-- D-010 requires model changes to coexist during a rebuild. Observation identity alone
-- made every replacement-model insert conflict with the old derived vector.

alter table dami.observation_embeddings
    drop constraint observation_embeddings_pkey;

alter table dami.observation_embeddings
    add primary key (observation_id, embedding_model);

create index observation_embeddings_model
    on dami.observation_embeddings (embedding_model);
