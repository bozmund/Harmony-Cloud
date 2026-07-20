# Harmony Cloud

Account-scoped logical backup and offline-first synchronization for Harmony Music.

Harmony Cloud stores immutable sync events and merged snapshots in PostgreSQL. It never stores audio:
verified global media belongs to Harmony Resolver and contains no user ownership data.

```powershell
dotnet test Harmony.Cloud.slnx
dotnet format Harmony.Cloud.slnx --verify-no-changes
```

The public API lives below `https://harmony-resolver.duckdns.org/cloud/v1/`. User requests use the
shared Auth0 audience. The service uses a separate M2M client carrying only `tracks:backup` when it
requests Resolver upload grants.
