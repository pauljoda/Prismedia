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
        JobType? probeJobType = null,
        bool probeRequiresAutomaticMetadata = false,
        JobType? fingerprintJobType = null,
        JobType? previewJobType = null,
        bool previewRequiresAutomaticGeneration = false,
        bool supportsTrickplayGeneration = false,
        JobType? subtitleExtractionJobType = null,
        IReadOnlyList<EntityFileRole>? generatedFileRoles = null) {
        if (probeRequiresAutomaticMetadata && probeJobType is null) {
            throw new ArgumentException(
                "An automatic-metadata probe gate requires a probe job.",
                nameof(probeRequiresAutomaticMetadata));
        }

        if ((previewRequiresAutomaticGeneration || supportsTrickplayGeneration) && previewJobType is null) {
            throw new ArgumentException(
                "Preview generation gates require a preview job.",
                nameof(previewJobType));
        }

        var roles = generatedFileRoles?.ToArray() ?? [];
        if (roles.Distinct().Count() != roles.Length) {
            throw new ArgumentException(
                "A processing policy cannot repeat generated file roles.",
                nameof(generatedFileRoles));
        }

        ProbeJobType = probeJobType;
        ProbeRequiresAutomaticMetadata = probeRequiresAutomaticMetadata;
        FingerprintJobType = fingerprintJobType;
        PreviewJobType = previewJobType;
        PreviewRequiresAutomaticGeneration = previewRequiresAutomaticGeneration;
        SupportsTrickplayGeneration = supportsTrickplayGeneration;
        SubtitleExtractionJobType = subtitleExtractionJobType;
        _generatedFileRoles = Array.AsReadOnly(roles);
    }

    /// <summary>Required technical-probe job, when this kind has one.</summary>
    public JobType? ProbeJobType { get; }

    /// <summary>Whether probing is enabled only with automatic metadata generation.</summary>
    public bool ProbeRequiresAutomaticMetadata { get; }

    /// <summary>Best-effort fingerprint job, when this kind supports fingerprints.</summary>
    public JobType? FingerprintJobType { get; }

    /// <summary>Best-effort preview/thumbnail/waveform job, when this kind generates one.</summary>
    public JobType? PreviewJobType { get; }

    /// <summary>Whether ordinary preview generation follows the automatic-preview setting.</summary>
    public bool PreviewRequiresAutomaticGeneration { get; }

    /// <summary>Whether the preview job also produces enabled trickplay output.</summary>
    public bool SupportsTrickplayGeneration { get; }

    /// <summary>Best-effort subtitle reconciliation job, when applicable.</summary>
    public JobType? SubtitleExtractionJobType { get; }

    /// <summary>Generated Entity-file roles invalidated before this kind is rebuilt.</summary>
    public IReadOnlyList<EntityFileRole> GeneratedFileRoles => _generatedFileRoles;

    /// <summary>Resolves required probing from current downstream state and server settings.</summary>
    public JobType? ResolveProbe(bool needsProbe, bool automaticMetadataEnabled) =>
        needsProbe && (!ProbeRequiresAutomaticMetadata || automaticMetadataEnabled)
            ? ProbeJobType
            : null;

    /// <summary>Resolves best-effort fingerprinting after the shared fingerprint gate passes.</summary>
    public JobType? ResolveFingerprint(bool shouldFingerprint) =>
        shouldFingerprint ? FingerprintJobType : null;

    /// <summary>Resolves subtitle reconciliation for missing state or an owned source path.</summary>
    public JobType? ResolveSubtitleExtraction(bool needsExtraction, bool hasSourcePath) =>
        needsExtraction || hasSourcePath ? SubtitleExtractionJobType : null;

    /// <summary>Resolves preview generation from ordinary-preview and trickplay needs.</summary>
    public JobType? ResolvePreview(
        bool needsPreview,
        bool needsTrickplay,
        bool automaticPreviewEnabled,
        bool trickplayEnabled) {
        if (PreviewJobType is null) {
            return null;
        }

        var ordinaryPreview = needsPreview &&
            (!PreviewRequiresAutomaticGeneration || automaticPreviewEnabled);
        var trickplay = SupportsTrickplayGeneration && needsTrickplay && trickplayEnabled;
        return ordinaryPreview || trickplay ? PreviewJobType : null;
    }
}
