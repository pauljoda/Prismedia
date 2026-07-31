namespace Prismedia.Domain.Entities;

/// <summary>
/// Immutable manual-acquisition contract owned by one Entity kind. This distinguishes browser
/// uploads from reviewed replacement because some structural acquisition units may be uploaded
/// without replacing existing owned content.
/// </summary>
public sealed record EntityManualAcquisitionPolicy {
    /// <summary>Policy for kinds that expose no manual acquisition actions.</summary>
    public static EntityManualAcquisitionPolicy None { get; } = new();

    /// <summary>Policy for kinds that accept a new browser upload but cannot replace content.</summary>
    public static EntityManualAcquisitionPolicy Upload { get; } = new(supportsUpload: true);

    /// <summary>Policy for kinds that accept both browser uploads and reviewed replacement.</summary>
    public static EntityManualAcquisitionPolicy UploadAndReplacement { get; } = new(
        supportsUpload: true,
        supportsReplacement: true);

    /// <summary>Creates one validated manual-acquisition policy.</summary>
    /// <param name="supportsUpload">Whether this kind is a concrete browser upload/import unit.</param>
    /// <param name="supportsReplacement">Whether existing owned content may be replaced after review.</param>
    public EntityManualAcquisitionPolicy(
        bool supportsUpload = false,
        bool supportsReplacement = false) {
        if (supportsReplacement && !supportsUpload) {
            throw new ArgumentException(
                "Reviewed replacement requires manual upload support.",
                nameof(supportsReplacement));
        }

        SupportsUpload = supportsUpload;
        SupportsReplacement = supportsReplacement;
    }

    /// <summary>Whether this kind is a concrete browser upload/import unit.</summary>
    public bool SupportsUpload { get; }

    /// <summary>Whether existing owned content may be replaced after review.</summary>
    public bool SupportsReplacement { get; }
}
