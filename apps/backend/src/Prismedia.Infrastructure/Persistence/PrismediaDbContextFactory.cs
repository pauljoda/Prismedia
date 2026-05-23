using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Prismedia.Infrastructure.Database;

namespace Prismedia.Infrastructure.Persistence;

public sealed class PrismediaDbContextFactory : IDesignTimeDbContextFactory<PrismediaDbContext> {
    public PrismediaDbContext CreateDbContext(string[] args) {
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL") ??
            "Host=localhost;Port=5432;Database=prismedia;Username=prismedia;Password=prismedia";

        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseNpgsql(PostgresConnectionString.Normalize(connectionString))
            .Options;

        return new PrismediaDbContext(options);
    }
}
