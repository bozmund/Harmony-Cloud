using Microsoft.EntityFrameworkCore;
using Harmony.Cloud.Api.Domain;

namespace Harmony.Cloud.Api.Persistence;

public static class CloudSchemaMigrator
{
    private const string InitialMigrationId = "20260723220940_InitialCloudSchema";
    private const string EfCoreProductVersion = "10.0.4";

    public static async Task MigrateAsync(CloudDbContext db, CancellationToken cancellationToken = default)
    {
        // The original deployment path used EnsureCreated followed by hand-written DDL.
        // Mark only a complete instance of that exact schema as baselined so existing
        // installations can adopt migrations without replaying the initial CreateTable calls.
        await BaselineLegacySchemaAsync(db, cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
        await RedactExistingSongMetadataAsync(db, cancellationToken);
    }

    private static Task BaselineLegacySchemaAsync(CloudDbContext db, CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                "MigrationId" character varying(150) NOT NULL,
                "ProductVersion" character varying(32) NOT NULL,
                CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId"));

            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            SELECT {0}, {1}
            WHERE to_regclass('public.cloud_sync_events') IS NOT NULL
              AND to_regclass('public.cloud_snapshots') IS NOT NULL
              AND to_regclass('public.cloud_devices') IS NOT NULL
              AND to_regclass('public.cloud_playback_commands') IS NOT NULL
              AND to_regclass('public.cloud_playback_sessions') IS NOT NULL
            ON CONFLICT ("MigrationId") DO NOTHING;
            """;

        return db.Database.ExecuteSqlRawAsync(sql, [InitialMigrationId, EfCoreProductVersion], cancellationToken);
    }

    private static async Task RedactExistingSongMetadataAsync(CloudDbContext db, CancellationToken cancellationToken)
    {
        var events = await db.SyncEvents.OrderBy(item => item.Revision).ToListAsync(cancellationToken);
        foreach (var item in events)
            ReplaceWithNormalizedPayload(item.EntityType, item.EntityId, item.Payload, payload => item.Payload = payload);

        var snapshots = await db.Snapshots.ToListAsync(cancellationToken);
        foreach (var item in snapshots)
            ReplaceWithNormalizedPayload(item.EntityType, item.EntityId, item.Payload, payload => item.Payload = payload);

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void ReplaceWithNormalizedPayload(string entityType, string entityId, System.Text.Json.JsonDocument current,
        Action<System.Text.Json.JsonDocument> replace)
    {
        var normalized = SyncPayloadPolicy.Normalize(entityType, entityId, current.RootElement);
        if (string.Equals(current.RootElement.GetRawText(), normalized.RootElement.GetRawText(), StringComparison.Ordinal))
        {
            normalized.Dispose();
            return;
        }
        current.Dispose();
        replace(normalized);
    }
}
