using System.Text.Json;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Durable terminal state carried from exact import materialization to the graph node that publishes the
/// acquisition as imported. Quality codes use the domain codecs so retries remain independent of enum ordinals.
/// </summary>
public sealed record AcquisitionFinalizeJobPayload(
    Guid AcquisitionId,
    string OwnedSourceTier,
    string OwnedFormatTier,
    string? Message,
    string? OwnedMediaQuality = null,
    int OwnedMediaRevision = 1,
    int OwnedFormatScore = 0,
    Guid? UpgradeParentAcquisitionId = null,
    string? ReplacementBackupPath = null,
    IReadOnlyList<Guid>? TouchedAncestorIds = null) {
    /// <summary>Creates a payload from the quality values established by the import engine.</summary>
    public static AcquisitionFinalizeJobPayload Create(
        Guid acquisitionId,
        BookQualityRank ownedQuality,
        string? message,
        string? ownedMediaQuality = null,
        int ownedMediaRevision = 1,
        int ownedFormatScore = 0,
        IReadOnlyList<Guid>? touchedAncestorIds = null) =>
        new(
            acquisitionId,
            ownedQuality.Source.ToCode(),
            ownedQuality.Format.ToCode(),
            message,
            ownedMediaQuality,
            ownedMediaRevision,
            ownedFormatScore,
            TouchedAncestorIds: touchedAncestorIds);

    /// <summary>Creates the required-readiness continuation for a replacement child.</summary>
    public static AcquisitionFinalizeJobPayload CreateUpgrade(
        Guid childAcquisitionId,
        Guid parentAcquisitionId,
        string? message,
        string? replacementBackupPath = null,
        IReadOnlyList<Guid>? touchedAncestorIds = null) =>
        new(
            childAcquisitionId,
            BookSourceTier.Unknown.ToCode(),
            BookFormatTier.Unknown.ToCode(),
            message,
            UpgradeParentAcquisitionId: parentAcquisitionId,
            ReplacementBackupPath: replacementBackupPath,
            TouchedAncestorIds: touchedAncestorIds);

    /// <summary>Serializes the durable graph payload.</summary>
    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>Parses and validates the durable graph payload.</summary>
    public static AcquisitionFinalizeJobPayload Parse(string payloadJson) =>
        JsonSerializer.Deserialize<AcquisitionFinalizeJobPayload>(payloadJson)
        ?? throw new InvalidOperationException("Acquisition finalization payload is missing or invalid.");

    /// <summary>Restores the canonical domain quality value.</summary>
    public BookQualityRank OwnedQuality() =>
        new(
            CodecRegistry.Get<BookSourceTier>().Decode(OwnedSourceTier),
            CodecRegistry.Get<BookFormatTier>().Decode(OwnedFormatTier));
}
