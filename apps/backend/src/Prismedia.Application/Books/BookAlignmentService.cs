using Prismedia.Application.Entities;
using Prismedia.Contracts.Books;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media.Books;

namespace Prismedia.Application.Books;

/// <summary>
/// Projects a Book's reading/listening alignment together with the current user's exact positions
/// and the resume, switch, and combined destinations derived from them.
/// </summary>
public sealed class BookAlignmentService {
    #region Variables

    private readonly IEntityWriteRepository _entities;
    private readonly IWorkAlignmentReader _alignments;
    private readonly IEntityVisibilityChecker _visibility;

    #endregion

    #region Constructors

    /// <summary>Creates the service over its Entity, alignment, and visibility ports.</summary>
    public BookAlignmentService(
        IEntityWriteRepository entities,
        IWorkAlignmentReader alignments,
        IEntityVisibilityChecker visibility) {
        _entities = entities;
        _alignments = alignments;
        _visibility = visibility;
    }

    #endregion

    #region Actions - Projection

    /// <summary>
    /// Gets a visible work's alignment and the current user's resume destinations.
    /// </summary>
    /// <param name="workId">Identifier of the work Entity.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The projection, or <c>null</c> when the work is hidden, missing, or of a kind that declares no
    /// consumption modalities.
    /// </returns>
    public async Task<BookAlignmentResponse?> GetAsync(Guid workId, CancellationToken cancellationToken) {
        if (!await _visibility.IsVisibleAsync(workId, cancellationToken)) {
            return null;
        }

        var work = await _entities.FindShallowAsync(workId, cancellationToken);
        if (work is null || work.Definition.Engagement.Modalities.Count == 0) {
            return null;
        }

        var alignment = await _alignments.LoadAsync(workId, cancellationToken);
        return alignment is null ? null : Project(work, alignment);
    }

    private static BookAlignmentResponse Project(Entity work, WorkAlignment alignment) {
        var available = work.Definition.Engagement.Modalities
            .Where(modality => modality.AddressesByOffset ? alignment.HasAudio : alignment.HasReadableRendition)
            .ToArray();
        return new BookAlignmentResponse(
            available.Select(modality => modality.Modality).ToArray(),
            ConsumptionModalityDefinition.ReadablePositionTotal,
            alignment.Rows
                .Select(row => new BookAlignmentRow(
                    row.RowId,
                    row.Order,
                    row.MatchState,
                    row.Provenance,
                    row.Readable,
                    row.Audio))
                .ToArray(),
            alignment.Coverage,
            available.Length > 0 ? Resume(alignment, work.Progress) : null);
    }

    /// <summary>
    /// Exact positions resume only when recorded after the work's completion. Switch targets align
    /// the other modality's resumable position; the combined target anchors on the newest resumable
    /// position, or starts fresh at the first paired chapter when there is none.
    /// </summary>
    private static BookResumeProjection Resume(WorkAlignment alignment, CapabilityProgress? progress) {
        var reading = Resumable(progress, ConsumptionModality.Reading);
        var listening = Resumable(progress, ConsumptionModality.Listening);
        var resumable = progress?.Resumable;
        var readerMode = progress?.CheckpointFor(ConsumptionModality.Reading)?.Mode ?? progress?.Mode;
        return new BookResumeProjection(
            progress?.LastModality,
            progress?.CompletedAt,
            resumable is null ? null : alignment.Continue(resumable),
            reading is null ? null : alignment.ExactReading(reading),
            listening is null ? null : alignment.ExactListening(listening),
            alignment.Switch(listening, readerMode),
            alignment.Switch(reading, readerMode),
            alignment.Combined(resumable, readerMode));
    }

    private static ProgressCheckpoint? Resumable(CapabilityProgress? progress, ConsumptionModality modality) =>
        progress?.CheckpointFor(modality) is { } checkpoint && progress.IsResumable(checkpoint) ? checkpoint : null;

    #endregion
}
