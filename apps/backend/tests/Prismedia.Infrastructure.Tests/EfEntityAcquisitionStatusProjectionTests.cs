using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfEntityAcquisitionStatusProjectionTests {
    [Fact]
    public async Task PostgreSqlFilterReadsThePersistedAvailabilitySnapshot() {
        await using var db = new PrismediaDbContext(
            new DbContextOptionsBuilder<PrismediaDbContext>()
                .UseNpgsql("Host=localhost;Database=prismedia;Username=prismedia;Password=prismedia")
                .Options);
        var projection = new EfEntityAcquisitionStatusProjection(db);

        var filtered = await projection.ApplyFilterAsync(
            db.Entities.AsNoTracking(),
            AcquisitionStatus.Downloading,
            CancellationToken.None);
        var sql = filtered.ToQueryString();

        Assert.Contains("acquisition_status_codes", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("WITH RECURSIVE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("acquisitions", sql, StringComparison.OrdinalIgnoreCase);
    }

}
