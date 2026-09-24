using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Entities;

/// <summary>
/// API-facing non-time progress capability. The main cursor is the work's single last-used
/// position; kinds that declare consumption modalities also expose each modality's exact checkpoint.
/// </summary>
/// <param name="CurrentEntityId">Entity addressed by the main cursor.</param>
/// <param name="Unit">Unit counted by the main cursor.</param>
/// <param name="Index">Zero-based main-cursor position.</param>
/// <param name="Total">Unit total addressed by the main cursor.</param>
/// <param name="Mode">Reader layout recorded with the main cursor.</param>
/// <param name="CompletedAt">When the work was finished, if it was.</param>
/// <param name="UpdatedAt">When the main cursor last accepted a signal.</param>
/// <param name="WorkIndex">Absolute main-cursor position across the work, when it resolves to one.</param>
/// <param name="WorkTotal">Absolute unit total across the work.</param>
/// <param name="Location">Opaque locator recorded with the main cursor.</param>
/// <param name="ConsumedCount">Units consumed independently of the cursor.</param>
/// <param name="ConsumedTotal">Unit total the consumed count is measured against.</param>
/// <param name="ConsumedPercent">Consumed share (0..1).</param>
/// <param name="LastModality">Modality of the newest checkpoint.</param>
/// <param name="Checkpoints">Exact per-modality positions, one per recorded modality.</param>
[CapabilityKind("progress")]
public sealed record ProgressCapability(
    Guid? CurrentEntityId,
    ProgressUnit Unit,
    int Index,
    int Total,
    ReaderMode? Mode,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? UpdatedAt,
    int? WorkIndex = null,
    int? WorkTotal = null,
    string? Location = null,
    int ConsumedCount = 0,
    int? ConsumedTotal = null,
    double ConsumedPercent = 0,
    ConsumptionModality? LastModality = null,
    IReadOnlyList<ModalityProgress>? Checkpoints = null) : EntityCapability;
