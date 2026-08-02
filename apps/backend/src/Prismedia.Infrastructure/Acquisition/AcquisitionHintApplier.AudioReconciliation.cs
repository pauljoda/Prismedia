using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Entities;
using Prismedia.Application.Files;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Audio-track identity reconciliation owned by <see cref="AcquisitionHintApplier"/>.</summary>
public sealed partial class AcquisitionHintApplier {
    /// <inheritdoc />
    public async Task<IReadOnlyList<WantedAudioTrackReconciliation>> ReconcileExistingWantedAudioTracksAsync(
        Guid libraryRootId,
        CancellationToken cancellationToken) {
        var audioTrackCode = EntityKind.AudioTrack.ToCode();
        var candidates = await db.Entities.AsNoTracking()
            .Where(track => track.KindCode == audioTrackCode
                && !track.IsWanted
                && track.ParentEntityId != null)
            .Join(
                db.EntityFiles.AsNoTracking().Where(file => file.Role == EntityFileRole.Source),
                track => track.Id,
                file => file.EntityId,
                (track, file) => new { Track = track, SourcePath = file.Path })
            .Join(
                db.EntityLibraryRoots.AsNoTracking().Where(detail => detail.LibraryRootId == libraryRootId),
                candidate => candidate.Track.ParentEntityId,
                detail => detail.EntityId,
                (candidate, _) => new {
                    AudioLibraryId = candidate.Track.ParentEntityId!.Value,
                    candidate.SourcePath,
                    candidate.Track.Title,
                    SortOrder = candidate.Track.SortOrder ?? 0
                })
            .ToArrayAsync(cancellationToken);

        var reconciliations = new List<WantedAudioTrackReconciliation>();
        foreach (var candidate in candidates) {
            var reconciliation = await ReconcileWantedAudioTrackAsync(
                candidate.AudioLibraryId,
                candidate.SourcePath,
                candidate.Title,
                candidate.SortOrder,
                cancellationToken);
            if (reconciliation is not null) {
                reconciliations.Add(reconciliation);
            }
        }
        return reconciliations;
    }

    /// <inheritdoc />
    public async Task<WantedAudioTrackReconciliation?> ReconcileWantedAudioTrackAsync(
        Guid audioLibraryId,
        string sourcePath,
        string scannedTitle,
        int sortOrder,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(sourcePath)) {
            return null;
        }

        var normalizedTitle = AudioTrackTitleText.Normalize(scannedTitle);
        if (normalizedTitle.Length == 0) {
            return null;
        }

        var audioTrackCode = EntityKind.AudioTrack.ToCode();
        var candidates = await db.Entities.AsNoTracking()
            .Where(entity => entity.ParentEntityId == audioLibraryId
                && entity.KindCode == audioTrackCode
                && entity.IsWanted
                && !db.EntityFiles.Any(file => file.EntityId == entity.Id && file.Role == EntityFileRole.Source))
            .Select(entity => new { entity.Id, entity.Title })
            .ToArrayAsync(cancellationToken);
        var matching = candidates
            .Where(entity => AudioTrackTitleText.MatchesMetadataTitle(entity.Title, scannedTitle))
            .ToArray();
        if (matching.Length != 1) {
            return null;
        }

