using System.Collections.Concurrent;
using System.Globalization;
using System.Xml.Linq;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Newznab/Torznab XML API wire vocabulary. Referenced by the direct indexer clients; never retyped.</summary>
public static class NewznabProtocol {
    public const string ApiPath = "api";

    // ── query parameters ────────────────────────────────────────
    public const string TypeParam = "t";
    public const string TypeSearch = "search";
    public const string TypeCaps = "caps";
    public const string QueryParam = "q";
    public const string CategoriesParam = "cat";
    public const string ApiKeyParam = "apikey";
    public const string LimitParam = "limit";
    public const int DefaultLimit = 100;

    // ── XML vocabulary ──────────────────────────────────────────
    // prism-vocab: external — Newznab/Torznab feed element and attribute names, decoded only at this parse boundary.
    public static readonly XNamespace TorznabNs = "http://torznab.com/schemas/2015/feed";
    public static readonly XNamespace NewznabNs = "http://www.newznab.com/DTD/2010/feeds/attributes/";
    public const string Item = "item";
    public const string Title = "title";
    public const string Guid = "guid";
    public const string Comments = "comments";
    public const string Link = "link";
    public const string PubDate = "pubDate";
    public const string Size = "size";
    public const string Enclosure = "enclosure";
    public const string EnclosureUrl = "url";
    public const string EnclosureLength = "length";
    public const string Attr = "attr";
    public const string AttrName = "name";
    public const string AttrValue = "value";
    public const string AttrSeeders = "seeders";
    public const string AttrPeers = "peers";
    public const string AttrLeechers = "leechers";
    public const string AttrInfoHash = "infohash";
    public const string AttrMagnetUrl = "magneturl";
    public const string AttrSize = "size";
    public const string ErrorElement = "error";
    public const string ErrorCode = "code";
    public const string ErrorDescription = "description";

    // ── caps vocabulary ─────────────────────────────────────────
    public const string CapsLimits = "limits";
    public const string CapsLimitsMax = "max";
    public const string CapsCategories = "categories";
    public const string CapsCategory = "category";
    public const string CapsSubcategory = "subcat";
    public const string CapsCategoryId = "id";
}

/// <summary>An indexer's advertised capabilities from its <c>?t=caps</c> endpoint, cached per indexer.</summary>
/// <param name="Categories">Every advertised category and subcategory id; empty when caps couldn't be read.</param>
/// <param name="MaxLimit">The indexer's maximum page size, or null when unadvertised.</param>
public sealed record NewznabCapabilities(IReadOnlySet<int> Categories, int? MaxLimit) {
    public static NewznabCapabilities Unknown { get; } = new(new HashSet<int>(), null);
}

/// <summary>
/// Talks the Newznab/Torznab XML API to one indexer directly: <c>?t=caps</c> capability discovery
/// (cached seven days), <c>?t=search</c> text queries with the requested categories clamped to the
/// advertised set, and RSS item parsing including the torznab/newznab attribute extensions. Subclasses
/// pin the indexer family and the transfer protocol of the releases it serves.
/// </summary>
public abstract class NewznabIndexerClientBase(HttpClient http) : IIndexerSearchClient {
    private static readonly TimeSpan CapsTtl = TimeSpan.FromDays(7);
    private static readonly ConcurrentDictionary<Guid, (NewznabCapabilities Caps, DateTimeOffset FetchedAt)> CapsCache = new();

    public abstract IndexerKind Kind { get; }

    /// <summary>The transfer protocol of every release this indexer family serves.</summary>
    protected abstract DownloadProtocol Protocol { get; }

    public async Task<IReadOnlyList<IndexerRelease>> SearchAsync(IndexerConnection connection, IndexerQuery query, CancellationToken cancellationToken) {
        var caps = await CapsAsync(connection, cancellationToken);
        var categories = ClampCategories(query.Categories, caps);
        var limit = caps.MaxLimit is { } max ? Math.Min(max, NewznabProtocol.DefaultLimit) : NewznabProtocol.DefaultLimit;

        var parameters = new List<string> {
            $"{NewznabProtocol.TypeParam}={NewznabProtocol.TypeSearch}",
            $"{NewznabProtocol.QueryParam}={Uri.EscapeDataString(query.Text)}",
            $"{NewznabProtocol.LimitParam}={limit}"
        };
        if (categories.Count > 0) {
            parameters.Add($"{NewznabProtocol.CategoriesParam}={string.Join(',', categories)}");
        }

        var document = await FetchXmlAsync(connection, parameters, cancellationToken);
        return Parse(document);
    }

