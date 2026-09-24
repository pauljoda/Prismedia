using Prismedia.Application.Playback;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Entities;

public sealed partial class EntityCapabilityService {
    #region Actions - Progress Reports

    /// <summary>
    /// Applies one progress report to the work that owns <paramref name="id"/>. Reading and legacy
    /// reports move the shared cursor and, for kinds that declare modalities, record the reading
    /// checkpoint. Listening reports record the exact track position and place the cursor through the
    /// work's alignment: the aligned readable cursor inside a paired chapter, whole-work seconds for a
    /// work without a readable rendition, and no move at all for unpaired audio. Completion and
    /// start-over apply either way.
    /// </summary>
    /// <param name="id">Requested Entity; its declared progress topology selects the owning work.</param>
    /// <param name="report">The report to apply.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Whether the report applied, and the owning work when it did.</returns>
    public async Task<EntityProgressReportResult> ReportProgressAsync(
        Guid id,
        EntityProgressReport report,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(report);
        // A heartbeat is one action even if it races another client. Keep its timestamp stable while
        // every retry reloads topology and latest-cursor state from the database.
        var occurredAt = _timeProvider.GetUtcNow();
        return await ExecuteWriteAttemptAsync(
            attemptCancellationToken => ReportProgressAttemptAsync(id, report, occurredAt, attemptCancellationToken),
            cancellationToken);
    }

    /// <summary>Reads the projected document of a work whose progress was just reported.</summary>
    public Task<EntityCard?> ReadProgressOwnerAsync(Guid ownerId, CancellationToken cancellationToken) =>
        _entityReads.GetAsync(ownerId, hideNsfw: false, cancellationToken);

    /// <summary>
    /// Updates a non-time progress cursor such as the current chapter and page for books.
    /// </summary>
    public Task<EntityCard?> UpdateProgressAsync(
        Guid id,
        Guid currentEntityId,
        ProgressUnit unit,
        int index,
        int total,
        ReaderMode? mode,
        bool? completed,
        bool reset,
        string? location,
        double? activitySeconds,
        ConsumptionActivityKind? activityKind,
        CancellationToken cancellationToken) =>
        UpdateProgressAsync(
            id,
            currentEntityId,
            unit,
            index,
            total,
            mode,
            completed,
            reset,
            location,
            activitySeconds,
            activityKind,
            utcOffsetMinutes: null,
            cancellationToken);

    /// <summary>Updates the last-active cursor and independent consumed-unit coverage.</summary>
    public async Task<EntityCard?> UpdateProgressAsync(
        Guid id,
        Guid currentEntityId,
        ProgressUnit unit,
        int index,
        int total,
        ReaderMode? mode,
        bool? completed,
        bool reset,
        string? location,
        double? activitySeconds,
        ConsumptionActivityKind? activityKind,
        int? utcOffsetMinutes,
        CancellationToken cancellationToken) {
        var result = await ReportProgressAsync(
            id,
            new EntityProgressReport(
                currentEntityId,
                unit,
                index,
                total,
                mode,
                completed,
                reset,
                location,
                activitySeconds,
                activityKind,
                utcOffsetMinutes),
            cancellationToken);
        return result.OwnerId is { } ownerId
            ? await ReadProgressOwnerAsync(ownerId, cancellationToken)
            : null;
    }

    private async Task<EntityProgressReportResult> ReportProgressAttemptAsync(
        Guid id,
        EntityProgressReport report,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) {
        // Progress ownership is derived from the requested entity only. A cursor or track is data within
        // that owner tree; it must never be allowed to redirect this mutation to another work.
        if (_visibility is not null && !await _visibility.IsVisibleAsync(id, cancellationToken)) {
            return EntityProgressReportResult.NotFound;
        }

        var owner = await _progressTopology.ResolveOwnerAsync(id, cancellationToken);
        if (owner is null ||
            _visibility is not null && !await _visibility.IsVisibleAsync(owner.OwnerId, cancellationToken)) {
            return EntityProgressReportResult.NotFound;
        }

        var entity = await _entities.FindShallowAsync(owner.OwnerId, cancellationToken);
        if (entity is null || !entity.Definition.SupportsDefaultCapability<CapabilityProgress>()) {
            return EntityProgressReportResult.NotFound;
        }

        var engagement = entity.Definition.Engagement;
        if (report.Modality is { } named && engagement.ModalityFor(named) is null) {
            return EntityProgressReportResult.Invalid(
                $"{entity.Definition.DisplayName} progress does not keep a '{named.ToCode()}' position.");
        }

        var modality = report.ModalityUnder(engagement);
        if (modality is { AddressesByOffset: true }) {
            return report.Listening is { } listening
                ? await ReportListeningAsync(entity, modality, listening, report, occurredAt, cancellationToken)
                : report.Modality is null
                    ? await ReportCursorAsync(entity, modality, report, occurredAt, cancellationToken)
                    : EntityProgressReportResult.Invalid(
                        $"A {modality.Modality.ToCode()} report requires the exact track position.");
        }
        if (report.Listening is not null) {
            return EntityProgressReportResult.Invalid(
                $"{entity.Definition.DisplayName} progress does not keep a listening position.");
        }

        return await ReportCursorAsync(entity, modality, report, occurredAt, cancellationToken);
    }

