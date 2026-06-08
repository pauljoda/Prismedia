using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Plugins;

/// <summary>
/// Candidate image returned by a plugin for user review.
/// </summary>
public sealed record ImageCandidate(
    string Kind,
    string Url,
    string Source,
    decimal? Rank,
    string? Language,
    int? Width,
    int? Height);

/// <summary>
/// Search candidate returned when a plugin needs user disambiguation.
/// </summary>
/// <param name="Confidence">Optional 0-1 provider score for title-search matches.</param>
/// <param name="MatchReason">Provider reason for the candidate score, such as title-search.</param>
public sealed record EntitySearchCandidate(
    IReadOnlyDictionary<string, string> ExternalIds,
    string Title,
    int? Year,
    string? Overview,
    string? PosterUrl,
    decimal? Popularity,
    string? CandidateId = null,
    string? Source = null,
    decimal? Confidence = null,
    string? MatchReason = null);

/// <summary>
/// Capability-aligned metadata patch proposed by a plugin.
/// </summary>
public sealed record EntityMetadataPatch(
    string? Title,
    string? Description,
    IReadOnlyDictionary<string, string> ExternalIds,
    IReadOnlyList<string> Urls,
    IReadOnlyList<string> Tags,
    string? Studio,
    IReadOnlyList<CreditPatch> Credits,
    IReadOnlyDictionary<string, string> Dates,
    IReadOnlyDictionary<string, int> Stats,
    IReadOnlyDictionary<string, int> Positions,
    string? Classification) {
    /// <summary>Optional user rating value from 0 through 5.</summary>
    public int? Rating { get; init; }

    /// <summary>Optional shared user-state flags.</summary>
    public EntityMetadataFlagsPatch? Flags { get; init; }
}

/// <summary>
/// Editable shared entity flags carried by the unified metadata patch shape.
/// Null values leave individual flags unchanged when a flags field is applied.
/// </summary>
public sealed record EntityMetadataFlagsPatch(bool? IsFavorite, bool? IsNsfw, bool? IsOrganized);

/// <summary>
/// Credited person patch returned by a plugin.
/// </summary>
public sealed record CreditPatch(string Name, string Role, string? Character, int? SortOrder);

/// <summary>
/// Metadata proposal returned by a plugin process.
/// </summary>
/// <param name="Children">Structural child entity proposals such as seasons and episodes.</param>
/// <param name="Relationships">Non-structural related entity proposals such as people, studios, and tags.</param>
public sealed record EntityMetadataProposal(
    string ProposalId,
    string Provider,
    ProposalKind TargetKind,
    decimal? Confidence,
    string? MatchReason,
    EntityMetadataPatch Patch,
    IReadOnlyList<ImageCandidate> Images,
    IReadOnlyList<EntityMetadataProposal> Children,
    IReadOnlyList<EntitySearchCandidate> Candidates,
    Guid? TargetEntityId = null,
    IReadOnlyList<EntityMetadataProposal> Relationships = null!);

/// <summary>
/// Response envelope written by plugin processes.
/// </summary>
public sealed record IdentifyPluginResponse(bool Ok, EntityMetadataProposal? Result, string? Error);

/// <summary>
/// Request body for applying selected fields from a reviewed metadata proposal.
/// </summary>
public sealed record ApplyIdentifyProposalRequest(
    EntityMetadataProposal Proposal,
    IReadOnlyList<string> SelectedFields,
    IReadOnlyDictionary<string, string?>? SelectedImages);
