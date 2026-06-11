namespace Prismedia.Domain.Entities;

/// <summary>
/// The closed set of identify actions a provider lookup can run — the two-mode plugin contract.
/// <see cref="LookupId"/> and <see cref="LookupUrl"/> are the confident, deterministic lookups
/// (a provider id or URL resolves to a single high-confidence proposal); <see cref="Search"/> is
/// the confidence-scored title search that returns ranked candidates for user disambiguation.
/// </summary>
public enum IdentifyAction {
    /// <summary>Confidence-scored title/query search returning ranked candidates for disambiguation.</summary>
    [Code("search")]
    Search,

    /// <summary>Deterministic lookup by a provider-specific external id.</summary>
    [Code("lookup-id")]
    LookupId,

    /// <summary>Deterministic lookup by a provider URL.</summary>
    [Code("lookup-url")]
    LookupUrl
}