    #endregion

    #region Actions - Cursor Reports

    /// <summary>
    /// Applies a report that names the cursor itself: reading, kinds without modalities, and older
    /// clients' listening reports (which move only the cursor and record no checkpoint).
    /// </summary>
    private async Task<EntityProgressReportResult> ReportCursorAsync(
        Entity entity,
        ConsumptionModalityDefinition? modality,
        EntityProgressReport report,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) {
        if (report.Cursor() is not { } cursor) {
            return EntityProgressReportResult.Invalid(
                "A progress report without a listening position requires currentEntityId, unit, index, and total.");
        }
        if (_visibility is not null && !await _visibility.IsVisibleAsync(cursor.CurrentEntityId, cancellationToken)) {
            return EntityProgressReportResult.NotFound;
        }

        var proposedCursor = await _progressTopology.ResolveCursorAsync(entity.Id, cursor.CurrentEntityId, cancellationToken);
        if (proposedCursor is null) {
            return EntityProgressReportResult.NotFound;
        }

        var total = Math.Max(0, cursor.Total);
        var index = total == 0 ? 0 : Math.Clamp(cursor.Index, 0, total - 1);
        var workPosition = await _progressTopology.ResolveWorkPositionAsync(
            entity.Id,
            cursor.CurrentEntityId,
            index,
            total,
            cancellationToken);
        var cursorId = workPosition?.CursorId ?? proposedCursor.NormalizedCursorId;
        var location = string.IsNullOrWhiteSpace(report.Location) ? null : report.Location.Trim();

        ProgressCheckpoint? checkpoint = null;
        if (modality is { AddressesByOffset: false } && (report.Modality is not null || modality.Accepts(cursor.Unit))) {
            try {
                checkpoint = modality.Checkpoint(cursorId, cursor.Unit, index, total, occurredAt, mode: report.Mode, location: location);
            } catch (ArgumentException exception) when (report.Modality is not null) {
                return EntityProgressReportResult.Invalid(exception.Message);
            } catch (ArgumentException) {
                // Older clients that name no modality keep their cursor even when it cannot be a checkpoint.
            }
        }

        var progress = GetOrAddDefaultCapability<CapabilityProgress>(entity)!;
        var hasActivity = await AccumulateConsumptionActivityAsync(
            entity,
            report.ActivitySeconds,
            modality?.ActivityKind ?? report.ActivityKind,
            report.UtcOffsetMinutes,
            occurredAt,
            cancellationToken);
        if (MarkIncompleteOnly(report)) {
            return await MarkIncompleteAsync(entity, progress, hasActivity, occurredAt, cancellationToken);
        }

        var recorded = checkpoint is not null && progress.TryRecord(checkpoint);
        return await MoveCursorAsync(
            entity,
            progress,
            new CursorMove(cursorId, cursor.Unit, index, total, report.Mode, location, workPosition),
            report,
            recorded || hasActivity,
            occurredAt,
            cancellationToken);
    }

