namespace Prismedia.Domain.Entities;

/// <summary>
/// The two response shapes a plugin can return for an identify request — the wire discriminator of
/// the two-mode contract. <see cref="Proposal"/> is a confident match carrying a hydrated metadata
/// patch; <see cref="Candidates"/> is a confidence-scored search returning ranked candidates for the
/// user to disambiguate. A response that is neither (no match) carries no result.
/// </summary>
public enum IdentifyResultKind {
    /// <summary>A confident match: the response carries a hydrated metadata proposal.</summary>
    [Code("proposal")]
    Proposal,

    /// <summary>A search result: the response carries ranked candidates for user disambiguation.</summary>
    [Code("candidates")]
    Candidates
}
