namespace Prismedia.Domain.Entities;

/// <summary>
/// Prismedia-owned statistic codes. Provider statistics remain an open vocabulary, while these
/// values are maintained by application workflows and must not be overwritten by metadata imports.
/// </summary>
public static class EntityStatCodes {
    /// <summary>Total readable pages owned by an installment/chapter or rolled up from descendants.</summary>
    public const string Pages = "pages";
}
