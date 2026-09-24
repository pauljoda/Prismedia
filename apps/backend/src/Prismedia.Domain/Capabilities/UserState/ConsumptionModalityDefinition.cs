using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Capabilities;

/// <summary>
/// The behavior of one <see cref="ConsumptionModality"/>. The enum stays the persisted and generated
/// identity; each definition declares how its exact positions are addressed, which daily activity
/// bucket its time lands in, and which kinds may declare it, so write paths and projections ask the
/// modality instead of naming a particular one.
/// </summary>
public sealed class ConsumptionModalityDefinition {
    #region Static Variables

    /// <summary>
    /// Whole-work position total used by normalized reading cursors (EPUB fractions). This is the only
    /// home of that total; clients read it from the alignment projection.
    /// </summary>
    public const int ReadablePositionTotal = 10_000;

    /// <summary>
    /// Reading a readable rendition. Positions are an EPUB locator normalized to
    /// <see cref="ReadablePositionTotal"/> or a zero-based page index; the reader's mode and opaque
    /// locator travel with the checkpoint.
    /// </summary>
    public static readonly ConsumptionModalityDefinition Reading = new(
        ConsumptionModality.Reading,
        ConsumptionActivityKind.Reading,
        units: [ProgressUnit.Cfi, ProgressUnit.Page],
        positionUnit: ProgressUnit.Cfi,
        positionTotal: ReadablePositionTotal,
        carriesReaderState: true);

    /// <summary>
    /// Listening to playable audio. Positions are a whole-second offset inside one physical track,
    /// optionally identified by an embedded chapter marker.
    /// </summary>
    public static readonly ConsumptionModalityDefinition Listening = new(
        ConsumptionModality.Listening,
        ConsumptionActivityKind.Listening,
        units: [ProgressUnit.Second],
        offsetUnit: ProgressUnit.Second,
        requiresAudioPlaybackOwner: true);

    /// <summary>
    /// Every modality definition, one per <see cref="ConsumptionModality"/> member. The order breaks
    /// timestamp ties: an earlier modality wins when two checkpoints were recorded at the same instant.
    /// </summary>
    public static IReadOnlyList<ConsumptionModalityDefinition> All { get; } = [Reading, Listening];

    #endregion

    #region Variables

    /// <summary>Accepted progress units, in preference order.</summary>
    public IReadOnlyList<ProgressUnit> Units { get; }

    /// <summary>Persisted and generated identity of this modality.</summary>
    public ConsumptionModality Modality { get; }

    /// <summary>Daily activity bucket that time spent in this modality accumulates into.</summary>
    public ConsumptionActivityKind ActivityKind { get; }

    /// <summary>
    /// Unit whose index is a normalized whole-work position, or <see langword="null"/> when the
    /// modality has none. A checkpoint in this unit must use <see cref="PositionTotal"/> as its total.
    /// </summary>
    public ProgressUnit? PositionUnit { get; }

    /// <summary>Total required of <see cref="PositionUnit"/> checkpoints, when the modality has one.</summary>
    public int? PositionTotal { get; }

    /// <summary>
    /// Unit of a physical time offset inside the position Entity, or <see langword="null"/> when the
    /// modality addresses positions by index alone. Offset checkpoints record the exact offset and
    /// derive their index from it.
    /// </summary>
    public ProgressUnit? OffsetUnit { get; }

    /// <summary>Whether a checkpoint may carry a reader mode and opaque format locator.</summary>
    public bool CarriesReaderState { get; }

    /// <summary>Whether only kinds that own a shared-player audio queue may declare this modality.</summary>
    public bool RequiresAudioPlaybackOwner { get; }

    /// <summary>Whether positions are addressed by a physical time offset.</summary>
    public bool AddressesByOffset => OffsetUnit is not null;

    #endregion

    #region Constructors

    private ConsumptionModalityDefinition(
        ConsumptionModality modality,
        ConsumptionActivityKind activityKind,
        IReadOnlyList<ProgressUnit> units,
        ProgressUnit? positionUnit = null,
        int? positionTotal = null,
        ProgressUnit? offsetUnit = null,
        bool carriesReaderState = false,
        bool requiresAudioPlaybackOwner = false) {
        Modality = modality;
        ActivityKind = activityKind;
        Units = units;
        PositionUnit = positionUnit;
        PositionTotal = positionTotal;
        OffsetUnit = offsetUnit;
        CarriesReaderState = carriesReaderState;
        RequiresAudioPlaybackOwner = requiresAudioPlaybackOwner;
    }

    #endregion

    #region Actions - Lookup

    /// <summary>Returns the definition of a persisted modality.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined modality.</exception>
    public static ConsumptionModalityDefinition For(ConsumptionModality modality) =>
        All.FirstOrDefault(definition => definition.Modality == modality)
        ?? throw new ArgumentOutOfRangeException(nameof(modality), modality, "Unknown consumption modality.");

    /// <summary>Whether a checkpoint of this modality may be recorded in <paramref name="unit"/>.</summary>
    public bool Accepts(ProgressUnit unit) => Units.Contains(unit);

    #endregion

    #region Actions - Checkpoints

