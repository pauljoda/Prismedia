using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Persists atomic preparation in the acquisition's native checkpoint slot under its Entity lifecycle lease.</summary>
public sealed class EfAtomicUpgradeCheckpointStore(PrismediaDbContext db) : IAtomicUpgradeCheckpointStore {
    /// <inheritdoc />
    public async Task<AtomicUpgradeCheckpoint?> GetAsync(Guid acquisitionId, CancellationToken cancellationToken) {
        var json = await db.Acquisitions.AsNoTracking().Where(row => row.Id == acquisitionId)
            .Select(row => row.ImportCheckpointJson).SingleOrDefaultAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(json) ? null : AtomicUpgradeCheckpointJson.Deserialize(json);
    }

    /// <inheritdoc />
    public async Task<AtomicUpgradeCheckpoint?> TryPrepareAsync(Guid acquisitionId, Guid claimJobId,
        AtomicUpgradePreparation preparation, CancellationToken cancellationToken) {
        var row = await db.Acquisitions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == acquisitionId, cancellationToken);
        if (row is null || row.ImportCheckpointJson is not null || row.FinalSourcePath is not null
            || row.UpgradeOfAcquisitionId != preparation.ParentAcquisitionId || row.Kind != preparation.Kind
            || row.Status is not (AcquisitionStatus.Downloaded or AcquisitionStatus.Importing)
            || row.ImportClaimJobId is { } claim && claim != claimJobId
            || !SelectedMatches(row, preparation.SelectedRelease)) return null;
        var sourceId = await SoleSourceIdAsync(preparation.ParentEntityId, preparation.Files.OwnedPath, cancellationToken);
        if (sourceId is null || !await ParentAndTransferMatchAsync(acquisitionId, preparation, cancellationToken)) return null;
        var checkpoint = new AtomicUpgradeCheckpoint(Guid.NewGuid(), claimJobId, preparation.ParentAcquisitionId,
            preparation.ParentEntityId, sourceId.Value, preparation.Kind, preparation.Files,
            preparation.TransferContentPath, preparation.TransferClientItemId, preparation.SelectedRelease);
        return await WriteAsync(row, AtomicUpgradeCheckpointJson.Serialize(checkpoint), claimJobId, cancellationToken)
            ? checkpoint : null;
    }

    /// <inheritdoc />
    public async Task<bool> TryClaimAsync(Guid acquisitionId, AtomicUpgradeCheckpoint checkpoint,
        Guid claimJobId, CancellationToken cancellationToken) {
        var row = await db.Acquisitions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == acquisitionId, cancellationToken);
        if (row is null || !CheckpointMatches(row.ImportCheckpointJson, checkpoint)
            || row.Status is not (AcquisitionStatus.Downloaded or AcquisitionStatus.Importing)
            || row.Status == AcquisitionStatus.Importing && row.ImportClaimJobId != claimJobId) return false;
        return await WriteAsync(row, AtomicUpgradeCheckpointJson.Serialize(checkpoint with { ClaimJobId = claimJobId }),
            claimJobId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsCurrentAsync(Guid acquisitionId, AtomicUpgradeCheckpoint checkpoint, CancellationToken cancellationToken) {
        var row = await db.Acquisitions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == acquisitionId, cancellationToken);
        return row is not null && row.Status == AcquisitionStatus.Importing && row.ImportClaimJobId == checkpoint.ClaimJobId
            && CheckpointMatches(row.ImportCheckpointJson, checkpoint)
            && await IdentityMatchesAsync(row, checkpoint, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TryClearAsync(Guid acquisitionId, AtomicUpgradeCheckpoint checkpoint, CancellationToken cancellationToken) {
        var row = await db.Acquisitions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == acquisitionId, cancellationToken);
        if (row is null || !CheckpointMatches(row.ImportCheckpointJson, checkpoint)
            || row.ImportClaimJobId != checkpoint.ClaimJobId || row.Status != AcquisitionStatus.Importing) return false;
        return await WriteAsync(row, null, null, cancellationToken);
    }

    private async Task<bool> IdentityMatchesAsync(AcquisitionRow row, AtomicUpgradeCheckpoint checkpoint, CancellationToken cancellationToken) =>
        row.UpgradeOfAcquisitionId == checkpoint.ParentAcquisitionId && row.Kind == checkpoint.Kind
        && SelectedMatches(row, checkpoint.SelectedRelease)
        && await SoleSourceIdAsync(checkpoint.ParentEntityId, checkpoint.Files.OwnedPath, cancellationToken) == checkpoint.SourceFileId
        && await ParentAndTransferMatchAsync(row.Id, new AtomicUpgradePreparation(checkpoint.ParentAcquisitionId,
            checkpoint.ParentEntityId, checkpoint.Kind, checkpoint.Files, checkpoint.TransferContentPath,
            checkpoint.TransferClientItemId, checkpoint.SelectedRelease), cancellationToken);

    private async Task<bool> ParentAndTransferMatchAsync(Guid childId, AtomicUpgradePreparation preparation, CancellationToken cancellationToken) {
        var parent = await db.Acquisitions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == preparation.ParentAcquisitionId, cancellationToken);
        if (parent is null || parent.EntityId != preparation.ParentEntityId || parent.Kind != preparation.Kind
            || parent.Status != AcquisitionStatus.Imported || string.IsNullOrWhiteSpace(parent.FinalSourcePath)
            || !FileSystemPathComparison.IsSameOrDescendant(parent.FinalSourcePath, preparation.Files.OwnedPath)) return false;
        var transfer = await db.DownloadTransfers.AsNoTracking().Where(row => row.AcquisitionId == childId)
            .OrderByDescending(row => row.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        return transfer is not null && transfer.Progress >= 1 && transfer.ClientItemId == preparation.TransferClientItemId
            && transfer.ContentPath is not null && FileSystemPathComparison.Equals(transfer.ContentPath, preparation.TransferContentPath);
    }

    private async Task<Guid?> SoleSourceIdAsync(Guid entityId, string path, CancellationToken cancellationToken) {
        var sources = await db.EntityFiles.AsNoTracking()
            .Where(row => row.Role == EntityFileRole.Source && (row.EntityId == entityId || row.Path == path))
            .Select(row => new { row.Id, row.EntityId, row.Path }).ToArrayAsync(cancellationToken);
        return sources.Length == 1 && sources[0].EntityId == entityId && FileSystemPathComparison.Equals(sources[0].Path, path)
            ? sources[0].Id : null;
    }

    private static bool CheckpointMatches(string? json, AtomicUpgradeCheckpoint checkpoint) =>
        json is not null && AtomicUpgradeCheckpointJson.Deserialize(json) == checkpoint;

    private static bool SelectedMatches(AcquisitionRow row, SelectedRelease selected) =>
        row.SelectedReleaseJson is { Length: > 0 } json && JsonSerializer.Deserialize<SelectedRelease>(json) == selected;

    private async Task<bool> WriteAsync(AcquisitionRow expected, string? json, Guid? claimJobId, CancellationToken cancellationToken) {
        if (!db.Database.IsRelational() || db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Atomic replacement preparation requires a committed Entity lifecycle transaction.");
        var affected = await db.Acquisitions.Where(row => row.Id == expected.Id
                && row.Status == expected.Status && row.ImportCheckpointJson == expected.ImportCheckpointJson
                && row.ImportClaimJobId == expected.ImportClaimJobId && row.SelectedReleaseJson == expected.SelectedReleaseJson
                && row.UpgradeOfAcquisitionId == expected.UpgradeOfAcquisitionId && row.FinalSourcePath == expected.FinalSourcePath)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ImportCheckpointJson, json)
                .SetProperty(row => row.ImportClaimJobId, claimJobId)
                .SetProperty(row => row.Status, AcquisitionStatus.Importing)
                .SetProperty(row => row.UpdatedAt, DateTimeOffset.UtcNow), cancellationToken);
        if (affected == 1 && db.Acquisitions.Local.FirstOrDefault(row => row.Id == expected.Id) is { } tracked)
            await db.Entry(tracked).ReloadAsync(cancellationToken);
        return affected == 1;
    }
}
