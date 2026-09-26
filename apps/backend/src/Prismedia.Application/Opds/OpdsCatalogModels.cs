using Prismedia.Application.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Opds;

/// <summary>Validated OPDS page request with a one-based page number.</summary>
/// <param name="Page">One-based page number.</param>
/// <param name="Limit">Maximum number of items in the page.</param>
public sealed record OpdsPageRequest(int Page, int Limit) {
    /// <summary>Zero-based item offset represented by this page.</summary>
    public int Offset => (Page - 1) * Limit;
}

/// <summary>Page of OPDS catalog items plus total count for pagination links.</summary>
public sealed record OpdsCatalogPage<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int Limit) {
    /// <summary>True when there is another page after this one.</summary>
    public bool HasNextPage => Page * Limit < TotalCount;

    /// <summary>True when there is a page before this one.</summary>
    public bool HasPreviousPage => Page > 1;
}

/// <summary>One named navigation entry backed by at least one visible book.</summary>
public sealed record OpdsNavigationEntry(
    Guid Id,
    string Title,
    int VisibleBookCount,
    DateTimeOffset UpdatedAt);

/// <summary>Person, creator, writer, artist, or similar contributor shown on a book entry.</summary>
public sealed record OpdsContributor(Guid Id, string Name);

/// <summary>Visible tag/category attached to a book entry.</summary>
public sealed record OpdsCategory(Guid Id, string Name);

/// <summary>One visible publication exposed as an OPDS acquisition entry.</summary>
public sealed record OpdsBookEntry(
    Guid Id,
    string Title,
    string? Summary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? SeriesId,
    string? SeriesTitle,
    IReadOnlyList<OpdsContributor> Authors,
    IReadOnlyList<OpdsCategory> Categories,
    string AcquisitionContentType,
    long? SizeBytes,
    string? CoverContentType,
    string? ThumbnailContentType);

/// <summary>Streamable OPDS book cover or acquisition file resolved after authorization.</summary>
public sealed record OpdsFileContent(
    Guid EntityId,
    EntityFileRole Role,
    string Path,
    string ContentType,
    string? FileName);

/// <summary>Read-side application port for the OPDS catalog.</summary>
public interface IOpdsCatalogService {
    /// <summary>Counts books that can appear in OPDS feeds for the current visibility mode.</summary>
    Task<int> CountVisibleBooksAsync(bool hideNsfw, CancellationToken cancellationToken);

    /// <summary>Lists recently added visible books for the OPDS root catalog.</summary>
    Task<OpdsCatalogPage<OpdsBookEntry>> ListRecentAsync(
        bool hideNsfw,
        OpdsPageRequest page,
        CancellationToken cancellationToken);

    /// <summary>Lists watched roots that contain at least one visible OPDS book.</summary>
    Task<OpdsCatalogPage<OpdsNavigationEntry>> ListLibrariesAsync(
        bool hideNsfw,
        OpdsPageRequest page,
        CancellationToken cancellationToken);

    /// <summary>Lists visible OPDS books under one watched root, or null when the root is hidden.</summary>
    Task<OpdsCatalogPage<OpdsBookEntry>?> ListLibraryBooksAsync(
        Guid libraryId,
        bool hideNsfw,
        OpdsPageRequest page,
        CancellationToken cancellationToken);

    /// <summary>Lists contributors that are attached to at least one visible OPDS book.</summary>
    Task<OpdsCatalogPage<OpdsNavigationEntry>> ListAuthorsAsync(
        bool hideNsfw,
        OpdsPageRequest page,
        CancellationToken cancellationToken);

    /// <summary>Lists visible OPDS books for one contributor, or null when the contributor is hidden.</summary>
    Task<OpdsCatalogPage<OpdsBookEntry>?> ListAuthorBooksAsync(
        Guid authorId,
        bool hideNsfw,
        OpdsPageRequest page,
        CancellationToken cancellationToken);

    /// <summary>Lists series that contain at least one visible OPDS book.</summary>
    Task<OpdsCatalogPage<OpdsNavigationEntry>> ListSeriesAsync(
        bool hideNsfw,
        OpdsPageRequest page,
        CancellationToken cancellationToken);

    /// <summary>Lists visible OPDS books in one series, or null when the series is hidden.</summary>
    Task<OpdsCatalogPage<OpdsBookEntry>?> ListSeriesBooksAsync(
        Guid seriesId,
        bool hideNsfw,
        OpdsPageRequest page,
        CancellationToken cancellationToken);

    /// <summary>Lists collections that contain at least one visible OPDS book.</summary>
    Task<OpdsCatalogPage<OpdsNavigationEntry>> ListCollectionsAsync(
        bool hideNsfw,
        OpdsPageRequest page,
        CancellationToken cancellationToken);

    /// <summary>Lists visible OPDS books in one collection, or null when the collection is hidden.</summary>
    Task<OpdsCatalogPage<OpdsBookEntry>?> ListCollectionBooksAsync(
        Guid collectionId,
        bool hideNsfw,
        OpdsPageRequest page,
        CancellationToken cancellationToken);

    /// <summary>Lists tags that are attached to at least one visible OPDS book.</summary>
    Task<OpdsCatalogPage<OpdsNavigationEntry>> ListTagsAsync(
        bool hideNsfw,
        OpdsPageRequest page,
        CancellationToken cancellationToken);

    /// <summary>Lists visible OPDS books for one tag, or null when the tag is hidden.</summary>
    Task<OpdsCatalogPage<OpdsBookEntry>?> ListTagBooksAsync(
        Guid tagId,
        bool hideNsfw,
        OpdsPageRequest page,
        CancellationToken cancellationToken);

    /// <summary>Searches visible OPDS books and visible related metadata.</summary>
    Task<OpdsCatalogPage<OpdsBookEntry>> SearchBooksAsync(
        string? query,
        bool hideNsfw,
        OpdsPageRequest page,
        CancellationToken cancellationToken);

    /// <summary>Gets one visible OPDS book entry by id.</summary>
    Task<OpdsBookEntry?> GetBookAsync(
        Guid bookId,
        bool hideNsfw,
        CancellationToken cancellationToken);

    /// <summary>Resolves one visible book source file for an authorized OPDS download.</summary>
    Task<OpdsFileContent?> GetBookDownloadAsync(
        Guid bookId,
        bool hideNsfw,
        CancellationToken cancellationToken);

    /// <summary>Resolves one visible book cover file for an authorized OPDS image request.</summary>
    Task<OpdsFileContent?> GetBookCoverAsync(
        Guid bookId,
        bool hideNsfw,
        CancellationToken cancellationToken);
}
