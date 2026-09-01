namespace Prismedia.Domain.Entities;

/// <summary>
/// Immutable derived-media processing contract owned by one Entity kind. Application planners
/// supply current settings and downstream needs; the policy resolves the jobs without rebuilding
/// kind switches outside the definition.
/// </summary>
public sealed record EntityProcessingPolicy {
    /// <summary>Policy for kinds that own no generated-media processing.</summary>
    public static EntityProcessingPolicy None { get; } = new();

    private readonly IReadOnlyList<EntityFileRole> _generatedFileRoles;

    /// <summary>Creates one validated processing policy.</summary>
    public EntityProcessingPolicy(
        GeneratedAssetFamily assetFamily = GeneratedAssetFamily.None,
        JobType? probeJobType = null,
        bool probeRequiresAutomaticMetadata = false,
        JobType? fingerprintJobType = null,
        JobType? previewJobType = null,
        bool previewRequiresAutomaticGeneration = false,
        JobType? trickplayJobType = null,
        JobType? subtitleExtractionJobType = null,
        JobType? gridThumbnailJobType = null,
        IReadOnlyList<EntityFileRole>? generatedFileRoles = null) {
        if (probeRequiresAutomaticMetadata && probeJobType is null) {
            throw new ArgumentException(
                "An automatic-metadata probe gate requires a probe job.",
                nameof(probeRequiresAutomaticMetadata));
        }

        if (previewRequiresAutomaticGeneration && previewJobType is null) {
            throw new ArgumentException(
                "Automatic preview generation requires a preview job.",
                nameof(previewJobType));
        }

        if (gridThumbnailJobType is not null && assetFamily == GeneratedAssetFamily.None) {
            throw new ArgumentException(
                "A grid-thumbnail job requires a generated asset family.",
                nameof(gridThumbnailJobType));
        }

        var roles = generatedFileRoles?.ToArray() ?? [];
        if (roles.Distinct().Count() != roles.Length) {
            throw new ArgumentException(
                "A processing policy cannot repeat generated file roles.",
                nameof(generatedFileRoles));
        }

        if (assetFamily == GeneratedAssetFamily.None &&
            (previewJobType is not null || trickplayJobType is not null || generatedFileRoles is { Count: > 0 })) {
            throw new ArgumentException(
                "Generated jobs and file roles require a generated asset family.",
                nameof(assetFamily));
        }

        AssetFamily = assetFamily;
        ProbeJobType = probeJobType;
        ProbeRequiresAutomaticMetadata = probeRequiresAutomaticMetadata;
        FingerprintJobType = fingerprintJobType;
        PreviewJobType = previewJobType;
        PreviewRequiresAutomaticGeneration = previewRequiresAutomaticGeneration;
        TrickplayJobType = trickplayJobType;
        SubtitleExtractionJobType = subtitleExtractionJobType;
        GridThumbnailJobType = gridThumbnailJobType;
        _generatedFileRoles = Array.AsReadOnly(roles);
    }

    /// <summary>Required technical-probe job, when this kind has one.</summary>
    public JobType? ProbeJobType { get; }

    /// <summary>Conventional generated-asset family owned by this definition.</summary>
    public GeneratedAssetFamily AssetFamily { get; }

    /// <summary>Whether probing is enabled only with automatic metadata generation.</summary>
    public bool ProbeRequiresAutomaticMetadata { get; }

    /// <summary>Best-effort fingerprint job, when this kind supports fingerprints.</summary>
    public JobType? FingerprintJobType { get; }

    /// <summary>Best-effort preview/thumbnail/waveform job, when this kind generates one.</summary>
    public JobType? PreviewJobType { get; }

    /// <summary>Whether ordinary preview generation follows the automatic-preview setting.</summary>
    public bool PreviewRequiresAutomaticGeneration { get; }

    /// <summary>Deferred best-effort trickplay job, when this kind supports scrub previews.</summary>
    public JobType? TrickplayJobType { get; }

    /// <summary>Whether this kind supports trickplay generation.</summary>
    public bool SupportsTrickplayGeneration => TrickplayJobType is not null;

    /// <summary>Best-effort subtitle reconciliation job, when applicable.</summary>
    public JobType? SubtitleExtractionJobType { get; }

    /// <summary>Fallback grid-thumbnail job used when the preview branch is not selected.</summary>
    public JobType? GridThumbnailJobType { get; }

    /// <summary>Generated Entity-file roles invalidated before this kind is rebuilt.</summary>
    public IReadOnlyList<EntityFileRole> GeneratedFileRoles => _generatedFileRoles;

    /// <summary>Builds the complete processing plan for one entity from immutable current state.</summary>
    public EntityProcessingPlan Plan(EntityProcessingInputs inputs) {
        var probe = inputs.NeedsProbe &&
            (!ProbeRequiresAutomaticMetadata || inputs.AutomaticMetadataEnabled)
            ? ProbeJobType
            : null;
        var fingerprint = inputs.ShouldFingerprint ? FingerprintJobType : null;
        var subtitles = inputs.NeedsSubtitleExtraction || inputs.ForceSubtitleReconciliationForOwnedSource
            ? SubtitleExtractionJobType
            : null;
        var ordinaryPreview = inputs.NeedsPreview &&
            (!PreviewRequiresAutomaticGeneration || inputs.AutomaticPreviewEnabled);
        var preview = ordinaryPreview ? PreviewJobType : null;
        var trickplay = inputs.NeedsTrickplay && inputs.TrickplayEnabled ? TrickplayJobType : null;
        var gridThumbnail = preview is null && inputs.NeedsGridThumbnail ? GridThumbnailJobType : null;
        return new EntityProcessingPlan(probe, fingerprint, subtitles, preview, trickplay, gridThumbnail);
    }
}

/// <summary>Immutable settings and downstream state used to plan one entity's processing jobs.</summary>
public sealed record EntityProcessingInputs(
    bool NeedsProbe,
    bool ShouldFingerprint,
    bool NeedsSubtitleExtraction,
    bool ForceSubtitleReconciliationForOwnedSource,
    bool NeedsPreview,
    bool NeedsTrickplay,
    bool NeedsGridThumbnail,
    bool AutomaticMetadataEnabled,
    bool AutomaticPreviewEnabled,
    bool TrickplayEnabled);

/// <summary>Typed required and best-effort jobs chosen by <see cref="EntityProcessingPolicy.Plan"/>.</summary>
public sealed record EntityProcessingPlan(
    JobType? ProbeJobType,
    JobType? FingerprintJobType,
    JobType? SubtitleExtractionJobType,
    JobType? PreviewJobType,
    JobType? TrickplayJobType,
    JobType? GridThumbnailJobType);
