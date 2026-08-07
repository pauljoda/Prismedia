using System.Collections.Concurrent;
using Prismedia.Application.Books;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Books;
using Prismedia.Domain.Entities;
using VersOne.Epub;

namespace Prismedia.Infrastructure.Media.Books;

/// <summary>
/// Extracts EPUB navigation and section weights from the server-side archive. The reference reader
/// opens only package/navigation metadata; chapter bodies never cross the API merely to render a TOC.
/// </summary>
internal sealed class EpubBookContentsService(
    IEntityFileContentService files,
    EpubBookContentsCache cache) : IBookContentsService {
    /// <inheritdoc />
    public async Task<BookContentsResponse?> GetAsync(Guid bookId, CancellationToken cancellationToken) {
        var source = await files.GetContentAsync(
            bookId,
            EntityFileRole.Source.ToCode(),
            cancellationToken);
        if (source is null ||
            !string.Equals(Path.GetExtension(source.Path), ".epub", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        var info = new FileInfo(source.Path);
        if (!info.Exists) {
            return null;
        }

        return await cache.GetAsync(
            new EpubBookContentsCacheKey(info.FullName, info.Length, info.LastWriteTimeUtc.Ticks),
            cancellationToken);
    }
}

/// <summary>
/// Process-wide, file-versioned EPUB contents cache. Concurrent requests share one parse, while a
/// changed source path/length/timestamp naturally receives a new entry.
/// </summary>
internal sealed class EpubBookContentsCache {
    private const int MaximumEntries = 256;
    private readonly ConcurrentDictionary<
        EpubBookContentsCacheKey,
        Lazy<Task<BookContentsResponse>>> _entries = new();

    internal async Task<BookContentsResponse> GetAsync(
        EpubBookContentsCacheKey key,
        CancellationToken cancellationToken) {
        if (_entries.Count >= MaximumEntries) {
            foreach (var staleKey in _entries.Keys.Where(candidate =>
                         string.Equals(candidate.Path, key.Path, StringComparison.Ordinal) && candidate != key)) {
                _entries.TryRemove(staleKey, out _);
            }
            if (_entries.Count >= MaximumEntries) {
                _entries.Clear();
            }
        }

        var lazy = _entries.GetOrAdd(
            key,
            static cacheKey => new Lazy<Task<BookContentsResponse>>(
                () => ParseAsync(cacheKey.Path),
                LazyThreadSafetyMode.ExecutionAndPublication));
        try {
            return await lazy.Value.WaitAsync(cancellationToken);
        } catch {
            _entries.TryRemove(new KeyValuePair<EpubBookContentsCacheKey, Lazy<Task<BookContentsResponse>>>(key, lazy));
            throw;
        }
    }

    private static async Task<BookContentsResponse> ParseAsync(string path) {
        using var book = await EpubReader.OpenBookAsync(path);
        var readingOrder = await book.GetReadingOrderAsync();
        var navigation = await book.GetNavigationAsync() ?? [];
        var sectionSizes = readingOrder
            .Select(section => Math.Max(0L, section.ContentFileEntry?.Length ?? 0L))
            .ToArray();
        var sectionIndexByPath = readingOrder
            .Select((section, index) => new { section.FilePath, Index = index })
            .GroupBy(section => section.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);

        var flattened = new List<UnboundedBookContentsEntry>();
        Flatten(navigation, 0, sectionIndexByPath, flattened);
        var deepestByLocation = flattened
            .GroupBy(entry => entry.Location, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(entry => entry.Depth).First(),
                StringComparer.Ordinal);
        var entries = flattened
            .Where(entry => ReferenceEquals(deepestByLocation[entry.Location], entry))
            .Select((entry, order) => entry with { Order = order })
            .ToArray();

        return new BookContentsResponse(AddRanges(entries, sectionSizes));
    }

    private static void Flatten(
        IReadOnlyList<EpubNavigationItemRef> items,
        int depth,
        IReadOnlyDictionary<string, int> sectionIndexByPath,
        List<UnboundedBookContentsEntry> output) {
        foreach (var item in items) {
            var title = item.Title.Trim();
            var link = item.Link;
            if (title.Length > 0 && link is not null) {
                var location = string.IsNullOrWhiteSpace(link.Anchor)
                    ? link.ContentFileUrl
                    : $"{link.ContentFileUrl}#{link.Anchor}";
                if (!string.IsNullOrWhiteSpace(location)) {
                    output.Add(new UnboundedBookContentsEntry(
                        location,
                        title,
                        location,
                        depth,
                        output.Count,
                        sectionIndexByPath.GetValueOrDefault(link.ContentFilePath, -1) is var index && index >= 0
                            ? index
                            : null));
                }
            }

            Flatten(item.NestedItems, depth + 1, sectionIndexByPath, output);
        }
    }

    private static IReadOnlyList<BookContentsEntry> AddRanges(
        IReadOnlyList<UnboundedBookContentsEntry> entries,
        IReadOnlyList<long> sectionSizes) {
        var totalSize = sectionSizes.Sum();
        if (totalSize <= 0) {
            return entries.Select(entry => ToContract(entry)).ToArray();
        }

        var sectionFractions = new double[sectionSizes.Count + 1];
        long accumulated = 0;
        for (var index = 0; index < sectionSizes.Count; index++) {
            accumulated += sectionSizes[index];
            sectionFractions[index + 1] = (double)accumulated / totalSize;
        }

        return entries.Select((entry, index) => {
            if (entry.SectionIndex is not { } sectionIndex ||
                sectionIndex < 0 || sectionIndex >= sectionSizes.Count) {
                return ToContract(entry);
            }

            var nextSectionIndex = entries
                .Skip(index + 1)
                .Select(candidate => candidate.SectionIndex)
                .FirstOrDefault(candidate => candidate is not null && candidate > sectionIndex);
            var start = sectionFractions[sectionIndex];
            var end = nextSectionIndex is { } next
                ? sectionFractions[next]
                : 1d;
            return ToContract(entry, end > start ? start : null, end > start ? end : null);
        }).ToArray();
    }

    private static BookContentsEntry ToContract(
        UnboundedBookContentsEntry entry,
        double? startFraction = null,
        double? endFraction = null) =>
        new(
            entry.Id,
            entry.Title,
            entry.Location,
            entry.Depth,
            entry.Order,
            entry.SectionIndex,
            startFraction,
            endFraction);

    private sealed record UnboundedBookContentsEntry(
        string Id,
        string Title,
        string Location,
        int Depth,
        int Order,
        int? SectionIndex);
}

internal readonly record struct EpubBookContentsCacheKey(string Path, long Length, long LastWriteTicks);
