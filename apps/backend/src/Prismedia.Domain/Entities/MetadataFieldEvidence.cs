namespace Prismedia.Domain.Entities;

/// <summary>Current descriptive-value provenance and an independent user lock. Unknown history is never invented.</summary>
public sealed record MetadataFieldEvidence(MetadataValueOrigin Origin, string? ProviderId, DateTimeOffset? ObservedAt,
    decimal? Confidence, bool IsCleared, bool IsLocked, long Revision) {
    /// <summary>An existing untracked value begins unlocked without claiming a source.</summary>
    public static MetadataFieldEvidence Unknown { get; } = new(MetadataValueOrigin.Unknown, null, null, null, false, false, 0);
    /// <summary>Changes future enrichment permission without rewriting where the current value came from.</summary>
    public MetadataFieldEvidence WithLock(bool locked) => IsLocked == locked ? this : this with { IsLocked = locked, Revision = Revision + 1 };
    /// <summary>Records an explicit user edit, including a clear; manual values are protected until explicitly unlocked.</summary>
    public MetadataFieldEvidence WrittenByUser(bool cleared, DateTimeOffset now) =>
        new(MetadataValueOrigin.User, null, now, null, cleared, true, Revision + 1);
    /// <summary>Records accepted provider evidence only when unlocked. Provider omission must be filtered before this call.</summary>
    public MetadataFieldEvidence WrittenByProvider(string providerId, decimal? confidence, DateTimeOffset now) {
        if (IsLocked) return this;
        if (string.IsNullOrWhiteSpace(providerId) || providerId.Length > 128) throw new ArgumentException("A provider value requires its exact plugin identity.");
        return new(MetadataValueOrigin.Provider, providerId, now, confidence is >= 0 and <= 1 ? confidence : null, false, false, Revision + 1);
    }
}

/// <summary>Scalar descriptive fields with complete provenance and enrichment-lock semantics.</summary>
public static class MetadataFieldProtection {
    /// <summary>Collection sections need member-level provenance and are intentionally outside this scalar policy.</summary>
    public static IReadOnlyList<MetadataPatchField> Fields { get; } = Array.AsReadOnly(new[] {
        MetadataPatchField.Title, MetadataPatchField.Description, MetadataPatchField.Classification
    });
    /// <summary>Whether this field has complete scalar value ownership semantics.</summary>
    public static bool Supports(MetadataPatchField field) => Fields.Contains(field);
}