        var retainedId = matching[0].Id;
        WantedAudioTrackReconciliation? reconciliation = null;
        if (!await _lifecycle.ExecuteAsync(
                retainedId,
                async leaseCancellationToken => {
                    var retained = await db.Entities.FirstOrDefaultAsync(
                        entity => entity.Id == retainedId
                            && entity.ParentEntityId == audioLibraryId
                            && entity.KindCode == audioTrackCode
                            && entity.IsWanted,
                        leaseCancellationToken);
                    if (retained is null || await HasSourceFileAsync(retained.Id, leaseCancellationToken)) {
                        return;
                    }

                    var normalizedSourcePath = Normalize(sourcePath);
                    var sourceCandidates = await db.EntityFiles
                        .Where(file => file.Role == EntityFileRole.Source
                            && file.Path.Length == normalizedSourcePath.Length)
                        .ToArrayAsync(leaseCancellationToken);
                    var matchingSources = sourceCandidates.Where(file =>
                        FileSystemPathComparison.Equals(Normalize(file.Path), normalizedSourcePath)).ToArray();
                    if (matchingSources.Length > 1) {
                        return;
                    }

                    var ownedSource = matchingSources.SingleOrDefault();
                    var now = DateTimeOffset.UtcNow;
                    var needsWaveformRegeneration = false;
                    if (ownedSource is null) {
                        db.EntityFiles.Add(new EntityFileRow {
                            Id = Guid.NewGuid(),
                            EntityId = retained.Id,
                            Role = EntityFileRole.Source,
                            Path = sourcePath,
                            MimeType = ContentTypeForPath(sourcePath),
                            SizeBytes = TryGetFileSize(sourcePath),
                            CreatedAt = now,
                            UpdatedAt = now
                        });
                    } else if (ownedSource.EntityId != retained.Id) {
                        var duplicate = await db.Entities.FirstOrDefaultAsync(
                            entity => entity.Id == ownedSource.EntityId,
                            leaseCancellationToken);
                        if (duplicate is null
                            || duplicate.ParentEntityId != audioLibraryId
                            || duplicate.KindCode != audioTrackCode
                            || duplicate.IsWanted
                            || duplicate.IsOrganized
                            || AudioTrackTitleText.Normalize(duplicate.Title) != normalizedTitle) {
                            return;
                        }

                        var duplicateFiles = await db.EntityFiles
                            .Where(file => file.EntityId == duplicate.Id)
                            .ToArrayAsync(leaseCancellationToken);
                        var waveformFiles = duplicateFiles
                            .Where(file => file.Role == EntityFileRole.Waveform)
                            .ToArray();
                        if (duplicateFiles.Count(file => file.Role == EntityFileRole.Source) != 1
                            || duplicateFiles.Any(file => file.Role is not EntityFileRole.Source and not EntityFileRole.Waveform)
                            || duplicateFiles.Single(file => file.Role == EntityFileRole.Source).Id != ownedSource.Id
                            || ownedSource.Source != FileSourceKind.Scan.ToCode()
                            || waveformFiles.Any(file =>
                                file.Source != FileSourceKind.Scan.ToCode()
                                || file.Path != AssetPathService.AudioWaveformUrl(duplicate.Id))
                            || (waveformFiles.Length > 0 && await db.EntityFiles.AsNoTracking().AnyAsync(
                                file => file.EntityId == retained.Id && file.Role == EntityFileRole.Waveform,
                                leaseCancellationToken))) {
                            return;
                        }

                        var duplicateTechnical = await db.EntityTechnical.FirstOrDefaultAsync(
                            row => row.EntityId == duplicate.Id,
                            leaseCancellationToken);
                        if ((duplicateTechnical is not null && await db.EntityTechnical.AsNoTracking().AnyAsync(
                                row => row.EntityId == retained.Id,
                                leaseCancellationToken))
                            || (waveformFiles.Length > 0 && duplicateTechnical is null)) {
                            return;
                        }

                        var duplicateFingerprints = await db.EntityFileFingerprints
                            .Where(row => row.EntityId == duplicate.Id)
                            .ToArrayAsync(leaseCancellationToken);
                        var duplicateAlgorithms = duplicateFingerprints.Select(row => row.Algorithm).ToArray();
                        if (duplicateFingerprints.Any(row =>
                                row.EntityFileId is not null && row.EntityFileId != ownedSource.Id)
                            || (duplicateAlgorithms.Length > 0 && await db.EntityFileFingerprints.AsNoTracking().AnyAsync(
                                row => row.EntityId == retained.Id && duplicateAlgorithms.Contains(row.Algorithm),
                                leaseCancellationToken))) {
                            return;
                        }

                        if (await HasNonTransferableAudioContentAsync(duplicate.Id, leaseCancellationToken)) {
                            return;
                        }

                        var duplicateTarget = duplicate.Id.ToString();
                        var activeJobs = await db.JobRuns
                            .Where(job => job.TargetEntityId == duplicateTarget
                                && (job.Status == JobRunStatus.Queued || job.Status == JobRunStatus.Running))
                            .ToArrayAsync(leaseCancellationToken);
                        if (activeJobs.Any(job => job.Status == JobRunStatus.Running)) {
                            return;
                        }

                        foreach (var job in activeJobs) {
                            job.Status = JobRunStatus.Cancelled;
                            job.Message = "Superseded by wanted audio-track reconciliation.";
                            job.FinishedAt = now;
                        }

                        if (duplicateTechnical is not null) {
                            db.EntityTechnical.Remove(duplicateTechnical);
                            db.EntityTechnical.Add(CopyTechnicalTo(duplicateTechnical, retained.Id));
                        }

                        foreach (var fingerprint in duplicateFingerprints) {
                            fingerprint.EntityId = retained.Id;
                        }

                        var duplicateDetail = await db.AudioTrackDetails.FirstOrDefaultAsync(
                            row => row.EntityId == duplicate.Id,
                            leaseCancellationToken);
                        if (duplicateDetail is not null) {
                            var retainedDetail = await db.AudioTrackDetails.FirstOrDefaultAsync(
                                row => row.EntityId == retained.Id,
                                leaseCancellationToken);
                            if (retainedDetail is null) {
                                db.AudioTrackDetails.Add(new AudioTrackDetailRow {
                                    EntityId = retained.Id,
                                    EmbeddedArtist = duplicateDetail.EmbeddedArtist,
                                    EmbeddedAlbum = duplicateDetail.EmbeddedAlbum,
                                    SectionLabel = duplicateDetail.SectionLabel,
                                    SectionOrder = duplicateDetail.SectionOrder
                                });
                            } else {
                                retainedDetail.EmbeddedArtist ??= duplicateDetail.EmbeddedArtist;
                                retainedDetail.EmbeddedAlbum ??= duplicateDetail.EmbeddedAlbum;
                            }
                            db.AudioTrackDetails.Remove(duplicateDetail);
                        }

                        if (waveformFiles.Length > 0) {
                            db.EntityFiles.RemoveRange(waveformFiles);
                            needsWaveformRegeneration = true;
                        }

                        ownedSource.EntityId = retained.Id;
                        ownedSource.UpdatedAt = now;
                        db.Entities.Remove(duplicate);
                    }

                    retained.IsWanted = false;
                    retained.SortOrder ??= sortOrder;
                    retained.UpdatedAt = now;
                    await db.SaveChangesAsync(leaseCancellationToken);
                    reconciliation = new WantedAudioTrackReconciliation(
                        retained.Id,
                        needsWaveformRegeneration);
                },
                cancellationToken)) {
            throw new EntityLifecycleMutationConflictException(retainedId);
        }

