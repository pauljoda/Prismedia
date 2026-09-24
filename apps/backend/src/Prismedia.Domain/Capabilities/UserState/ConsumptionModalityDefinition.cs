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
}
