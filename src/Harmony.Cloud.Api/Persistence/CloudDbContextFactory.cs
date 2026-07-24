using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Harmony.Cloud.Api.Persistence;

/// <summary>
/// Supplies EF Core tooling without requiring runtime configuration or secrets.
/// </summary>
public sealed class CloudDbContextFactory : IDesignTimeDbContextFactory<CloudDbContext>
{
    public CloudDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PostgreSql")
            ?? "Host=localhost;Database=harmony_cloud_design;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<CloudDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new CloudDbContext(options);
    }
}
