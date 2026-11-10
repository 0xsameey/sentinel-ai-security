using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sentinel.Infrastructure.Persistence;

// Scaffolding never loads production credentials or starts reconciliation.
// Applying migrations remains an explicit operator action.
public sealed class SentinelDesignTimeFactory : IDesignTimeDbContextFactory<SentinelDbContext>
{
    public SentinelDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<SentinelDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("SENTINEL_MIGRATION_CONNECTION") ??
            "Host=127.0.0.1;Database=sentinel_design_only;Username=unused").Options);
}