    /// <summary>
    /// Creates one validated checkpoint of this modality. Every checkpoint keeps
    /// <c>0 ≤ index ≤ total</c> in an accepted unit; a <see cref="PositionUnit"/> checkpoint uses
    /// <see cref="PositionTotal"/>; offset-addressed modalities require a finite, non-negative offset
    /// whose whole seconds are the index; reader state and markers are accepted only by the
    /// modalities that carry them.
    /// </summary>
    /// <param name="positionEntityId">Entity the position addresses (work, chapter, or track).</param>
    /// <param name="unit">Unit counted by <paramref name="index"/>.</param>
    /// <param name="index">Zero-based position within <paramref name="total"/>.</param>
    /// <param name="total">Unit total addressed by the index.</param>
    /// <param name="updatedAt">Server time at which the signal was accepted.</param>
    /// <param name="offsetSeconds">Exact time offset, for offset-addressed modalities.</param>
    /// <param name="markerId">Embedded chapter marker, for offset-addressed modalities.</param>
    /// <param name="mode">Reader layout, for reader modalities.</param>
    /// <param name="location">Opaque format locator, for reader modalities; blank becomes null.</param>
    /// <returns>The validated checkpoint.</returns>
    /// <exception cref="ArgumentException">A value violates this modality's rules.</exception>
    public ProgressCheckpoint Checkpoint(
        Guid positionEntityId,
        ProgressUnit unit,
        int index,
        int total,
        DateTimeOffset updatedAt,
        double? offsetSeconds = null,
        Guid? markerId = null,
        ReaderMode? mode = null,
        string? location = null) {
        var code = Modality.ToCode();
        if (positionEntityId == Guid.Empty) {
            throw new ArgumentException($"A {code} checkpoint requires the Entity its position addresses.", nameof(positionEntityId));
        }
        if (!Accepts(unit)) {
            throw new ArgumentException($"A {code} checkpoint cannot be recorded in '{unit.ToCode()}' units.", nameof(unit));
        }
        if (total < 0 || index < 0 || index > total) {
            throw new ArgumentException(
                $"A {code} checkpoint index must be between 0 and its total {total}; received {index}.",
                nameof(index));
        }
        if (unit == PositionUnit && total != PositionTotal) {
            throw new ArgumentException(
                $"A '{unit.ToCode()}' checkpoint must use the whole-work total {PositionTotal}; received {total}.",
                nameof(total));
        }

        if (AddressesByOffset) {
            if (offsetSeconds is not { } offset || !double.IsFinite(offset) || offset < 0 || offset >= int.MaxValue) {
                throw new ArgumentException(
                    $"A {code} checkpoint requires a finite, non-negative offset in seconds.",
                    nameof(offsetSeconds));
            }
            if (index != (int)Math.Floor(offset)) {
                throw new ArgumentException(
                    $"A {code} checkpoint index must equal the whole seconds of its offset {offset}; received {index}.",
                    nameof(index));
            }
        } else if (offsetSeconds is not null || markerId is not null) {
            throw new ArgumentException(
                $"A {code} checkpoint cannot carry a time offset or chapter marker.",
                nameof(offsetSeconds));
        }

        var normalizedLocation = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
        if (!CarriesReaderState && (mode is not null || normalizedLocation is not null)) {
            throw new ArgumentException(
                $"A {code} checkpoint cannot carry a reader mode or locator.",
                nameof(mode));
        }

        return new ProgressCheckpoint(
            Modality,
            positionEntityId,
            unit,
            index,
            total,
            offsetSeconds,
            markerId,
            mode,
            normalizedLocation,
            updatedAt);
    }

    /// <summary>
    /// Creates a validated checkpoint at an exact time offset inside <paramref name="positionEntityId"/>.
    /// The index is the offset's whole seconds and the total covers the Entity's known duration.
    /// </summary>
    /// <param name="positionEntityId">Physical Entity the offset is measured in.</param>
    /// <param name="markerId">Optional embedded chapter marker that identified the position.</param>
    /// <param name="offsetSeconds">Exact offset from the Entity's beginning.</param>
    /// <param name="durationSeconds">Known duration of the Entity, when probed.</param>
    /// <param name="updatedAt">Server time at which the signal was accepted.</param>
    /// <returns>The validated checkpoint.</returns>
    /// <exception cref="InvalidOperationException">This modality does not address positions by offset.</exception>
    /// <exception cref="ArgumentException">The offset is not finite and non-negative.</exception>
    public ProgressCheckpoint OffsetCheckpoint(
        Guid positionEntityId,
        Guid? markerId,
        double offsetSeconds,
        double? durationSeconds,
        DateTimeOffset updatedAt) {
        var unit = OffsetUnit
            ?? throw new InvalidOperationException($"The {Modality.ToCode()} modality does not address positions by time offset.");
        if (!double.IsFinite(offsetSeconds) || offsetSeconds < 0 || offsetSeconds >= int.MaxValue) {
            throw new ArgumentException(
                $"A {Modality.ToCode()} checkpoint requires a finite, non-negative offset in seconds.",
                nameof(offsetSeconds));
        }

        var index = (int)Math.Floor(offsetSeconds);
        var knownDuration = durationSeconds is { } duration && double.IsFinite(duration) && duration > 0
            ? (int)Math.Min(int.MaxValue - 1, Math.Ceiling(duration))
            : 0;
        return Checkpoint(
            positionEntityId,
            unit,
            index,
            Math.Max(index, knownDuration),
            updatedAt,
            offsetSeconds: offsetSeconds,
            markerId: markerId);
    }

    #endregion
}
