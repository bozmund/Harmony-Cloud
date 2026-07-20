# Harmony Cloud Agent Guide

- Inspect existing code and the latest accepted plan before changing behavior.
- Never store raw Auth0 subjects, bearer tokens, credentials, local paths, visitor IDs, or Piped secrets.
- PostgreSQL is the source of truth; API replicas must remain stateless.
- Sync writes must be idempotent and preserve the immutable account event history until account deletion.
- Run `dotnet test Harmony.Cloud.slnx` before handoff.
- Do not run Git state-changing commands without explicit authorization for that exact operation.
- Save accepted plans under `plans/` and index them in `plans/index.md`.
