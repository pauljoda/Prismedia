using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Plugins;

public sealed partial class EntityMetadataApplyService {
    private async Task ReplaceProviderAlternativeTitlesAsync(EntityRow entity, EntityMetadataProposal proposal,
        DateTimeOffset now, CancellationToken cancellationToken) {
        // Formal work names cannot promote a season title into a global series alias. Only an exact
        // accepted root identity may author this evidence; incidental cross-provider IDs cannot.
        if (entity.KindCode != EntityKind.VideoSeries.ToCode() && entity.KindCode != EntityKind.Movie.ToCode()) return;
        if (proposal.Patch.AlternativeTitles is not { } alternatives) return;
        var trackedBinding = _db.ChangeTracker.Entries<EntityProviderIdentityRow>()
            .FirstOrDefault(entry => entry.Entity.EntityId == entity.Id);
        var binding = trackedBinding is not null
            ? trackedBinding.State == EntityState.Deleted ? null : trackedBinding.Entity
            : await _db.EntityProviderIdentities.AsNoTracking().SingleOrDefaultAsync(row => row.EntityId == entity.Id, cancellationToken);
        if (binding is null || !string.Equals(binding.PluginId, proposal.Provider, StringComparison.OrdinalIgnoreCase)
            || !proposal.Patch.ExternalIds.TryGetValue(binding.IdentityNamespace, out var nativeId)
            || nativeId != binding.IdentityValue) return;
        var trackedIdentity = _db.ChangeTracker.Entries<EntityExternalIdRow>()
            .FirstOrDefault(entry => entry.Entity.EntityId == entity.Id
                && entry.Entity.Provider == binding.IdentityNamespace && entry.Entity.Value == binding.IdentityValue);
        var identityOwned = trackedIdentity is not null
            ? trackedIdentity.State != EntityState.Deleted
            : await _db.EntityExternalIds.AnyAsync(row => row.EntityId == entity.Id
                && row.Provider == binding.IdentityNamespace && row.Value == binding.IdentityValue, cancellationToken);
        if (!identityOwned) return;

        var titles = AcquisitionWorkTitles.Normalize(alternatives.Prepend(proposal.Patch.Title));
        var existing = await _db.EntityAlternativeTitles.Where(row => row.EntityId == entity.Id).ToArrayAsync(cancellationToken);
        _db.EntityAlternativeTitles.RemoveRange(existing.Where(row => !titles.Contains(row.Title)));
        foreach (var title in titles) {
            var row = existing.FirstOrDefault(row => row.Title == title);
            if (row is null) {
                row = new EntityAlternativeTitleRow { EntityId = entity.Id, Title = title };
                _db.EntityAlternativeTitles.Add(row);
            }
            row.PluginId = binding.PluginId;
            row.IdentityNamespace = binding.IdentityNamespace;
            row.IdentityValue = binding.IdentityValue;
            row.UpdatedAt = now;
        }
    }
}
