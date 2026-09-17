using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Prismedia.Application.Entities;
using Prismedia.Application.Files;
using Prismedia.Application.Integrations;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Retains target and source foreign keys independently of transient jobs and serializes their reconciliation with scans.</summary>
public sealed partial class EfManagedTrackingStore(PrismediaDbContext db, IExternalLibraryMountStore mounts,
    IJobQueueService queue, IVideoScanPersistence videos, IEntityLifecycleMutationLease lifecycle) : IManagedTrackingStore {
    private static readonly JsonSerializerOptions Json = PluginProcessTransport.JsonOptions;

    /// <inheritdoc />
    public async Task<ManagedTrackingWork?> FindAsync(Guid id, CancellationToken token) {
        var row = await db.ManagedHoldings.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token);
        return row is null ? null : new(await MapAsync(row, token), JsonSerializer.Deserialize<ManagedBindingSelection[]>(row.SelectionsJson, Json)!);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ManagedTrackingResponse>> ListAsync(Guid connectionId, CancellationToken token) {
        var rows = await db.ManagedHoldings.AsNoTracking().Where(row => row.ConnectionId == connectionId).OrderBy(row => row.Title).Take(1000).ToArrayAsync(token);
        var results = new List<ManagedTrackingResponse>();
        foreach (var row in rows) results.Add(await MapAsync(row, token));
        return results;
    }

    /// <inheritdoc />
    public async Task<ManagedTrackingResponse> CreateAsync(Guid connectionId, TrackManagedHoldingRequest request, string title, CancellationToken token) {
        var item = request.Item with { ExpectedExternalIds = request.Item.ExpectedExternalIds.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary() };
        var itemJson = JsonSerializer.Serialize(item, Json);
        var selections = request.Selections.OrderBy(selection => selection.RemoteTargetId, StringComparer.Ordinal).ToArray();
        if (await FindAsync(request.OperationId, token) is { } existing) {
            if (existing.Tracking.ConnectionId != connectionId || existing.Tracking.LibraryRootId != request.LibraryRootId
                || JsonSerializer.Serialize(existing.Tracking.Item with { ExpectedExternalIds = existing.Tracking.Item.ExpectedExternalIds.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary() }, Json) != itemJson
                || !existing.Selections.SequenceEqual(selections)) throw new ArgumentException("This operation ID already accepted different associations.");
            return existing.Tracking;
        }
        if (!await db.ExternalLibraryMounts.AnyAsync(mount => mount.ConnectionId == connectionId && mount.LibraryRootId == request.LibraryRootId, token))
            throw new ArgumentException("Choose a library mapped to this connection.");
        if (item.EntityKind is not (EntityKind.Movie or EntityKind.VideoSeries)) throw new ArgumentException("Tracking currently supports movie and television holdings.");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var row = new ManagedHoldingRow { Id = request.OperationId, ConnectionId = connectionId, LibraryRootId = request.LibraryRootId,
            Kind = item.EntityKind, RemoteId = item.RemoteId, Title = title, ItemJson = itemJson,
            SelectionsJson = JsonSerializer.Serialize(selections, Json), Revision = 1, Status = ManagedTrackingStatus.Pending, NextCheckAt = DateTimeOffset.UtcNow };
        try {
            if (!await lifecycle.ExecuteManyAsync(selections.Select(selection => selection.EntityId).ToArray(), async leaseToken => {
                db.ManagedHoldings.Add(row);
                foreach (var selection in selections)
                    await new EfFulfillmentReservationStore(db).ReserveAsync(row.Id, FulfillmentOwnerKind.ConnectedLibrary,
                        connectionId, selection.EntityId, null, leaseToken);
                await db.SaveChangesAsync(leaseToken);
                await PublishAsync(row, leaseToken);
            }, token)) throw new ArgumentException("The selected items are changing. Refresh their associations before linking.");
            await transaction.CommitAsync(token);
        } catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) {
            await transaction.RollbackAsync(token); db.ChangeTracker.Clear();
            if (await FindAsync(request.OperationId, token) is { } accepted)
                return await CreateAsync(connectionId, request, accepted.Tracking.Title, token);
            throw new ArgumentException("This holding or one of its local sources is already tracked. Refresh the existing association.");
        } catch (Exception error) when (FulfillmentOwnershipViolation.IsConflict(error)) {
            await transaction.RollbackAsync(token); db.ChangeTracker.Clear();
            throw new FulfillmentOwnershipConflictException(error);
        }
        return await MapAsync(row, token);
    }

    /// <inheritdoc />
    public async Task QueueAsync(Guid connectionId, Guid id, CancellationToken token) {
        var row = await db.ManagedHoldings.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id && row.ConnectionId == connectionId, token)
            ?? throw new ArgumentException("This connection does not own the tracked holding.");
        await PublishAsync(row, token);
    }

    /// <inheritdoc />
    public async Task QueueDueAsync(CancellationToken token) {
        var now = DateTimeOffset.UtcNow;
        var due = await db.ManagedHoldings.Where(row => row.NextCheckAt <= now && row.Status != ManagedTrackingStatus.NeedsReview && row.Status != ManagedTrackingStatus.WaitingForFiles)
            .Join(db.IntegrationConnections.Where(connection => connection.Enabled), row => row.ConnectionId, connection => connection.Id, (row, _) => row)
            .OrderBy(row => row.NextCheckAt).Take(25).ToArrayAsync(token);
        foreach (var row in due) {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            await PublishAsync(row, token);
            row.NextCheckAt = now.AddMinutes(5);
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
    }

    private async Task PublishAsync(ManagedHoldingRow row, CancellationToken token) {
        await queue.DeclareResourceAsync(JobResourceKeys.LibraryScan, 1, TimeSpan.Zero, token);
        if (!await queue.HasPendingAsync(JobType.ManagedLibraryReconcile, row.Id.ToString(), token))
            await queue.EnqueueAsync(new EnqueueJobRequest(JobType.ManagedLibraryReconcile, TargetEntityKind: JobTargetKinds.ManagedHolding,
                TargetEntityId: row.Id.ToString(), TargetLabel: row.Title, ResourceKey: JobResourceKeys.LibraryScan), token);
    }

    private async Task<ManagedTrackingResponse> MapAsync(ManagedHoldingRow row, CancellationToken token) {
        var saved = await db.ManagedSourceBindings.AsNoTracking().Where(binding => binding.HoldingId == row.Id).ToArrayAsync(token);
        var files = saved.GroupBy(binding => binding.RemoteFileId, StringComparer.Ordinal).Select(group => {
            var file = group.First();
            return new ManagedFileBinding(file.RemoteFileId, file.LocalPath, file.SizeBytes, file.WrittenAt, file.IsAvailable,
                group.Select(binding => new ManagedEntityBinding(new(binding.RemoteTargetId, binding.Kind, binding.SeasonNumber,
                    binding.EpisodeNumber, binding.AbsoluteNumber), binding.EntityId, binding.SourceFileId)).ToArray());
        }).ToArray();
        return new(row.Id, row.ConnectionId, row.LibraryRootId, JsonSerializer.Deserialize<ManagedItemInput>(row.ItemJson, Json)!,
            row.Title, row.Status, row.Revision, row.LastCheckedAt, row.Problem, files,
            JsonSerializer.Deserialize<ManagedTargetBinding[]>(row.TargetsJson, Json)!);
    }
}