    /// <summary>
    /// Moves the shared cursor for an accepted report. Explicit start-over resets coverage; ordinary
    /// progress always follows the most recent accepted cursor even when it moved backward.
    /// </summary>
    private async Task<EntityProgressReportResult> MoveCursorAsync(
        Entity entity,
        CapabilityProgress progress,
        CursorMove move,
        EntityProgressReport report,
        bool stateChanged,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) {
        var consumedTotal = move.WorkPosition?.Total ?? move.Total;
        var consumedIndex = move.WorkPosition?.Index ?? move.Index;
        if (report.Reset) {
            progress.TryMarkIncomplete(occurredAt);
            if (progress.TryMoveTo(move.CursorId, move.Unit, move.Index, move.Total, move.Mode, occurredAt, move.Location, consumedCount: 0) ||
                stateChanged) {
                await SaveProgressStateAsync(entity, cancellationToken);
            }
            return EntityProgressReportResult.Applied(entity.Id);
        }

        var completed = report.Completed == true;
        var consumedCount = completed
            ? consumedTotal
            : Math.Max(progress.ConsumedCount, consumedTotal > 0 ? consumedIndex + 1 : 0);
        if (!progress.TryMoveTo(
                move.CursorId,
                move.Unit,
                move.Index,
                move.Total,
                move.Mode,
                occurredAt,
                move.Location,
                completed: completed,
                consumedCount: consumedCount)) {
            if (stateChanged) {
                await SaveProgressStateAsync(entity, cancellationToken);
            }
            return EntityProgressReportResult.Applied(entity.Id);
        }

        if (completed) {
            await StageCompletionAsync(entity, occurredAt, cancellationToken);
        }
        await SaveProgressStateAsync(entity, cancellationToken);
        return EntityProgressReportResult.Applied(entity.Id);
    }

    /// <summary>A validated cursor destination and its absolute position across the work.</summary>
    private sealed record CursorMove(
        Guid CursorId,
        ProgressUnit Unit,
        int Index,
        int Total,
        ReaderMode? Mode,
        string? Location,
        ProgressWorkPosition? WorkPosition);

    #endregion

    #region Actions - Listening Reports

    /// <summary>
    /// Records an exact listening position on every heartbeat, whether or not its chapter is paired,
    /// then places the shared cursor through the work's alignment.
    /// </summary>
    private async Task<EntityProgressReportResult> ReportListeningAsync(
        Entity entity,
        ConsumptionModalityDefinition modality,
        ListeningPositionRequest listening,
        EntityProgressReport report,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) {
        if (!double.IsFinite(listening.OffsetSeconds) || listening.OffsetSeconds < 0) {
            return EntityProgressReportResult.Invalid("A listening position requires a finite, non-negative offset in seconds.");
        }
        if (_visibility is not null && !await _visibility.IsVisibleAsync(listening.TrackEntityId, cancellationToken)) {
            return EntityProgressReportResult.NotFound;
        }

        var track = await _entities.FindShallowAsync(listening.TrackEntityId, cancellationToken);
        if (entity.Definition is not IAudioPlaybackOwnerKindDefinition audioOwner ||
            track?.Kind != audioOwner.AudioPlaybackPolicy.ItemKind ||
            track.ParentEntityId != entity.Id) {
            return EntityProgressReportResult.NotFound;
        }

        var alignment = _workAlignments is null ? null : await _workAlignments.LoadAsync(entity.Id, cancellationToken);
        // A marker re-imported since the client loaded its queue no longer exists; keep the exact
        // offset and let the alignment locate the window instead.
        var markerId = listening.MarkerId is { } requestedMarker && alignment?.HasAudioWindow(track.Id, requestedMarker) == true
            ? requestedMarker
            : (Guid?)null;
        var checkpoint = modality.OffsetCheckpoint(
            track.Id,
            markerId,
            listening.OffsetSeconds,
            track.Technical?.Duration?.TotalSeconds,
            occurredAt);
        var move = alignment?.PlaceCursor(checkpoint) is { } placement
            ? await ResolvePlacementAsync(entity, placement, cancellationToken)
            : null;

        var progress = GetOrAddDefaultCapability<CapabilityProgress>(entity)!;
        var hasActivity = await AccumulateConsumptionActivityAsync(
            entity,
            report.ActivitySeconds,
            modality.ActivityKind,
            report.UtcOffsetMinutes,
            occurredAt,
            cancellationToken);
        if (MarkIncompleteOnly(report)) {
            return await MarkIncompleteAsync(entity, progress, hasActivity, occurredAt, cancellationToken);
        }

        var recorded = progress.TryRecord(checkpoint);
        if (move is not null) {
            // The aligned cursor reopens in the reader layout of the work's reader checkpoint.
            var readerMode = progress.Checkpoints.Values
                .FirstOrDefault(candidate => candidate.Definition.CarriesReaderState)?.Mode ?? progress.Mode;
            return await MoveCursorAsync(
                entity,
                progress,
                move with { Mode = readerMode },
                report,
                recorded || hasActivity,
                occurredAt,
                cancellationToken);
        }

        // Unpaired audio leaves the cursor alone so it keeps one unit, but completion and start-over
        // still apply to the work.
        var changed = recorded || hasActivity;
        if (report.Reset) {
            changed |= progress.TryResetCoverage(occurredAt);
        } else if (report.Completed == true && progress.TryMarkCompleted(occurredAt)) {
            await StageCompletionAsync(entity, occurredAt, cancellationToken);
            changed = true;
        }
        if (changed) {
            await SaveProgressStateAsync(entity, cancellationToken);
        }
        return EntityProgressReportResult.Applied(entity.Id);
    }

