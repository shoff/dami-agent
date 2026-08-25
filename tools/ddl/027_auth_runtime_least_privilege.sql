-- G5a1: the runtime manages identity/OIDC state but never migrations. Migration 026
-- initially granted DML across the isolated schema, including EF's bookkeeping table.

revoke all privileges on dami_auth."__EFMigrationsHistory" from dami_app;
