using Harmony.Cloud.Api.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Harmony.Cloud.IntegrationTests;

/// <summary>
/// Boots the real API against a throwaway Postgres. Auth0 is left unconfigured, which disables JWT
/// and lets <c>AccountIdentity</c> take the subject from the Development-only <c>X-Test-Subject</c>
/// header — the same affordance the app itself relies on for local end-to-end runs.
/// </summary>
public sealed class CloudApiFixture : IAsyncLifetime
{
    // Testcontainers 4.14 obsoleted the parameterless builder; the image is a constructor argument
    // now, so it can no longer be left implicit and silently default to a version nobody chose.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("harmony_cloud_tests")
        .WithUsername("harmony")
        .WithPassword("test-only-password")
        .Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:PostgreSql", _postgres.GetConnectionString());
            builder.UseSetting("Cloud:IdentityHmacKey", new string('k', 48));
        });

        await using var scope = Factory.Services.CreateAsyncScope();
        var contexts = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CloudDbContext>>();
        await using var db = await contexts.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public async Task<bool> IsRealtimeConnectedAsync(Guid deviceId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var contexts = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CloudDbContext>>();
        await using var db = await contexts.CreateDbContextAsync();
        return await db.Devices
            .Where(x => x.DeviceId == deviceId)
            .Select(x => x.IsRealtimeConnected)
            .SingleAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class CloudApiCollection : ICollectionFixture<CloudApiFixture>
{
    public const string Name = "cloud-api";
}
