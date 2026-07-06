using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prismedia.Application.Opds;
using Prismedia.Contracts.Jellyfin;
using Prismedia.Contracts.Media;
using Prismedia.Contracts.Opds;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Tests;

public sealed class OpdsEndpointTests : IDisposable {
    private static readonly Guid VisibleBookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid HiddenBookId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PdfBookId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid CbzBookId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid FolderComicId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"prismedia-opds-{Guid.NewGuid():N}");

    public OpdsEndpointTests() {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task OpdsRootRequiresAuthentication() {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(OpdsProtocol.Prefix);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(OpdsProtocol.BasicChallenge, response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task OpdsRootAcceptsApiKeyAndReturnsNavigationFeed() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync(OpdsProtocol.Prefix);
        var document = await ReadXmlAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/atom+xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(response.Content.Headers.ContentType?.Parameters ?? [], parameter =>
            parameter.Name?.Equals("profile", StringComparison.OrdinalIgnoreCase) == true &&
            parameter.Value?.Contains("opds-catalog", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Equal("feed", document.Root?.Name.LocalName);
        Assert.Contains(document.Descendants(Atom("link")), link =>
            (string?)link.Attribute("rel") == OpdsProtocol.LinkRelations.Search);
        Assert.Contains(document.Descendants(Atom("entry")), entry =>
            entry.Element(Atom("title"))?.Value == "Recently Added");
    }

    [Fact]
    public async Task OpdsBasicAuthWorksAndInvalidCredentialsFail() {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var goodRequest = new HttpRequestMessage(HttpMethod.Get, OpdsProtocol.Prefix);
        goodRequest.Headers.Authorization = Basic(TestAuth.Password);
        using var goodResponse = await client.SendAsync(goodRequest);

        using var badRequest = new HttpRequestMessage(HttpMethod.Get, OpdsProtocol.Prefix);
        badRequest.Headers.Authorization = Basic("wrong-key");
        using var badResponse = await client.SendAsync(badRequest);

        Assert.Equal(HttpStatusCode.OK, goodResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, badResponse.StatusCode);
    }

    [Fact]
    public async Task OpdsAcceptsBearerJellyfinSessionTokens() {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var auth = await AuthenticateAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Get, OpdsProtocol.Prefix);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OpenSearchDescriptorKeepsTemplatePlaceholderAndQueryAuth() {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"{OpdsProtocol.Routes.OpenSearch}?api_key={TestAuth.Token}");
        var document = await ReadXmlAsync(response);
        var url = Assert.Single(document.Descendants(XName.Get("Url", OpdsProtocol.OpenSearchNamespace)));
        var template = (string?)url.Attribute("template");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OpdsProtocol.ContentTypes.OpenSearch, response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("api_key=", template);
        Assert.Contains("q={searchTerms}", template);
        Assert.DoesNotContain("%7BsearchTerms%7D", template);
    }

    [Fact]
    public async Task AcquisitionFeedIncludesBookLinksMimeTypesAndEscapedXml() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync(OpdsProtocol.Routes.Recent);
        var document = await ReadXmlAsync(response);
        var entries = document.Descendants(Atom("entry")).ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(entries, entry => entry.Element(Atom("title"))?.Value == "Visible & <Book>");
        Assert.Contains(entries.SelectMany(entry => entry.Elements(Atom("link"))), link =>
            (string?)link.Attribute("rel") == OpdsProtocol.LinkRelations.Acquisition &&
            (string?)link.Attribute("type") == MediaContentTypes.Epub);
        Assert.Contains(entries.SelectMany(entry => entry.Elements(Atom("link"))), link =>
            (string?)link.Attribute("rel") == OpdsProtocol.LinkRelations.Acquisition &&
            (string?)link.Attribute("type") == MediaContentTypes.Pdf);
        Assert.Contains(entries.SelectMany(entry => entry.Elements(Atom("link"))), link =>
            (string?)link.Attribute("rel") == OpdsProtocol.LinkRelations.Acquisition &&
            (string?)link.Attribute("type") == MediaContentTypes.ComicBookZip);
        Assert.Contains(entries.SelectMany(entry => entry.Elements(Atom("link"))), link =>
            (string?)link.Attribute("rel") == OpdsProtocol.LinkRelations.Image);
    }

    [Fact]
    public async Task OpdsLinksUseForwardedOriginForProxyRequests() {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, OpdsProtocol.Routes.Recent);
        request.Headers.Authorization = Basic(TestAuth.Password);
        request.Headers.Host = "127.0.0.1:8008";
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-Host", "prismedia.example.com");

        using var response = await client.SendAsync(request);
        var document = await ReadXmlAsync(response);
        var hrefs = document.Descendants(Atom("link"))
            .Select(link => (string?)link.Attribute("href"))
            .Where(href => href is not null)
            .Cast<string>()
            .ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(hrefs);
        Assert.All(hrefs, href => Assert.StartsWith("https://prismedia.example.com/", href, StringComparison.Ordinal));
        Assert.Contains(hrefs, href => href.Contains("/cover", StringComparison.Ordinal));
        Assert.Contains(hrefs, href => href.Contains("/download", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DirectCoverAndDownloadLinksRequireAuthorizationAndStreamWhenAuthorized() {
        using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();
        using var authenticated = factory.CreateAuthenticatedClient();

        using var anonymousDownload = await anonymous.GetAsync(OpdsProtocol.Routes.BookDownload(VisibleBookId));
        using var anonymousCover = await anonymous.GetAsync(OpdsProtocol.Routes.BookCover(VisibleBookId));
        using var download = await authenticated.GetAsync(OpdsProtocol.Routes.BookDownload(VisibleBookId));
        using var cover = await authenticated.GetAsync(OpdsProtocol.Routes.BookCover(VisibleBookId));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousDownload.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousCover.StatusCode);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(MediaContentTypes.Epub, download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("visible book", await download.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, cover.StatusCode);
        Assert.Equal(MediaContentTypes.ImageJpeg, cover.Content.Headers.ContentType?.MediaType);
        Assert.Equal("cover", await cover.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DirectoryComicDownloadStreamsVirtualCbz() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync(OpdsProtocol.Routes.BookDownload(FolderComicId));
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaContentTypes.ComicBookZip, response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("folder-comic.cbz", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Contains(archive.Entries, entry => entry.FullName == "001.jpg");
        Assert.Contains(archive.Entries, entry => entry.FullName == "chapter/002.png");
    }

    [Fact]
    public async Task SfwProfileCannotSeeNsfwBooksOrDirectLinks() {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{OpdsProtocol.Routes.Search}?q=Hidden");
        request.Headers.Authorization = Basic(TestAuth.Password);

        using var response = await client.SendAsync(request);
        var document = await ReadXmlAsync(response);
        using var hiddenDetailRequest = new HttpRequestMessage(HttpMethod.Get, OpdsProtocol.Routes.Book(HiddenBookId));
        hiddenDetailRequest.Headers.Authorization = Basic(TestAuth.Password);
        using var hiddenDetail = await client.SendAsync(hiddenDetailRequest);
        using var hiddenCoverRequest = new HttpRequestMessage(HttpMethod.Get, OpdsProtocol.Routes.BookCover(HiddenBookId));
        hiddenCoverRequest.Headers.Authorization = Basic(TestAuth.Password);
        using var hiddenCover = await client.SendAsync(hiddenCoverRequest);

        var resultTitles = document.Descendants(Atom("entry"))
            .Select(entry => entry.Element(Atom("title"))?.Value)
            .Where(title => title is not null)
            .ToArray();

        Assert.DoesNotContain(resultTitles, title => title!.Contains("Hidden", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.NotFound, hiddenDetail.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, hiddenCover.StatusCode);
    }

    [Fact]
    public async Task NsfwAllowedProfileCanSeeNsfwBooks() {
        using var factory = CreateFactory(allowNsfw: true);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{OpdsProtocol.Routes.Search}?q=Hidden");
        request.Headers.Authorization = Basic(TestAuth.Password);

        using var response = await client.SendAsync(request);
        var document = await ReadXmlAsync(response);

        Assert.Contains(document.Descendants(Atom("title")), title => title.Value == "Hidden Book");
    }

    [Fact]
    public async Task PaginationProducesNextAndPreviousLinks() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        var firstPage = await ReadXmlAsync(await client.GetAsync($"{OpdsProtocol.Routes.Recent}?page=1&limit=1"));
        var secondPage = await ReadXmlAsync(await client.GetAsync($"{OpdsProtocol.Routes.Recent}?page=2&limit=1"));

        Assert.Contains(firstPage.Descendants(Atom("link")), link =>
            (string?)link.Attribute("rel") == OpdsProtocol.LinkRelations.Next &&
            ((string?)link.Attribute("href"))?.Contains("page=2", StringComparison.Ordinal) == true);
        Assert.Contains(secondPage.Descendants(Atom("link")), link =>
            (string?)link.Attribute("rel") == OpdsProtocol.LinkRelations.Previous &&
            ((string?)link.Attribute("href"))?.Contains("page=1", StringComparison.Ordinal) == true);
    }

    public void Dispose() {
        if (Directory.Exists(_tempDir)) {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private WebApplicationFactory<Program> CreateFactory(bool allowNsfw = false) {
        var service = new FakeOpdsCatalogService(_tempDir);
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.RemoveAll<IOpdsCatalogService>();
                    services.AddSingleton<IOpdsCatalogService>(service);
                });
            })
            .WithTestAuth(allowNsfw: allowNsfw);
    }

    private static async Task<XDocument> ReadXmlAsync(HttpResponseMessage response) {
        var xml = await response.Content.ReadAsStringAsync();
        return XDocument.Parse(xml);
    }

    private static AuthenticationHeaderValue Basic(string password) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"Prismedia:{password}")));

    private static async Task<JellyfinAuthenticationResult> AuthenticateAsync(HttpClient client) {
        using var response = await client.PostAsJsonAsync(
            "/Users/AuthenticateByName",
            new JellyfinAuthenticateByNameRequest {
                Username = "Prismedia",
                Password = TestAuth.Password
            });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JellyfinAuthenticationResult>();
        Assert.NotNull(result);
        return result;
    }

    private static XName Atom(string name) => XName.Get(name, OpdsProtocol.AtomNamespace);

    private sealed class FakeOpdsCatalogService : IOpdsCatalogService {
        private readonly IReadOnlyList<OpdsBookEntry> _books;
        private readonly IReadOnlyDictionary<Guid, bool> _nsfw;
        private readonly IReadOnlyDictionary<Guid, OpdsFileContent> _downloads;
        private readonly IReadOnlyDictionary<Guid, OpdsFileContent> _covers;

        public FakeOpdsCatalogService(string tempDir) {
            var visiblePath = Path.Combine(tempDir, "visible.epub");
            var hiddenPath = Path.Combine(tempDir, "hidden.epub");
            var pdfPath = Path.Combine(tempDir, "book.pdf");
            var cbzPath = Path.Combine(tempDir, "comic.cbz");
            var folderComicPath = Path.Combine(tempDir, "folder-comic");
            var coverPath = Path.Combine(tempDir, "cover.jpg");
            Directory.CreateDirectory(Path.Combine(folderComicPath, "chapter"));
            File.WriteAllText(visiblePath, "visible book");
            File.WriteAllText(hiddenPath, "hidden book");
            File.WriteAllText(pdfPath, "pdf book");
            File.WriteAllText(cbzPath, "cbz book");
            File.WriteAllText(Path.Combine(folderComicPath, "001.jpg"), "page 1");
            File.WriteAllText(Path.Combine(folderComicPath, "chapter", "002.png"), "page 2");
            File.WriteAllText(coverPath, "cover");

            _books = [
                Book(VisibleBookId, "Visible & <Book>", "Summary & <safe>", MediaContentTypes.Epub, cover: true),
                Book(PdfBookId, "PDF Book", null, MediaContentTypes.Pdf, cover: false),
                Book(CbzBookId, "Comic Book", null, MediaContentTypes.ComicBookZip, cover: false),
                Book(FolderComicId, "Folder Comic", null, MediaContentTypes.ComicBookZip, cover: false),
                Book(HiddenBookId, "Hidden Book", null, MediaContentTypes.Epub, cover: true)
            ];
            _nsfw = new Dictionary<Guid, bool> {
                [VisibleBookId] = false,
                [PdfBookId] = false,
                [CbzBookId] = false,
                [FolderComicId] = false,
                [HiddenBookId] = true
            };
            _downloads = new Dictionary<Guid, OpdsFileContent> {
                [VisibleBookId] = new(VisibleBookId, EntityFileRole.Source, visiblePath, MediaContentTypes.Epub, "visible.epub"),
                [HiddenBookId] = new(HiddenBookId, EntityFileRole.Source, hiddenPath, MediaContentTypes.Epub, "hidden.epub"),
                [PdfBookId] = new(PdfBookId, EntityFileRole.Source, pdfPath, MediaContentTypes.Pdf, "book.pdf"),
                [CbzBookId] = new(CbzBookId, EntityFileRole.Source, cbzPath, MediaContentTypes.ComicBookZip, "comic.cbz"),
                [FolderComicId] = new(FolderComicId, EntityFileRole.Source, folderComicPath, MediaContentTypes.ComicBookZip, "folder-comic.cbz")
            };
            _covers = new Dictionary<Guid, OpdsFileContent> {
                [VisibleBookId] = new(VisibleBookId, EntityFileRole.Cover, coverPath, MediaContentTypes.ImageJpeg, "cover.jpg"),
                [HiddenBookId] = new(HiddenBookId, EntityFileRole.Cover, coverPath, MediaContentTypes.ImageJpeg, "cover.jpg")
            };
        }

        public Task<int> CountVisibleBooksAsync(bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult(Visible(hideNsfw).Count);

        public Task<OpdsCatalogPage<OpdsBookEntry>> ListRecentAsync(bool hideNsfw, OpdsPageRequest page, CancellationToken cancellationToken) =>
            Task.FromResult(Page(Visible(hideNsfw), page));

        public Task<OpdsCatalogPage<OpdsNavigationEntry>> ListLibrariesAsync(bool hideNsfw, OpdsPageRequest page, CancellationToken cancellationToken) =>
            Task.FromResult(NavigationPage("Books", page, hideNsfw));

        public Task<OpdsCatalogPage<OpdsBookEntry>?> ListLibraryBooksAsync(Guid libraryId, bool hideNsfw, OpdsPageRequest page, CancellationToken cancellationToken) =>
            Task.FromResult<OpdsCatalogPage<OpdsBookEntry>?>(Page(Visible(hideNsfw), page));

        public Task<OpdsCatalogPage<OpdsNavigationEntry>> ListAuthorsAsync(bool hideNsfw, OpdsPageRequest page, CancellationToken cancellationToken) =>
            Task.FromResult(NavigationPage("Author", page, hideNsfw));

        public Task<OpdsCatalogPage<OpdsBookEntry>?> ListAuthorBooksAsync(Guid authorId, bool hideNsfw, OpdsPageRequest page, CancellationToken cancellationToken) =>
            Task.FromResult<OpdsCatalogPage<OpdsBookEntry>?>(Page(Visible(hideNsfw), page));

        public Task<OpdsCatalogPage<OpdsNavigationEntry>> ListSeriesAsync(bool hideNsfw, OpdsPageRequest page, CancellationToken cancellationToken) =>
            Task.FromResult(NavigationPage("Series", page, hideNsfw));

        public Task<OpdsCatalogPage<OpdsBookEntry>?> ListSeriesBooksAsync(Guid seriesId, bool hideNsfw, OpdsPageRequest page, CancellationToken cancellationToken) =>
            Task.FromResult<OpdsCatalogPage<OpdsBookEntry>?>(Page(Visible(hideNsfw), page));

        public Task<OpdsCatalogPage<OpdsNavigationEntry>> ListCollectionsAsync(bool hideNsfw, OpdsPageRequest page, CancellationToken cancellationToken) =>
            Task.FromResult(NavigationPage("Collection", page, hideNsfw));

        public Task<OpdsCatalogPage<OpdsBookEntry>?> ListCollectionBooksAsync(Guid collectionId, bool hideNsfw, OpdsPageRequest page, CancellationToken cancellationToken) =>
            Task.FromResult<OpdsCatalogPage<OpdsBookEntry>?>(Page(Visible(hideNsfw), page));

        public Task<OpdsCatalogPage<OpdsNavigationEntry>> ListTagsAsync(bool hideNsfw, OpdsPageRequest page, CancellationToken cancellationToken) =>
            Task.FromResult(NavigationPage("Tag", page, hideNsfw));

        public Task<OpdsCatalogPage<OpdsBookEntry>?> ListTagBooksAsync(Guid tagId, bool hideNsfw, OpdsPageRequest page, CancellationToken cancellationToken) =>
            Task.FromResult<OpdsCatalogPage<OpdsBookEntry>?>(Page(Visible(hideNsfw), page));

        public Task<OpdsCatalogPage<OpdsBookEntry>> SearchBooksAsync(string? query, bool hideNsfw, OpdsPageRequest page, CancellationToken cancellationToken) {
            var items = Visible(hideNsfw)
                .Where(book => string.IsNullOrWhiteSpace(query) || book.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return Task.FromResult(Page(items, page));
        }

        public Task<OpdsBookEntry?> GetBookAsync(Guid bookId, bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult(Visible(hideNsfw).FirstOrDefault(book => book.Id == bookId));

        public Task<OpdsFileContent?> GetBookDownloadAsync(Guid bookId, bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult(IsVisible(bookId, hideNsfw) ? _downloads.GetValueOrDefault(bookId) : null);

        public Task<OpdsFileContent?> GetBookCoverAsync(Guid bookId, bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult(IsVisible(bookId, hideNsfw) ? _covers.GetValueOrDefault(bookId) : null);

        private IReadOnlyList<OpdsBookEntry> Visible(bool hideNsfw) =>
            _books.Where(book => !_nsfw[book.Id] || !hideNsfw).ToArray();

        private bool IsVisible(Guid bookId, bool hideNsfw) =>
            _nsfw.TryGetValue(bookId, out var isNsfw) && (!isNsfw || !hideNsfw);

        private static OpdsCatalogPage<OpdsBookEntry> Page(IReadOnlyList<OpdsBookEntry> items, OpdsPageRequest page) =>
            new(items.Skip(page.Offset).Take(page.Limit).ToArray(), items.Count, page.Page, page.Limit);

        private OpdsCatalogPage<OpdsNavigationEntry> NavigationPage(string title, OpdsPageRequest page, bool hideNsfw) {
            var entries = new[] {
                new OpdsNavigationEntry(Guid.NewGuid(), title, Visible(hideNsfw).Count, DateTimeOffset.UtcNow)
            };
            return new OpdsCatalogPage<OpdsNavigationEntry>(
                entries.Skip(page.Offset).Take(page.Limit).ToArray(),
                entries.Length,
                page.Page,
                page.Limit);
        }

        private static OpdsBookEntry Book(Guid id, string title, string? summary, string mime, bool cover) =>
            new(
                id,
                title,
                summary,
                DateTimeOffset.UtcNow.AddMinutes(-(id.ToString()[0] % 4)),
                DateTimeOffset.UtcNow,
                BookType.Novel,
                mime == MediaContentTypes.Pdf ? BookFormat.Pdf : mime == MediaContentTypes.ComicBookZip ? BookFormat.ImageArchive : BookFormat.Epub,
                null,
                null,
                [new OpdsContributor(Guid.NewGuid(), "Author & Person")],
                [new OpdsCategory(Guid.NewGuid(), "Tag & Category")],
                mime,
                10,
                cover ? MediaContentTypes.ImageJpeg : null,
                cover ? MediaContentTypes.ImageJpeg : null);
    }
}
