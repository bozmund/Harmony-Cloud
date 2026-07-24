# Harmony Cloud

Account-scoped logical backup and offline-first synchronization for Harmony Music.

Harmony Cloud stores immutable sync events and merged snapshots in PostgreSQL. It never stores audio:
verified global media belongs to Harmony Resolver and contains no user ownership data.

```powershell
dotnet test Harmony.Cloud.slnx
dotnet format Harmony.Cloud.slnx --verify-no-changes
```

## Schema migrations

Database schema is owned by EF Core migrations. Apply it as a one-shot deployment step before
starting API replicas:

```powershell
dotnet Harmony.Cloud.Api.dll migrate
```

The migration command recognizes a complete schema created by older `EnsureCreated` deployments and
baselines it before applying later migrations. Do not run migrations concurrently from API replicas.

In Development, OpenAPI is available at `/openapi/v1.json` and Swagger at `/swagger`. Liveness,
readiness, and Prometheus metrics are available at `/health/live`, `/health/ready`, and `/metrics`.
Production ingress must keep `/metrics` private.

The public API lives below `https://harmony-resolver.duckdns.org/cloud/v1/`. User requests use the
shared Auth0 audience. The service uses a separate M2M client carrying only `tracks:backup` when it
requests Resolver upload grants.
