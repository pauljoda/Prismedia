using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Application.Integrations;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Projects saved external-library ownership and exact holding links without contacting connected services.</summary>
public sealed class EfEntityExternalLibraryProvenanceReader(PrismediaDbContext db)
    : IEntityExternalLibraryProvenanceReader {
    private static readonly JsonSerializerOptions Json = PluginProcessTransport.JsonOptions;

    private sealed record LibraryOrigin(Guid ConnectionId, string Name, string PluginId, Guid LibraryRootId, string LibraryLabel);

    /// <inheritdoc />
    public async Task<ExternalLibraryProvenanceCapability?> ReadAsync(Guid entityId, CancellationToken cancellationToken) {
        var source = await (
            from rollup in db.EntityRollups.AsNoTracking()
            join mount in db.ExternalLibraryMounts.AsNoTracking()
                on rollup.EffectiveLibraryRootId equals mount.LibraryRootId
            join root in db.LibraryRoots.AsNoTracking() on mount.LibraryRootId equals root.Id
            join connection in db.IntegrationConnections.AsNoTracking() on mount.ConnectionId equals connection.Id
            where rollup.EntityId == entityId
            select new LibraryOrigin(connection.Id, connection.Name, connection.PluginId, root.Id, root.Label))
            .SingleOrDefaultAsync(cancellationToken);

        // Requests own a library boundary before a file scan can give the Entity an effective root.
        // Episode ownership is exact: an unrequested sibling never inherits another episode's request.
        var requests = db.ManagedRequests.AsNoTracking()
            .Where(row => row.Phase != ManagedRequestPhase.Cancelled && row.Phase != ManagedRequestPhase.OwnershipReleased)
            .Where(row => row.EntityId == entityId || db.FulfillmentReservations.Any(owner =>
                owner.EntityId == entityId && owner.OwnerId == row.Id
                && owner.ConnectionId == row.ConnectionId && owner.OwnerKind == FulfillmentOwnerKind.ExternalManager
                && owner.ReleasedAt == null));
        if (source is null) {
            var hasLibrary = await db.EntityRollups.AsNoTracking()
                .AnyAsync(row => row.EntityId == entityId && row.EffectiveLibraryRootId != null, cancellationToken);
            if (hasLibrary) return null;
            source = await (
                from request in requests
                join mount in db.ExternalLibraryMounts.AsNoTracking()
                    on new { request.ConnectionId, request.LibraryRootId } equals new { mount.ConnectionId, mount.LibraryRootId }
                join root in db.LibraryRoots.AsNoTracking() on mount.LibraryRootId equals root.Id
                join connection in db.IntegrationConnections.AsNoTracking() on mount.ConnectionId equals connection.Id
                orderby request.UpdatedAt descending, request.Id
                select new LibraryOrigin(connection.Id, connection.Name, connection.PluginId, root.Id, root.Label))
                .FirstOrDefaultAsync(cancellationToken);
        }
        if (source is null) return null;

        var requestReference = await requests
            .Where(row => row.ConnectionId == source.ConnectionId && row.LibraryRootId == source.LibraryRootId)
            .OrderByDescending(row => row.UpdatedAt).ThenBy(row => row.Id)
            .Select(row => new ExternalLibraryRequestReference(row.Id, row.Phase, row.UpdatedAt, row.Problem))
            .FirstOrDefaultAsync(cancellationToken);

        var exactTarget = JsonSerializer.Serialize(new[] { new { EntityId = entityId } }, Json);
        var holding = await db.ManagedHoldings.AsNoTracking()
            .Where(row => row.ConnectionId == source.ConnectionId && row.LibraryRootId == source.LibraryRootId)
            .Where(row => db.ManagedSourceBindings.Any(binding => binding.HoldingId == row.Id && binding.EntityId == entityId)
                || EF.Functions.JsonContains(row.TargetsJson, exactTarget))
            .Select(row => new {
                row.Id,
                row.Kind,
                row.RemoteId,
                row.ItemJson,
                row.Status,
                row.LastCheckedAt,
                row.ReleasedAt,
                HasActiveBinding = db.ManagedSourceBindings.Any(binding => binding.HoldingId == row.Id && binding.EntityId == entityId),
            })
            .OrderByDescending(row => row.HasActiveBinding)
            .ThenBy(row => row.Status == ManagedTrackingStatus.Released)
            .ThenByDescending(row => row.LastCheckedAt)
            .ThenByDescending(row => row.ReleasedAt)
            .ThenBy(row => row.Id)
            .FirstOrDefaultAsync(cancellationToken);

        ExternalManagedHoldingReference? reference = null;
        if (holding is not null) {
            try {
                var item = JsonSerializer.Deserialize<ManagedItemInput>(holding.ItemJson, Json);
                if (ManagedLibraryService.IsValidInput(item)
                    && item!.EntityKind == holding.Kind
                    && string.Equals(item.RemoteId, holding.RemoteId, StringComparison.Ordinal)) {
                    reference = new(holding.Id, item, holding.Status);
                }
            } catch (JsonException) {
                // A legacy or corrupt holding must not hide mapped-library provenance or create an unpinned link.
            }
        }

        return new(
            source.ConnectionId,
            source.Name,
            source.PluginId,
            source.LibraryRootId,
            source.LibraryLabel,
            reference,
            requestReference);
    }
}
