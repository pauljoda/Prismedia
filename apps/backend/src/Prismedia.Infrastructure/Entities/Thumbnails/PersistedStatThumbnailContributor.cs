using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Entities.Thumbnails;

/// <summary>
/// Projects selected Prismedia-owned statistic rows as compact thumbnail chips. Values are read
/// directly from scan-maintained metadata; thumbnail requests never count descendants or pages.
/// </summary>
internal sealed class PersistedStatThumbnailContributor(PrismediaDbContext db) : IThumbnailContributor {
    private static readonly IReadOnlyDictionary<string, string> IconsByCode =
        new Dictionary<string, string>(StringComparer.Ordinal) {
            [EntityStatCodes.Pages] = EntityThumbnailMetaIcons.Page
        };

    /// <inheritdoc />
    public int MetaPriority => -90;

    /// <inheritdoc />
    public async Task ContributeAsync(
        ThumbnailContributions contributions,
        CancellationToken cancellationToken) {
        var entityIds = contributions.Rows.Select(row => row.Id).ToArray();
        if (entityIds.Length == 0) {
            return;
        }

        var codes = IconsByCode.Keys.ToArray();
        var stats = await db.EntityStats.AsNoTracking()
            .Where(row => entityIds.Contains(row.EntityId) && codes.Contains(row.Code) && row.Value > 0)
            .ToArrayAsync(cancellationToken);
        foreach (var stat in stats) {
            contributions.AddMeta(
                stat.EntityId,
                IconsByCode[stat.Code],
                stat.Value.ToString(CultureInfo.InvariantCulture));
        }
    }
}
