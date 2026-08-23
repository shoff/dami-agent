-- A schema for integration tests to build and tear down in.
--
-- Stores qualify their tables from PostgresOptions.SchemaName rather than hardcoding
-- "dami" (standards §10), and this is what that parameterisation buys: tests exercise the
-- real DDL against a throwaway schema instead of the live one, and a test that forgets to
-- clean up cannot damage the event store.
create schema if not exists dami_test authorization dami_ddl;

grant usage on schema dami_test to dami_app;
