# Cloud API quality refactor

Accepted implementation plan for bringing Harmony Cloud's API structure and operational safeguards in line with Harmony Resolver.

1. Replace runtime schema creation and ad-hoc DDL with a design-time EF Core factory and a committed initial migration. Production startup applies migrations explicitly, so API replicas remain stateless and schema changes are reviewable.
2. Split HTTP route registration out of `Program.cs` into feature endpoint modules. Keep the existing `/cloud/v1` routes, response shapes, authorization, and account isolation intact.
3. Establish clear API layers: contracts and domain validation are independent of HTTP and persistence; infrastructure contains database, identity, push, and Resolver-client concerns; services own multi-step application workflows.
4. Add readiness, structured exception handling, OpenAPI (development only), tracing and metrics only where they are supportable by the deployment configuration.
5. Improve unit coverage for pure domain rules and service behavior. Add database-backed integration coverage only together with a deterministic PostgreSQL test environment.
6. Update deployment and README guidance to use migrations and run the complete solution test suite before handoff.

The first implementation slice covers items 1 and 2 while preserving the current API behavior. Subsequent slices add observability and broader test coverage after that baseline is verified.

## Song catalog ownership amendment

Cloud persists user-owned state and references to Resolver media only. A song or track sync event is
reduced to its validated `videoId`; Cloud never retains canonical song metadata. Metadata fields
inside other user-owned payloads that carry a `videoId` are removed before event history and
snapshots are written. Playlist names, ordering, user labels, history timestamps, and settings
remain Cloud data. Existing event and snapshot payloads are redacted during the one-shot migration
workflow.
