namespace Prismedia.Domain.Entities;

public enum AcquisitionImportDecision {
    [Code("place-new")] PlaceNew,
    [Code("replace-upgrade")] ReplaceUpgrade,
    [Code("adopt-existing")] AdoptExisting,
    [Code("skip-existing")] SkipExisting,
    [Code("skip-not-upgrade")] SkipNotUpgrade,
    [Code("hold-format-change")] HoldFormatChange,
    [Code("hold-structural-conflict")] HoldStructuralConflict,
    [Code("unsupported")] Unsupported,
    [Code("ambiguous")] Ambiguous,
    /// <summary>The mapped video failed full verification and remains available for review.</summary>
    [Code("hold-verification")] HoldVerification,
}
