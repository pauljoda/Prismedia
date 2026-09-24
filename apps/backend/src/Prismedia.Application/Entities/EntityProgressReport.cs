using Prismedia.Contracts.Entities;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Entities;

/// <summary>
/// One progress report for a work. Reading and legacy reports carry the cursor fields; listening
/// reports carry the exact audio position and let the server place the cursor.
/// </summary>
/// <param name="CurrentEntityId">Cursor Entity (work or chapter); required unless listening.</param>
/// <param name="Unit">Cursor unit; required unless listening.</param>
/// <param name="Index">Zero-based cursor position; required unless listening.</param>
/// <param name="Total">Cursor unit total; required unless listening.</param>
/// <param name="Mode">Reader layout.</param>
/// <param name="Completed">True completes, false clears completion, null leaves it.</param>
/// <param name="Reset">Starts the posting modality, coverage, and completion over.</param>
/// <param name="Location">Opaque reader locator.</param>
/// <param name="ActivitySeconds">Active time since the previous heartbeat.</param>
/// <param name="ActivityKind">Activity bucket named by clients that report no modality.</param>
/// <param name="UtcOffsetMinutes">Client wall-clock offset for the daily bucket.</param>
/// <param name="Modality">Consumption modality that produced the report.</param>
/// <param name="Listening">Exact audio position of a listening report.</param>
public sealed record EntityProgressReport(
    Guid? CurrentEntityId = null,
    ProgressUnit? Unit = null,
    int? Index = null,
    int? Total = null,
    ReaderMode? Mode = null,
    bool? Completed = null,
    bool Reset = false,
    string? Location = null,
    double? ActivitySeconds = null,
    ConsumptionActivityKind? ActivityKind = null,
    int? UtcOffsetMinutes = null,
    ConsumptionModality? Modality = null,
    ListeningPositionRequest? Listening = null) {
    #region Actions - Interpretation

    /// <summary>
    /// Modality the report belongs to under <paramref name="engagement"/>: the named modality; for
    /// older clients that name none, the offset-addressed modality when an audio position is present,
    /// otherwise the modality whose activity bucket matches the report, falling back to the kind's
    /// default activity (reading for Books).
    /// Null for kinds without modalities or reports that match none.
    /// </summary>
    public ConsumptionModalityDefinition? ModalityUnder(EntityEngagementPolicy engagement) {
        if (Modality is { } named) {
            return engagement.ModalityFor(named);
        }
        if (Listening is not null) {
            return engagement.Modalities.FirstOrDefault(modality => modality.AddressesByOffset);
        }
        return engagement.ModalityFor(ActivityKind) ?? engagement.ModalityFor(engagement.DefaultActivityKind);
    }

    /// <summary>The cursor fields, when the report carries all of them.</summary>
    public (Guid CurrentEntityId, ProgressUnit Unit, int Index, int Total)? Cursor() =>
        CurrentEntityId is { } currentEntityId && Unit is { } unit && Index is { } index && Total is { } total
            ? (currentEntityId, unit, index, total)
            : null;

    #endregion
}
