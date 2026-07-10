using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfEntityAcquisitionStatusProjectionTests {
    [Fact]
    public async Task PostgreSqlFilterComposesTheRecursiveSubtreeAndUpgradeProjectionServerSide() {
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

        Assert.Contains("WITH RECURSIVE", sql, StringComparison.Ordinal);
        Assert.Contains("child.parent_entity_id = tree.entity_id", sql, StringComparison.Ordinal);
        Assert.Contains("child.upgrade_of_acquisition_id = direct.acquisition_id", sql, StringComparison.Ordinal);
        Assert.Contains("latest_rank = 1", sql, StringComparison.Ordinal);
        Assert.Contains("StatusCode", sql, StringComparison.Ordinal);
    }

}
