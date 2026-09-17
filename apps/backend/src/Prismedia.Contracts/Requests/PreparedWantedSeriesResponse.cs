using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Requests;

/// <summary>
/// A reviewed finite TV selection saved as stable wanted entities without acquisition, monitoring,
/// or an external-manager mutation.
/// </summary>
public sealed record PreparedWantedSeriesResponse(
    Guid SeriesEntityId,
    string Title,
    IReadOnlyList<PreparedWantedEpisode> Episodes);

/// <summary>One selected episode in a prepared wanted series tree.</summary>
public sealed record PreparedWantedEpisode(
    Guid EntityId,
    Guid? SeasonEntityId,
    string Title,
    int SeasonNumber,
    int EpisodeNumber,
    int? AbsoluteNumber,
    bool HasFile,
    ExternalIdentity ExternalIdentity);
