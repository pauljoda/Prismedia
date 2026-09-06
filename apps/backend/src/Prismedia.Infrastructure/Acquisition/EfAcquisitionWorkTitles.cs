using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Reads formal titles only while their captured native provider identity still belongs to the work.</summary>
internal static class EfAcquisitionWorkTitles {
    internal static async Task<IReadOnlyList<string>> ReadAsync(PrismediaDbContext db, Guid workId,
        CancellationToken cancellationToken) {
        return await db.EntityAlternativeTitles.AsNoTracking()
            .Where(title => title.EntityId == workId
                && db.EntityProviderIdentities.Any(binding => binding.EntityId == workId
                    && binding.PluginId == title.PluginId && binding.IdentityNamespace == title.IdentityNamespace
                    && binding.IdentityValue == title.IdentityValue)
                && db.EntityExternalIds.Any(identity => identity.EntityId == workId
                    && identity.Provider == title.IdentityNamespace && identity.Value == title.IdentityValue))
            .OrderBy(title => title.Title).Select(title => title.Title)
            .Take(AcquisitionWorkTitles.MaximumTitles).ToArrayAsync(cancellationToken);
    }
}