        return reconciliation;
    }

    private static EntityTechnicalRow CopyTechnicalTo(EntityTechnicalRow source, Guid entityId) => new() {
        EntityId = entityId,
        DurationSeconds = source.DurationSeconds,
        Width = source.Width,
        Height = source.Height,
        FrameRate = source.FrameRate,
        BitRate = source.BitRate,
        SampleRate = source.SampleRate,
        Channels = source.Channels,
        Codec = source.Codec,
        Container = source.Container,
        Format = source.Format,
        ProbeFailedAt = source.ProbeFailedAt,
        UpdatedAt = source.UpdatedAt
    };

    private async Task<bool> HasNonTransferableAudioContentAsync(
        Guid entityId,
        CancellationToken cancellationToken) =>
        await db.EntityExternalIds.AsNoTracking().AnyAsync(row => row.EntityId == entityId, cancellationToken)
        || await db.EntityProviderIdentities.AsNoTracking().AnyAsync(row => row.EntityId == entityId, cancellationToken)
        || await db.EntityDescriptions.AsNoTracking().AnyAsync(row => row.EntityId == entityId, cancellationToken)
        || await db.EntityRelationshipLinks.AsNoTracking().AnyAsync(
            row => row.EntityId == entityId || row.TargetEntityId == entityId,
            cancellationToken)
        || await db.EntityUrls.AsNoTracking().AnyAsync(row => row.EntityId == entityId, cancellationToken)
        || await db.EntityMarkers.AsNoTracking().AnyAsync(row => row.EntityId == entityId, cancellationToken)
        || await db.EntitySubtitles.AsNoTracking().AnyAsync(row => row.EntityId == entityId, cancellationToken)
        || await db.UserEntityStates.AsNoTracking().AnyAsync(
            row => row.EntityId == entityId || row.ProgressCurrentEntityId == entityId,
            cancellationToken)
        || await db.EntityConsumptionEvents.AsNoTracking().AnyAsync(row => row.EntityId == entityId, cancellationToken)
        || await db.EntityStats.AsNoTracking().AnyAsync(row => row.EntityId == entityId, cancellationToken)
        || await db.EntityDates.AsNoTracking().AnyAsync(row => row.EntityId == entityId, cancellationToken)
        || await db.EntitySources.AsNoTracking().AnyAsync(row => row.EntityId == entityId, cancellationToken)
        || await db.EntityPositions.AsNoTracking().AnyAsync(row => row.EntityId == entityId, cancellationToken)
        || await db.EntityClassifications.AsNoTracking().AnyAsync(row => row.EntityId == entityId, cancellationToken)
        || await db.EntityLifetimes.AsNoTracking().AnyAsync(row => row.EntityId == entityId, cancellationToken)
        || await db.MediaSources.AsNoTracking().AnyAsync(row => row.EntityId == entityId, cancellationToken)
        || await db.MediaStreams.AsNoTracking().AnyAsync(row => row.EntityId == entityId, cancellationToken)
        || await db.TrickplayInfos.AsNoTracking().AnyAsync(row => row.EntityId == entityId, cancellationToken)
        || await db.IdentifyResults.AsNoTracking().AnyAsync(row => row.EntityId == entityId, cancellationToken)
        || await db.IdentifyQueueItems.AsNoTracking().AnyAsync(row => row.EntityId == entityId, cancellationToken)
        || await db.FingerprintSubmissions.AsNoTracking().AnyAsync(row => row.EntityId == entityId, cancellationToken)
        || await db.Acquisitions.AsNoTracking().AnyAsync(row => row.EntityId == entityId, cancellationToken)
        || await db.AcquisitionImportHints.AsNoTracking().AnyAsync(row => row.EntityId == entityId, cancellationToken)
        || await db.AcquisitionHistory.AsNoTracking().AnyAsync(row => row.EntityId == entityId, cancellationToken)
        || await db.Monitors.AsNoTracking().AnyAsync(row => row.EntityId == entityId, cancellationToken)
        || await db.CollectionItemDetails.AsNoTracking().AnyAsync(
            row => row.CollectionEntityId == entityId || row.ItemEntityId == entityId,
            cancellationToken)
        || await db.CollectionDetails.AsNoTracking().AnyAsync(row => row.CoverItemEntityId == entityId, cancellationToken)
        || await db.GalleryDetails.AsNoTracking().AnyAsync(row => row.CoverImageEntityId == entityId, cancellationToken)
        || await db.BookDetails.AsNoTracking().AnyAsync(row => row.CoverPageEntityId == entityId, cancellationToken)
        || await db.BookChapterDetails.AsNoTracking().AnyAsync(row => row.CoverPageEntityId == entityId, cancellationToken)
        || await db.Entities.AsNoTracking().AnyAsync(row => row.ParentEntityId == entityId, cancellationToken);
}
