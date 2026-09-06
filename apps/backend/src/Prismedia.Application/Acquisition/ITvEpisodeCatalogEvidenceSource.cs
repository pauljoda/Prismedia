namespace Prismedia.Application.Acquisition;

/// <summary>Reads bounded provider episode evidence without creating requests, monitors, or library Entities.</summary>
public interface ITvEpisodeCatalogEvidenceSource {
    /// <summary>Returns available canonical season identities for uncertain files, or an empty list when no provider can resolve them.</summary>
    Task<IReadOnlyList<TvSeasonEpisodeCatalog>> ReadAsync(Guid linkedEntityId, int requestedSeason,
        IReadOnlyList<ImportCandidateFile> files, CancellationToken cancellationToken);
}