    public async Task<IndexerConnectionTest> TestAsync(IndexerConnection connection, CancellationToken cancellationToken) {
        try {
            var caps = await FetchCapsAsync(connection, cancellationToken);
            CapsCache[connection.Id] = (caps, DateTimeOffset.UtcNow);
            return new IndexerConnectionTest(true,
                caps.Categories.Count > 0
                    ? $"Connected; the indexer advertises {caps.Categories.Count} categories."
                    : "Connected, but the indexer advertised no categories — the configured ones are used as-is.");
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return new IndexerConnectionTest(false, ex.Message);
        }
    }

    /// <summary>
    /// The requested categories narrowed to those the indexer advertises. When the indexer advertises
    /// nothing recognizable — or none of the requested ids — the request passes through unchanged, so a
    /// caps endpoint that is broken or incomplete never silently empties a search.
    /// </summary>
    private static IReadOnlyList<int> ClampCategories(IReadOnlyList<int> requested, NewznabCapabilities caps) {
        if (requested.Count == 0 || caps.Categories.Count == 0) {
            return requested;
        }

        var supported = requested.Where(caps.Categories.Contains).ToArray();
        return supported.Length > 0 ? supported : requested;
    }

    private async Task<NewznabCapabilities> CapsAsync(IndexerConnection connection, CancellationToken cancellationToken) {
        if (CapsCache.TryGetValue(connection.Id, out var cached) && DateTimeOffset.UtcNow - cached.FetchedAt < CapsTtl) {
            return cached.Caps;
        }

        NewznabCapabilities caps;
        try {
            caps = await FetchCapsAsync(connection, cancellationToken);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception) {
            // A failing caps endpoint must not block searching; remember the miss for the TTL so every
            // search doesn't retry it.
            caps = NewznabCapabilities.Unknown;
        }

        CapsCache[connection.Id] = (caps, DateTimeOffset.UtcNow);
        return caps;
    }

    private async Task<NewznabCapabilities> FetchCapsAsync(IndexerConnection connection, CancellationToken cancellationToken) {
        var document = await FetchXmlAsync(connection, [$"{NewznabProtocol.TypeParam}={NewznabProtocol.TypeCaps}"], cancellationToken);
        var categories = new HashSet<int>();
        var categoriesElement = document.Root?.Element(NewznabProtocol.CapsCategories);
        foreach (var category in categoriesElement?.Elements(NewznabProtocol.CapsCategory) ?? []) {
            if (IntAttribute(category, NewznabProtocol.CapsCategoryId) is { } id) {
                categories.Add(id);
            }

            foreach (var subcategory in category.Elements(NewznabProtocol.CapsSubcategory)) {
                if (IntAttribute(subcategory, NewznabProtocol.CapsCategoryId) is { } subId) {
                    categories.Add(subId);
                }
            }
        }

        var maxLimit = document.Root?.Element(NewznabProtocol.CapsLimits) is { } limits
            ? IntAttribute(limits, NewznabProtocol.CapsLimitsMax)
            : null;
        return new NewznabCapabilities(categories, maxLimit);
    }

    private IReadOnlyList<IndexerRelease> Parse(XDocument document) {
        var releases = new List<IndexerRelease>();
        foreach (var item in document.Descendants(NewznabProtocol.Item)) {
            var title = item.Element(NewznabProtocol.Title)?.Value;
            if (string.IsNullOrWhiteSpace(title)) {
                continue;
            }

            var attrs = ExtensionAttributes(item);
            var enclosure = item.Element(NewznabProtocol.Enclosure);
            var downloadUrl = enclosure?.Attribute(NewznabProtocol.EnclosureUrl)?.Value
                ?? item.Element(NewznabProtocol.Link)?.Value;
            var size = LongElement(item, NewznabProtocol.Size)
                ?? LongValue(attrs.GetValueOrDefault(NewznabProtocol.AttrSize))
                ?? LongValue(enclosure?.Attribute(NewznabProtocol.EnclosureLength)?.Value)
                ?? 0;

            var seeders = IntValue(attrs.GetValueOrDefault(NewznabProtocol.AttrSeeders));
            // Torznab's "peers" attribute counts the whole swarm (seeders + leechers); an explicit
            // leechers attribute wins when present.
            var leechers = IntValue(attrs.GetValueOrDefault(NewznabProtocol.AttrLeechers))
                ?? (IntValue(attrs.GetValueOrDefault(NewznabProtocol.AttrPeers)) is { } peers && seeders is { } seeded
                    ? Math.Max(peers - seeded, 0)
                    : IntValue(attrs.GetValueOrDefault(NewznabProtocol.AttrPeers)));

            releases.Add(new IndexerRelease(
                title,
                size,
                seeders,
                leechers,
                Protocol,
                downloadUrl,
                attrs.GetValueOrDefault(NewznabProtocol.AttrMagnetUrl),
                attrs.GetValueOrDefault(NewznabProtocol.AttrInfoHash),
                item.Element(NewznabProtocol.Comments)?.Value ?? PermalinkGuid(item),
                Language: null,
                PublishedAt: ParsePubDate(item.Element(NewznabProtocol.PubDate)?.Value)));
        }

        return releases;
    }

