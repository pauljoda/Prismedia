using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Builds ID-first identify hints from persisted entity metadata.
/// </summary>
public sealed partial class IdentifyMatchHintResolver {
    private readonly PrismediaDbContext _db;

    /// <summary>
    /// Creates a resolver over entity capability rows.
    /// </summary>
    /// <param name="db">Database context containing entity links and source files.</param>
    public IdentifyMatchHintResolver(PrismediaDbContext db) {
        _db = db;
    }

    /// <summary>
    /// Resolves provider IDs, URLs, title, and source file path for an entity.
    /// Existing external IDs win over IDs parsed from URLs.
    /// </summary>
    /// <param name="entityId">Entity to identify.</param>
    /// <param name="provider">Provider key, such as tmdb.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Match hints for plugin execution.</returns>
    public async Task<IdentifyMatchHints> ResolveAsync(
        Guid entityId,
        string provider,
        CancellationToken cancellationToken) {
        var title = await _db.Entities
            .AsNoTracking()
            .Where(entity => entity.Id == entityId && entity.DeletedAt == null)
            .Select(entity => entity.Title)
            .SingleOrDefaultAsync(cancellationToken);

        if (title is null) {
            return new IdentifyMatchHints(new Dictionary<string, string>(), [], null, null);
        }

        var externalIds = await _db.EntityExternalIds
            .AsNoTracking()
            .Where(row => row.EntityId == entityId)
            .ToDictionaryAsync(row => row.Provider, row => row.Value, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var urls = await _db.EntityUrls
            .AsNoTracking()
            .Where(row => row.EntityId == entityId)
            .OrderBy(row => row.SortOrder)
            .Select(row => row.Url)
            .ToArrayAsync(cancellationToken);

        if (!externalIds.ContainsKey(provider) && TryParseProviderId(provider, urls, out var parsedId)) {
            externalIds[provider] = parsedId;
        }

        var filePath = await _db.EntityFiles
            .AsNoTracking()
            .Where(row => row.EntityId == entityId && row.Role == EntityFileRole.Source)
            .OrderBy(row => row.CreatedAt)
            .Select(row => row.Path)
            .FirstOrDefaultAsync(cancellationToken);

        return new IdentifyMatchHints(externalIds, urls, title, filePath);
    }

    private static bool TryParseProviderId(string provider, IReadOnlyList<string> urls, out string id) {
        id = string.Empty;
        if (!string.Equals(provider, "tmdb", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        foreach (var url in urls) {
            var match = TmdbUrlRegex().Match(url);
            if (!match.Success) {
                continue;
            }

            id = match.Groups["id"].Value;
            return true;
        }

        return false;
    }

    [GeneratedRegex(@"themoviedb\.org/(movie|tv)/(?<id>\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TmdbUrlRegex();
}