    private async Task<CursorMove?> ResolvePlacementAsync(
        Entity entity,
        Domain.Media.Books.WorkCursorPlacement placement,
        CancellationToken cancellationToken) {
        var resolved = await _progressTopology.ResolveCursorAsync(entity.Id, placement.CurrentEntityId, cancellationToken);
        if (resolved is null) {
            return null;
        }

        var total = Math.Max(0, placement.Total);
        var index = total == 0 ? 0 : Math.Clamp(placement.Index, 0, total - 1);
        var workPosition = await _progressTopology.ResolveWorkPositionAsync(
            entity.Id,
            placement.CurrentEntityId,
            index,
            total,
            cancellationToken);
        return new CursorMove(
            workPosition?.CursorId ?? resolved.NormalizedCursorId,
            placement.Unit,
            index,
            total,
            null,
            null,
            workPosition);
    }

    #endregion

    #region Actions - Shared Progress State

    /// <summary>
    /// Explicit "mark unread": clears completion in place, independent of the cursor and checkpoints,
    /// so a finished work can be reopened without losing its positions. It still obeys the
    /// latest-signal timestamp so an old client cannot reopen a newer completion.
    /// </summary>
    private static bool MarkIncompleteOnly(EntityProgressReport report) =>
        !report.Reset && report.Completed == false;

    private async Task<EntityProgressReportResult> MarkIncompleteAsync(
        Entity entity,
        CapabilityProgress progress,
        bool hasActivity,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) {
        if (progress.TryMarkIncomplete(occurredAt) || hasActivity) {
            await SaveProgressStateAsync(entity, cancellationToken);
        }
        return EntityProgressReportResult.Applied(entity.Id);
    }

    private async Task StageCompletionAsync(
        Entity entity,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) {
        var consumption = GetOrAddDefaultCapability<CapabilityConsumption>(entity);
        if (consumption is null) {
            return;
        }

        consumption.RecordCompletedOccurrence(occurredAt);
        await _consumptionEvents.StageAsync(
            CompletedEvent(entity, occurredAt, positionSeconds: null, durationSeconds: null),
            cancellationToken);
    }

    private async Task<bool> AccumulateConsumptionActivityAsync(
        Entity entity,
        double? activitySeconds,
        ConsumptionActivityKind? activityKind,
        int? utcOffsetMinutes,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) {
        if (entity.Definition.Engagement.Mode == EntityEngagementMode.None ||
            BoundActivitySeconds(activitySeconds) is not { } boundedSeconds) {
            return false;
        }

        var consumption = GetOrAddDefaultCapability<CapabilityConsumption>(entity);
        if (consumption is null) {
            return false;
        }

        consumption.AccumulateActiveDuration(TimeSpan.FromSeconds(boundedSeconds), occurredAt);
        await _consumptionActivities.StageAsync(new ConsumptionActivityAppend(
            entity.Id,
            activityKind ?? entity.Definition.Engagement.DefaultActivityKind ?? ConsumptionActivityKind.Reading,
            ActivityDate(occurredAt, utcOffsetMinutes),
            boundedSeconds),
            cancellationToken);
        return true;
    }

    private Task SaveProgressStateAsync(Entity entity, CancellationToken cancellationToken) =>
        _entities.SaveMutableStateAsync(
            entity,
            new EntityMutableStateChange(
                changedCapabilityTypes: [typeof(CapabilityProgress), typeof(CapabilityConsumption)]),
            cancellationToken);

    #endregion
}
