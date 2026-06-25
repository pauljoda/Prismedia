using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>EF-backed store projecting the default book acquisition profile into decision-engine rules.</summary>
public sealed class EfBookAcquisitionProfileStore(PrismediaDbContext db) : IBookAcquisitionProfileStore {
    public async Task<BookAcquisitionRules> GetDefaultRulesAsync(CancellationToken cancellationToken) {
        var row = await db.BookAcquisitionProfiles
            .AsNoTracking()
            .OrderByDescending(profile => profile.IsDefault)
            .ThenBy(profile => profile.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? BookAcquisitionRules.Default : ToRules(row);
    }

    private static BookAcquisitionRules ToRules(BookAcquisitionProfileRow row) =>
        new(
            row.AllowedFormats.Select(code => code.DecodeAs<BookFormat>()).ToArray(),
            row.Language,
            row.MinSeeders,
            row.MinSizeBytes,
            row.MaxSizeBytes,
            row.RequiredTerms,
            row.IgnoredTerms);
}
