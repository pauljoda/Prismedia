using System.Xml.Linq;
using Microsoft.AspNetCore.Http.Extensions;
using Prismedia.Api.Security;
using Prismedia.Application.Opds;
using Prismedia.Contracts.Jellyfin;
using Prismedia.Contracts.Media;
using Prismedia.Contracts.Opds;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

public static class OpdsEndpoints {
    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;
    private static readonly XNamespace Atom = OpdsProtocol.AtomNamespace;
    private static readonly XNamespace Opds = OpdsProtocol.OpdsNamespace;
    private static readonly XNamespace Dublin = OpdsProtocol.DublinCoreTermsNamespace;
    private static readonly XNamespace OpenSearch = OpdsProtocol.OpenSearchNamespace;

    public static IEndpointRouteBuilder MapOpdsEndpoints(this IEndpointRouteBuilder routes) {
        routes.MapGet(OpdsProtocol.Prefix, RootAsync)
            .WithTags("OPDS")
            .WithName("GetOpdsRoot")
            .WithSummary("Gets the OPDS navigation root catalog.")
            .Produces(StatusCodes.Status200OK, contentType: OpdsProtocol.ContentTypes.NavigationFeed);

        routes.MapGet(OpdsProtocol.Routes.Catalog, RootAsync)
            .WithTags("OPDS")
            .WithName("GetOpdsCatalog")
            .WithSummary("Gets the OPDS navigation root catalog.")
            .Produces(StatusCodes.Status200OK, contentType: OpdsProtocol.ContentTypes.NavigationFeed);

        routes.MapGet(OpdsProtocol.Routes.Recent, RecentAsync)
            .WithTags("OPDS")
            .WithName("GetOpdsRecent")
            .WithSummary("Lists recently added OPDS books and comics.")
            .Produces(StatusCodes.Status200OK, contentType: OpdsProtocol.ContentTypes.AcquisitionFeed)
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        routes.MapGet(OpdsProtocol.Routes.Libraries, LibrariesAsync)
            .WithTags("OPDS")
            .WithName("GetOpdsLibraries")
            .WithSummary("Lists OPDS book library roots.")
            .Produces(StatusCodes.Status200OK, contentType: OpdsProtocol.ContentTypes.NavigationFeed)
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        routes.MapGet(OpdsProtocol.Routes.Libraries + "/{libraryId:guid}", LibraryBooksAsync)
            .WithTags("OPDS")
            .WithName("GetOpdsLibraryBooks")
            .WithSummary("Lists OPDS books in one library.")
            .Produces(StatusCodes.Status200OK, contentType: OpdsProtocol.ContentTypes.AcquisitionFeed)
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        routes.MapGet(OpdsProtocol.Routes.Authors, AuthorsAsync)
            .WithTags("OPDS")
            .WithName("GetOpdsAuthors")
            .WithSummary("Lists OPDS authors with visible books.")
            .Produces(StatusCodes.Status200OK, contentType: OpdsProtocol.ContentTypes.NavigationFeed)
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        routes.MapGet(OpdsProtocol.Routes.Authors + "/{authorId:guid}", AuthorBooksAsync)
            .WithTags("OPDS")
            .WithName("GetOpdsAuthorBooks")
            .WithSummary("Lists OPDS books for one author.")
            .Produces(StatusCodes.Status200OK, contentType: OpdsProtocol.ContentTypes.AcquisitionFeed)
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        routes.MapGet(OpdsProtocol.Routes.Series, SeriesAsync)
            .WithTags("OPDS")
            .WithName("GetOpdsSeries")
            .WithSummary("Lists OPDS series with visible books.")
            .Produces(StatusCodes.Status200OK, contentType: OpdsProtocol.ContentTypes.NavigationFeed)
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        routes.MapGet(OpdsProtocol.Routes.Series + "/{seriesId:guid}", SeriesBooksAsync)
            .WithTags("OPDS")
            .WithName("GetOpdsSeriesBooks")
            .WithSummary("Lists OPDS books in one series.")
            .Produces(StatusCodes.Status200OK, contentType: OpdsProtocol.ContentTypes.AcquisitionFeed)
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        routes.MapGet(OpdsProtocol.Routes.Collections, CollectionsAsync)
            .WithTags("OPDS")
            .WithName("GetOpdsCollections")
            .WithSummary("Lists OPDS collections with visible books.")
            .Produces(StatusCodes.Status200OK, contentType: OpdsProtocol.ContentTypes.NavigationFeed)
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        routes.MapGet(OpdsProtocol.Routes.Collections + "/{collectionId:guid}", CollectionBooksAsync)
            .WithTags("OPDS")
            .WithName("GetOpdsCollectionBooks")
            .WithSummary("Lists OPDS books in one collection.")
            .Produces(StatusCodes.Status200OK, contentType: OpdsProtocol.ContentTypes.AcquisitionFeed)
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        routes.MapGet(OpdsProtocol.Routes.Tags, TagsAsync)
            .WithTags("OPDS")
            .WithName("GetOpdsTags")
            .WithSummary("Lists OPDS tags with visible books.")
            .Produces(StatusCodes.Status200OK, contentType: OpdsProtocol.ContentTypes.NavigationFeed)
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        routes.MapGet(OpdsProtocol.Routes.Tags + "/{tagId:guid}", TagBooksAsync)
            .WithTags("OPDS")
            .WithName("GetOpdsTagBooks")
            .WithSummary("Lists OPDS books for one tag.")
            .Produces(StatusCodes.Status200OK, contentType: OpdsProtocol.ContentTypes.AcquisitionFeed)
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        routes.MapGet(OpdsProtocol.Routes.Search, SearchAsync)
            .WithTags("OPDS")
            .WithName("SearchOpdsBooks")
            .WithSummary("Searches OPDS books and comics.")
            .Produces(StatusCodes.Status200OK, contentType: OpdsProtocol.ContentTypes.AcquisitionFeed)
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        routes.MapGet(OpdsProtocol.Routes.Book(default).Replace(Guid.Empty.ToString(), "{bookId:guid}"), BookAsync)
            .WithTags("OPDS")
            .WithName("GetOpdsBook")
            .WithSummary("Gets one OPDS book entry.")
            .Produces(StatusCodes.Status200OK, contentType: OpdsProtocol.ContentTypes.AcquisitionFeed)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        routes.MapGet(OpdsProtocol.Routes.BookDownload(default).Replace(Guid.Empty.ToString(), "{bookId:guid}"), DownloadAsync)
            .WithTags("OPDS")
            .WithName("DownloadOpdsBook")
            .WithSummary("Downloads one OPDS book source file.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status206PartialContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        routes.MapGet(OpdsProtocol.Routes.BookCover(default).Replace(Guid.Empty.ToString(), "{bookId:guid}"), CoverAsync)
            .WithTags("OPDS")
            .WithName("GetOpdsBookCover")
            .WithSummary("Streams one OPDS book cover image.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status206PartialContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        routes.MapGet(OpdsProtocol.Routes.OpenSearch, OpenSearchAsync)
            .WithTags("OPDS")
            .WithName("GetOpdsOpenSearch")
            .WithSummary("Gets the OPDS OpenSearch descriptor.")
            .Produces(StatusCodes.Status200OK, contentType: OpdsProtocol.ContentTypes.OpenSearch);

        return routes;
    }

    private static async Task<IResult> RootAsync(
        HttpContext httpContext,
        IOpdsCatalogService catalog,
        CancellationToken cancellationToken) {
        var hideNsfw = ShouldHideNsfw(httpContext);
        var visibleBooks = await catalog.CountVisibleBooksAsync(hideNsfw, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var entries = new List<NavigationXmlEntry>();

        if (visibleBooks > 0) {
            entries.Add(new NavigationXmlEntry(
                OpdsProtocol.Urns.Recent,
                "Recently Added",
                "Newest visible books and comics.",
                now,
                OpdsProtocol.Routes.Recent,
                OpdsProtocol.ContentTypes.AcquisitionFeed,
                visibleBooks));
            entries.Add(new NavigationXmlEntry(
                OpdsProtocol.Urns.Libraries,
                "Libraries",
                "Browse books by library.",
                now,
                OpdsProtocol.Routes.Libraries,
                OpdsProtocol.ContentTypes.NavigationFeed,
                null));
            entries.Add(new NavigationXmlEntry(
                OpdsProtocol.Urns.Authors,
                "Authors",
                "Browse books by credited people.",
                now,
                OpdsProtocol.Routes.Authors,
                OpdsProtocol.ContentTypes.NavigationFeed,
                null));
            entries.Add(new NavigationXmlEntry(
                OpdsProtocol.Urns.Series,
                "Series",
                "Browse grouped book series.",
                now,
                OpdsProtocol.Routes.Series,
                OpdsProtocol.ContentTypes.NavigationFeed,
                null));
            entries.Add(new NavigationXmlEntry(
                OpdsProtocol.Urns.Collections,
                "Collections",
                "Browse book collections.",
                now,
                OpdsProtocol.Routes.Collections,
                OpdsProtocol.ContentTypes.NavigationFeed,
                null));
            entries.Add(new NavigationXmlEntry(
                OpdsProtocol.Urns.Tags,
                "Tags",
                "Browse books by tag.",
                now,
                OpdsProtocol.Routes.Tags,
                OpdsProtocol.ContentTypes.NavigationFeed,
                null));
        }

        entries.Add(new NavigationXmlEntry(
            OpdsProtocol.Urns.Search,
            "Search",
            "Search visible books and comics.",
            now,
            OpdsProtocol.Routes.Search,
            OpdsProtocol.ContentTypes.AcquisitionFeed,
            null));

        return NavigationFeed(
            httpContext.Request,
            "Prismedia OPDS",
            OpdsProtocol.Urns.Root,
            OpdsProtocol.Prefix,
            null,
            entries);
    }

    private static async Task<IResult> RecentAsync(
        int? page,
        int? limit,
        HttpContext httpContext,
        IOpdsCatalogService catalog,
        CancellationToken cancellationToken) {
        if (!TryCreatePage(page, limit, out var request, out var error)) {
            return error;
        }

        return AcquisitionFeed(
            httpContext.Request,
            "Recently Added",
            OpdsProtocol.Urns.Recent,
            OpdsProtocol.Routes.Recent,
            OpdsProtocol.Prefix,
            await catalog.ListRecentAsync(ShouldHideNsfw(httpContext), request, cancellationToken));
    }

    private static async Task<IResult> LibrariesAsync(
        int? page,
        int? limit,
        HttpContext httpContext,
        IOpdsCatalogService catalog,
        CancellationToken cancellationToken) {
        if (!TryCreatePage(page, limit, out var request, out var error)) {
            return error;
        }

        var entries = await catalog.ListLibrariesAsync(ShouldHideNsfw(httpContext), request, cancellationToken);
        return NavigationFeed(
            httpContext.Request,
            "Libraries",
            OpdsProtocol.Urns.Libraries,
            OpdsProtocol.Routes.Libraries,
            OpdsProtocol.Prefix,
            entries,
            item => OpdsProtocol.Urns.Library(item.Id),
            item => OpdsProtocol.Routes.Library(item.Id),
            OpdsProtocol.ContentTypes.AcquisitionFeed);
    }

    private static async Task<IResult> LibraryBooksAsync(
        Guid libraryId,
        int? page,
        int? limit,
        HttpContext httpContext,
        IOpdsCatalogService catalog,
        CancellationToken cancellationToken) {
        if (!TryCreatePage(page, limit, out var request, out var error)) {
            return error;
        }

        var books = await catalog.ListLibraryBooksAsync(libraryId, ShouldHideNsfw(httpContext), request, cancellationToken);
        return books is null
            ? OpdsNotFound()
            : AcquisitionFeed(
                httpContext.Request,
                "Library Books",
                OpdsProtocol.Urns.Library(libraryId),
                OpdsProtocol.Routes.Library(libraryId),
                OpdsProtocol.Routes.Libraries,
                books);
    }

    private static async Task<IResult> AuthorsAsync(
        int? page,
        int? limit,
        HttpContext httpContext,
        IOpdsCatalogService catalog,
        CancellationToken cancellationToken) {
        if (!TryCreatePage(page, limit, out var request, out var error)) {
            return error;
        }

        var entries = await catalog.ListAuthorsAsync(ShouldHideNsfw(httpContext), request, cancellationToken);
        return NavigationFeed(
            httpContext.Request,
            "Authors",
            OpdsProtocol.Urns.Authors,
            OpdsProtocol.Routes.Authors,
            OpdsProtocol.Prefix,
            entries,
            item => OpdsProtocol.Urns.Author(item.Id),
            item => OpdsProtocol.Routes.Author(item.Id),
            OpdsProtocol.ContentTypes.AcquisitionFeed);
    }

    private static async Task<IResult> AuthorBooksAsync(
        Guid authorId,
        int? page,
        int? limit,
        HttpContext httpContext,
        IOpdsCatalogService catalog,
        CancellationToken cancellationToken) {
        if (!TryCreatePage(page, limit, out var request, out var error)) {
            return error;
        }

        var books = await catalog.ListAuthorBooksAsync(authorId, ShouldHideNsfw(httpContext), request, cancellationToken);
        return books is null
            ? OpdsNotFound()
            : AcquisitionFeed(
                httpContext.Request,
                "Author Books",
                OpdsProtocol.Urns.Author(authorId),
                OpdsProtocol.Routes.Author(authorId),
                OpdsProtocol.Routes.Authors,
                books);
    }

    private static async Task<IResult> SeriesAsync(
        int? page,
        int? limit,
        HttpContext httpContext,
        IOpdsCatalogService catalog,
        CancellationToken cancellationToken) {
        if (!TryCreatePage(page, limit, out var request, out var error)) {
            return error;
        }

        var entries = await catalog.ListSeriesAsync(ShouldHideNsfw(httpContext), request, cancellationToken);
        return NavigationFeed(
            httpContext.Request,
            "Series",
            OpdsProtocol.Urns.Series,
            OpdsProtocol.Routes.Series,
            OpdsProtocol.Prefix,
            entries,
            item => OpdsProtocol.Urns.SeriesItem(item.Id),
            item => OpdsProtocol.Routes.SeriesItem(item.Id),
            OpdsProtocol.ContentTypes.AcquisitionFeed);
    }

    private static async Task<IResult> SeriesBooksAsync(
        Guid seriesId,
        int? page,
        int? limit,
        HttpContext httpContext,
        IOpdsCatalogService catalog,
        CancellationToken cancellationToken) {
        if (!TryCreatePage(page, limit, out var request, out var error)) {
            return error;
        }

        var books = await catalog.ListSeriesBooksAsync(seriesId, ShouldHideNsfw(httpContext), request, cancellationToken);
        return books is null
            ? OpdsNotFound()
            : AcquisitionFeed(
                httpContext.Request,
                "Series Books",
                OpdsProtocol.Urns.SeriesItem(seriesId),
                OpdsProtocol.Routes.SeriesItem(seriesId),
                OpdsProtocol.Routes.Series,
                books);
    }

    private static async Task<IResult> CollectionsAsync(
        int? page,
        int? limit,
        HttpContext httpContext,
        IOpdsCatalogService catalog,
        CancellationToken cancellationToken) {
        if (!TryCreatePage(page, limit, out var request, out var error)) {
            return error;
        }

        var entries = await catalog.ListCollectionsAsync(ShouldHideNsfw(httpContext), request, cancellationToken);
        return NavigationFeed(
            httpContext.Request,
            "Collections",
            OpdsProtocol.Urns.Collections,
            OpdsProtocol.Routes.Collections,
            OpdsProtocol.Prefix,
            entries,
            item => OpdsProtocol.Urns.Collection(item.Id),
            item => OpdsProtocol.Routes.Collection(item.Id),
            OpdsProtocol.ContentTypes.AcquisitionFeed);
    }

    private static async Task<IResult> CollectionBooksAsync(
        Guid collectionId,
        int? page,
        int? limit,
        HttpContext httpContext,
        IOpdsCatalogService catalog,
        CancellationToken cancellationToken) {
        if (!TryCreatePage(page, limit, out var request, out var error)) {
            return error;
        }

        var books = await catalog.ListCollectionBooksAsync(collectionId, ShouldHideNsfw(httpContext), request, cancellationToken);
        return books is null
            ? OpdsNotFound()
            : AcquisitionFeed(
                httpContext.Request,
                "Collection Books",
                OpdsProtocol.Urns.Collection(collectionId),
                OpdsProtocol.Routes.Collection(collectionId),
                OpdsProtocol.Routes.Collections,
                books);
    }

    private static async Task<IResult> TagsAsync(
        int? page,
        int? limit,
        HttpContext httpContext,
        IOpdsCatalogService catalog,
        CancellationToken cancellationToken) {
        if (!TryCreatePage(page, limit, out var request, out var error)) {
            return error;
        }

        var entries = await catalog.ListTagsAsync(ShouldHideNsfw(httpContext), request, cancellationToken);
        return NavigationFeed(
            httpContext.Request,
            "Tags",
            OpdsProtocol.Urns.Tags,
            OpdsProtocol.Routes.Tags,
            OpdsProtocol.Prefix,
            entries,
            item => OpdsProtocol.Urns.Tag(item.Id),
            item => OpdsProtocol.Routes.Tag(item.Id),
            OpdsProtocol.ContentTypes.AcquisitionFeed);
    }

    private static async Task<IResult> TagBooksAsync(
        Guid tagId,
        int? page,
        int? limit,
        HttpContext httpContext,
        IOpdsCatalogService catalog,
        CancellationToken cancellationToken) {
        if (!TryCreatePage(page, limit, out var request, out var error)) {
            return error;
        }

        var books = await catalog.ListTagBooksAsync(tagId, ShouldHideNsfw(httpContext), request, cancellationToken);
        return books is null
            ? OpdsNotFound()
            : AcquisitionFeed(
                httpContext.Request,
                "Tagged Books",
                OpdsProtocol.Urns.Tag(tagId),
                OpdsProtocol.Routes.Tag(tagId),
                OpdsProtocol.Routes.Tags,
                books);
    }

    private static async Task<IResult> SearchAsync(
        string? q,
        int? page,
        int? limit,
        HttpContext httpContext,
        IOpdsCatalogService catalog,
        CancellationToken cancellationToken) {
        if (!TryCreatePage(page, limit, out var request, out var error)) {
            return error;
        }

        return AcquisitionFeed(
            httpContext.Request,
            string.IsNullOrWhiteSpace(q) ? "Search" : $"Search: {q}",
            OpdsProtocol.Urns.Search,
            OpdsProtocol.Routes.Search,
            OpdsProtocol.Prefix,
            await catalog.SearchBooksAsync(q, ShouldHideNsfw(httpContext), request, cancellationToken),
            q);
    }

    private static async Task<IResult> BookAsync(
        Guid bookId,
        HttpContext httpContext,
        IOpdsCatalogService catalog,
        CancellationToken cancellationToken) {
        var book = await catalog.GetBookAsync(bookId, ShouldHideNsfw(httpContext), cancellationToken);
        if (book is null) {
            return OpdsNotFound();
        }

        var page = new OpdsCatalogPage<OpdsBookEntry>([book], 1, 1, 1);
        return AcquisitionFeed(
            httpContext.Request,
            book.Title,
            OpdsProtocol.Urns.Book(bookId),
            OpdsProtocol.Routes.Book(bookId),
            OpdsProtocol.Prefix,
            page);
    }

    private static async Task<IResult> DownloadAsync(
        Guid bookId,
        HttpContext httpContext,
        IOpdsCatalogService catalog,
        CancellationToken cancellationToken) {
        var file = await catalog.GetBookDownloadAsync(bookId, ShouldHideNsfw(httpContext), cancellationToken);
        return file is null
            ? OpdsNotFound()
            : await EntityFileResults.StreamAsync(
                file.Path,
                file.ContentType,
                OpdsNotFound,
                cancellationToken,
                file.FileName);
    }

    private static async Task<IResult> CoverAsync(
        Guid bookId,
        HttpContext httpContext,
        IOpdsCatalogService catalog,
        CancellationToken cancellationToken) {
        var file = await catalog.GetBookCoverAsync(bookId, ShouldHideNsfw(httpContext), cancellationToken);
        return file is null
            ? OpdsNotFound()
            : await EntityFileResults.StreamAsync(
                file.Path,
                file.ContentType,
                OpdsNotFound,
                cancellationToken);
    }

    private static IResult OpenSearchAsync(HttpContext httpContext) {
        var template = OpenSearchTemplate(httpContext.Request);
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(OpenSearch + "OpenSearchDescription",
                new XAttribute(XNamespace.Xmlns + "atom", Atom.NamespaceName),
                new XElement(OpenSearch + "ShortName", "Prismedia"),
                new XElement(OpenSearch + "Description", "Search Prismedia books and comics."),
                new XElement(OpenSearch + "Url",
                    new XAttribute("type", OpdsProtocol.ContentTypes.AcquisitionFeed),
                    new XAttribute("template", template))));
        return Xml(document, OpdsProtocol.ContentTypes.OpenSearch);
    }

    private static string OpenSearchTemplate(HttpRequest request) {
        var baseUrl = AbsoluteUrl(request, OpdsProtocol.Routes.Search);
        var separator = baseUrl.Contains('?') ? "&" : "?";
        return $"{baseUrl}{separator}{OpdsProtocol.Query.Search}={{searchTerms}}";
    }

    private static IResult NavigationFeed(
        HttpRequest request,
        string title,
        string id,
        string selfPath,
        string? upPath,
        IReadOnlyList<NavigationXmlEntry> entries) {
        var updated = entries.Count == 0 ? DateTimeOffset.UtcNow : entries.Max(entry => entry.UpdatedAt);
        var feed = FeedBase(request, title, id, updated, selfPath, upPath, navigation: true);
        foreach (var entry in entries) {
            feed.Add(NavigationEntry(request, entry));
        }

        return Xml(new XDocument(new XDeclaration("1.0", "utf-8", null), feed), OpdsProtocol.ContentTypes.NavigationFeed);
    }

    private static IResult NavigationFeed(
        HttpRequest request,
        string title,
        string id,
        string selfPath,
        string? upPath,
        OpdsCatalogPage<OpdsNavigationEntry> page,
        Func<OpdsNavigationEntry, string> idFor,
        Func<OpdsNavigationEntry, string> pathFor,
        string entryContentType) {
        var updated = page.Items.Count == 0 ? DateTimeOffset.UtcNow : page.Items.Max(entry => entry.UpdatedAt);
        var feed = FeedBase(request, title, id, updated, selfPath, upPath, navigation: true);
        AddPaginationLinks(feed, request, selfPath, page, null);
        AddOpenSearchPaging(feed, page);

        foreach (var entry in page.Items) {
            feed.Add(NavigationEntry(
                request,
                new NavigationXmlEntry(
                    idFor(entry),
                    entry.Title,
                    $"{entry.VisibleBookCount} visible item(s).",
                    entry.UpdatedAt,
                    pathFor(entry),
                    entryContentType,
                    entry.VisibleBookCount)));
        }

        return Xml(new XDocument(new XDeclaration("1.0", "utf-8", null), feed), OpdsProtocol.ContentTypes.NavigationFeed);
    }

    private static IResult AcquisitionFeed(
        HttpRequest request,
        string title,
        string id,
        string selfPath,
        string? upPath,
        OpdsCatalogPage<OpdsBookEntry> page,
        string? searchQuery = null) {
        var updated = page.Items.Count == 0 ? DateTimeOffset.UtcNow : page.Items.Max(entry => entry.UpdatedAt);
        var feed = FeedBase(request, title, id, updated, selfPath, upPath, navigation: false, searchQuery);
        AddPaginationLinks(feed, request, selfPath, page, searchQuery);
        AddOpenSearchPaging(feed, page);

        foreach (var book in page.Items) {
            feed.Add(BookEntry(request, book));
        }

        return Xml(new XDocument(new XDeclaration("1.0", "utf-8", null), feed), OpdsProtocol.ContentTypes.AcquisitionFeed);
    }

    private static XElement FeedBase(
        HttpRequest request,
        string title,
        string id,
        DateTimeOffset updated,
        string selfPath,
        string? upPath,
        bool navigation,
        string? searchQuery = null) {
        var selfQuery = QueryPairs(
            (OpdsProtocol.Query.Page, request.Query[OpdsProtocol.Query.Page].FirstOrDefault()),
            (OpdsProtocol.Query.Limit, request.Query[OpdsProtocol.Query.Limit].FirstOrDefault()),
            (OpdsProtocol.Query.Search, searchQuery));
        var feed = new XElement(Atom + "feed",
            new XAttribute(XNamespace.Xmlns + "opds", Opds.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "dcterms", Dublin.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "os", OpenSearch.NamespaceName),
            new XElement(Atom + "id", id),
            new XElement(Atom + "title", title),
            new XElement(Atom + "updated", FormatDate(updated)),
            new XElement(Atom + "author", new XElement(Atom + "name", OpdsProtocol.Generator)),
            new XElement(Atom + "generator", OpdsProtocol.Generator),
            Link(request, OpdsProtocol.LinkRelations.Self, selfPath, navigation ? OpdsProtocol.ContentTypes.NavigationFeed : OpdsProtocol.ContentTypes.AcquisitionFeed, selfQuery),
            Link(request, OpdsProtocol.LinkRelations.Start, OpdsProtocol.Prefix, OpdsProtocol.ContentTypes.NavigationFeed),
            Link(request, OpdsProtocol.LinkRelations.Search, OpdsProtocol.Routes.OpenSearch, OpdsProtocol.ContentTypes.OpenSearch));

        if (upPath is not null) {
            feed.Add(Link(request, OpdsProtocol.LinkRelations.Up, upPath, OpdsProtocol.ContentTypes.NavigationFeed));
        }

        return feed;
    }

    private static XElement NavigationEntry(HttpRequest request, NavigationXmlEntry entry) {
        var element = new XElement(Atom + "entry",
            new XElement(Atom + "id", entry.Id),
            new XElement(Atom + "title", entry.Title),
            new XElement(Atom + "updated", FormatDate(entry.UpdatedAt)),
            new XElement(Atom + "content",
                new XAttribute("type", OpdsProtocol.ContentTypes.Text),
                entry.Summary),
            Link(request, OpdsProtocol.LinkRelations.Subsection, entry.Href, entry.Type));

        if (entry.Count is { } count) {
            element.Add(new XElement(OpenSearch + "totalResults", count));
        }

        return element;
    }

    private static XElement BookEntry(HttpRequest request, OpdsBookEntry book) {
        var entry = new XElement(Atom + "entry",
            new XElement(Atom + "id", OpdsProtocol.Urns.Book(book.Id)),
            new XElement(Atom + "title", book.Title),
            new XElement(Atom + "updated", FormatDate(book.UpdatedAt)));

        foreach (var author in book.Authors) {
            entry.Add(new XElement(Atom + "author",
                new XElement(Atom + "name", author.Name),
                new XElement(Atom + "uri", AbsoluteUrl(request, OpdsProtocol.Routes.Author(author.Id)))));
        }

        if (!string.IsNullOrWhiteSpace(book.Summary)) {
            entry.Add(new XElement(Atom + "summary", book.Summary));
        }

        foreach (var category in book.Categories) {
            entry.Add(new XElement(Atom + "category",
                new XAttribute("term", category.Name),
                new XAttribute("label", category.Name)));
        }

        if (!string.IsNullOrWhiteSpace(book.SeriesTitle)) {
            entry.Add(new XElement(Dublin + "isPartOf", book.SeriesTitle));
        }

        if (book.CoverContentType is not null) {
            entry.Add(Link(request, OpdsProtocol.LinkRelations.Image, OpdsProtocol.Routes.BookCover(book.Id), book.CoverContentType));
        }

        if (book.ThumbnailContentType is not null) {
            entry.Add(Link(request, OpdsProtocol.LinkRelations.Thumbnail, OpdsProtocol.Routes.BookCover(book.Id), book.ThumbnailContentType));
        }

        var acquisition = Link(
            request,
            OpdsProtocol.LinkRelations.Acquisition,
            OpdsProtocol.Routes.BookDownload(book.Id),
            book.AcquisitionContentType);
        if (book.SizeBytes is { } size) {
            acquisition.Add(new XAttribute("length", size));
        }

        entry.Add(acquisition);
        entry.Add(Link(request, OpdsProtocol.LinkRelations.Self, OpdsProtocol.Routes.Book(book.Id), OpdsProtocol.ContentTypes.AcquisitionFeed));
        return entry;
    }

    private static XElement Link(
        HttpRequest request,
        string rel,
        string path,
        string type,
        IReadOnlyList<KeyValuePair<string, string?>>? query = null) =>
        new(Atom + "link",
            new XAttribute("rel", rel),
            new XAttribute("href", AbsoluteUrl(request, path, query)),
            new XAttribute("type", type));

    private static void AddPaginationLinks<T>(
        XElement feed,
        HttpRequest request,
        string selfPath,
        OpdsCatalogPage<T> page,
        string? searchQuery) {
        if (page.HasPreviousPage) {
            feed.Add(Link(
                request,
                OpdsProtocol.LinkRelations.Previous,
                selfPath,
                OpdsProtocol.ContentTypes.Atom,
                QueryPairs(
                    (OpdsProtocol.Query.Page, (page.Page - 1).ToString()),
                    (OpdsProtocol.Query.Limit, page.Limit.ToString()),
                    (OpdsProtocol.Query.Search, searchQuery))));
        }

        if (page.HasNextPage) {
            feed.Add(Link(
                request,
                OpdsProtocol.LinkRelations.Next,
                selfPath,
                OpdsProtocol.ContentTypes.Atom,
                QueryPairs(
                    (OpdsProtocol.Query.Page, (page.Page + 1).ToString()),
                    (OpdsProtocol.Query.Limit, page.Limit.ToString()),
                    (OpdsProtocol.Query.Search, searchQuery))));
        }
    }

    private static void AddOpenSearchPaging<T>(XElement feed, OpdsCatalogPage<T> page) {
        feed.Add(new XElement(OpenSearch + "totalResults", page.TotalCount));
        feed.Add(new XElement(OpenSearch + "startIndex", page.TotalCount == 0 ? 0 : page.Offset() + 1));
        feed.Add(new XElement(OpenSearch + "itemsPerPage", page.Limit));
    }

    private static int Offset<T>(this OpdsCatalogPage<T> page) => (page.Page - 1) * page.Limit;

    private static string AbsoluteUrl(
        HttpRequest request,
        string path,
        IReadOnlyList<KeyValuePair<string, string?>>? query = null) {
        var pairs = new List<KeyValuePair<string, string?>>();
        if (query is not null) {
            pairs.AddRange(query.Where(pair => !string.IsNullOrWhiteSpace(pair.Value)));
        }

        if (request.Query[JellyfinProtocol.QueryKeys.ApiKeySnake].FirstOrDefault() is { Length: > 0 } snakeKey) {
            pairs.Add(new KeyValuePair<string, string?>(JellyfinProtocol.QueryKeys.ApiKeySnake, snakeKey));
        } else if (request.Query[JellyfinProtocol.QueryKeys.ApiKey].FirstOrDefault() is { Length: > 0 } apiKey) {
            pairs.Add(new KeyValuePair<string, string?>(JellyfinProtocol.QueryKeys.ApiKey, apiKey));
        }

        return UriHelper.BuildAbsolute(
            ForwardedScheme(request),
            ForwardedHost(request),
            request.PathBase,
            path,
            pairs.Count == 0 ? QueryString.Empty : QueryString.Create(pairs));
    }

    private static string ForwardedScheme(HttpRequest request) =>
        FirstForwardedValue(request.Headers["X-Forwarded-Proto"]) ?? request.Scheme;

    private static HostString ForwardedHost(HttpRequest request) =>
        HostString.FromUriComponent(FirstForwardedValue(request.Headers["X-Forwarded-Host"]) ?? request.Host.ToUriComponent());

    private static string? FirstForwardedValue(IEnumerable<string?> values) =>
        values
            .SelectMany(value => (value ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static IReadOnlyList<KeyValuePair<string, string?>> QueryPairs(params (string Key, string? Value)[] pairs) =>
        pairs
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))
            .ToArray();

    private static bool TryCreatePage(
        int? page,
        int? limit,
        out OpdsPageRequest request,
        out IResult error) {
        var resolvedPage = page ?? 1;
        var resolvedLimit = Math.Min(limit ?? DefaultLimit, MaxLimit);
        if (resolvedPage < 1 || resolvedLimit < 1) {
            request = new OpdsPageRequest(1, DefaultLimit);
            error = Results.BadRequest(new ApiProblem(ApiProblemCodes.InvalidOpdsRequest, "OPDS page and limit must be positive."));
            return false;
        }

        request = new OpdsPageRequest(resolvedPage, resolvedLimit);
        error = Results.Empty;
        return true;
    }

    private static bool ShouldHideNsfw(HttpContext httpContext) =>
        NsfwVisibility.ShouldHide(null, httpContext);

    private static IResult OpdsNotFound() =>
        Results.NotFound(new ApiProblem(ApiProblemCodes.EntityNotFound, "The requested OPDS resource was not found."));

    private static IResult Xml(XDocument document, string contentType) =>
        Results.Content(document.ToString(SaveOptions.DisableFormatting), contentType);

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O");

    private sealed record NavigationXmlEntry(
        string Id,
        string Title,
        string Summary,
        DateTimeOffset UpdatedAt,
        string Href,
        string Type,
        int? Count);
}
