using System.Net;
using System.Text.RegularExpressions;
using Prismedia.Application.Acquisition;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// Resolves a usable magnet link from an indexer release's info page. Meta-search indexers (e.g. Torrentz2)
/// surface only a web page through Prowlarr, but that page embeds the actual magnet — so we fetch it and
/// extract the first <c>magnet:?xt=urn:btih:</c> link (HTML-entity decoded). This is the automatic counterpart
/// to the manual .torrent upload fallback.
/// </summary>
public sealed partial class ReleaseLinkResolver(HttpClient http) : IReleaseLinkResolver {
    [GeneratedRegex(@"magnet:\?xt=urn:btih:[A-Za-z0-9]{32,40}[^""'<>\s]*", RegexOptions.IgnoreCase)]
    private static partial Regex MagnetRegex();

    public async Task<string?> ResolveMagnetAsync(string infoUrl, CancellationToken cancellationToken) {
        if (!Uri.TryCreate(infoUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) {
            return null;
        }

        try {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            // Some torrent sites block default user agents.
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (compatible; Prismedia)");
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) {
                return null;
            }

            var page = await response.Content.ReadAsStringAsync(cancellationToken);
            var decoded = WebUtility.HtmlDecode(page);
            var match = MagnetRegex().Match(decoded);
            return match.Success ? match.Value : null;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return null;
        }
    }
}