    /// <summary>The torznab/newznab extension attributes of an item, last value winning per name.</summary>
    private static Dictionary<string, string> ExtensionAttributes(XElement item) {
        var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attr in item.Elements(NewznabProtocol.TorznabNs + NewznabProtocol.Attr)
                     .Concat(item.Elements(NewznabProtocol.NewznabNs + NewznabProtocol.Attr))) {
            var name = attr.Attribute(NewznabProtocol.AttrName)?.Value;
            var value = attr.Attribute(NewznabProtocol.AttrValue)?.Value;
            if (!string.IsNullOrEmpty(name) && value is not null) {
                attrs[name] = value;
            }
        }

        return attrs;
    }

    /// <summary>RSS pubDate is RFC 822: the <c>+0000</c> zone form (no colon) needs normalizing before parse.</summary>
    private static DateTimeOffset? ParsePubDate(string? text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return null;
        }

        var normalized = System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"([+-]\d{2})(\d{2})$", "$1:$2");
        return DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.None, out var published)
            ? published
            : null;
    }

    private static string? PermalinkGuid(XElement item) {
        var guid = item.Element(NewznabProtocol.Guid);
        return guid is not null && Uri.IsWellFormedUriString(guid.Value, UriKind.Absolute) ? guid.Value : null;
    }

    /// <summary>Fetches one API call and parses the XML, translating the Newznab in-band error element into an exception.</summary>
    private async Task<XDocument> FetchXmlAsync(IndexerConnection connection, IReadOnlyList<string> parameters, CancellationToken cancellationToken) {
        var query = new List<string>(parameters);
        if (!string.IsNullOrWhiteSpace(connection.ApiKey)) {
            query.Add($"{NewznabProtocol.ApiKeyParam}={Uri.EscapeDataString(connection.ApiKey)}");
        }

        var baseUrl = connection.BaseUrl.TrimEnd('/');
        // A base URL already pointing at /api (Jackett's per-indexer torznab paths end in /api) is used
        // as-is; otherwise the standard api path is appended.
        var endpoint = baseUrl.EndsWith($"/{NewznabProtocol.ApiPath}", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : $"{baseUrl}/{NewznabProtocol.ApiPath}";
        using var response = await http.GetAsync(new Uri($"{endpoint}?{string.Join('&', query)}"), cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        XDocument document;
        try {
            document = XDocument.Parse(body);
        } catch (System.Xml.XmlException) {
            var text = body.Trim();
            throw new InvalidOperationException(text.Length is > 0 and <= 200 ? text : "The indexer returned an unreadable (non-XML) response.");
        }

        if (document.Root is { } root && root.Name.LocalName == NewznabProtocol.ErrorElement) {
            var code = root.Attribute(NewznabProtocol.ErrorCode)?.Value;
            var description = root.Attribute(NewznabProtocol.ErrorDescription)?.Value ?? "request rejected";
            throw new InvalidOperationException($"Indexer error {code}: {description}");
        }

        return document;
    }

    private static int? IntAttribute(XElement element, string attribute) =>
        int.TryParse(element.Attribute(attribute)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static long? LongElement(XElement element, string name) =>
        long.TryParse(element.Element(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static long? LongValue(string? text) =>
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static int? IntValue(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
}

/// <summary>A torrent indexer speaking Torznab directly (native tracker endpoints, Jackett, per-indexer Prowlarr URLs).</summary>
public sealed class TorznabIndexerClient(HttpClient http) : NewznabIndexerClientBase(http) {
    public override IndexerKind Kind => IndexerKind.Torznab;
    protected override DownloadProtocol Protocol => DownloadProtocol.Torrent;
}

/// <summary>A usenet indexer speaking Newznab directly.</summary>
public sealed class NewznabIndexerClient(HttpClient http) : NewznabIndexerClientBase(http) {
    public override IndexerKind Kind => IndexerKind.Newznab;
    protected override DownloadProtocol Protocol => DownloadProtocol.Usenet;
}
