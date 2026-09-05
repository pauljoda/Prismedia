using System.Text.Json;
using Prismedia.Application.Acquisition;

namespace Prismedia.Infrastructure.Acquisition;

public sealed partial class SabnzbdDownloadClient {
    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetCompletedDirectoriesAsync(DownloadClientConnection connection, CancellationToken cancellationToken) {
        var response = await GetAsync(connection, SabnzbdProtocol.ModeStatus,
            new Dictionary<string, string> { [SabnzbdProtocol.SkipDashboardParam] = "1" }, cancellationToken);
        if (!response.TryGetProperty(SabnzbdProtocol.Status, out var status)
            || Text(status, SabnzbdProtocol.CompleteDirectory) is not { Length: > 0 } completed) {
            throw new IOException("SABnzbd did not report its resolved completed directory; cleanup was deferred.");
        }
        var configuration = await GetAsync(connection, SabnzbdProtocol.ModeGetConfig,
            new Dictionary<string, string> { [SabnzbdProtocol.SectionParam] = SabnzbdProtocol.Categories }, cancellationToken);
        if (!configuration.TryGetProperty(SabnzbdProtocol.Config, out var config)
            || !config.TryGetProperty(SabnzbdProtocol.Categories, out var categories)
            || categories.ValueKind != JsonValueKind.Array) {
            throw new IOException("SABnzbd did not report its category directories; cleanup was deferred.");
        }
        var categoryName = string.IsNullOrWhiteSpace(connection.Category) ? SabnzbdProtocol.DefaultCategory : connection.Category;
        foreach (var category in categories.EnumerateArray()) {
            if (!string.Equals(Text(category, SabnzbdProtocol.HistoryName), categoryName, StringComparison.OrdinalIgnoreCase)) continue;
            var directory = (Text(category, SabnzbdProtocol.CategoryDirectory) ?? string.Empty).TrimEnd('*');
            if (string.IsNullOrWhiteSpace(directory)) return [completed];
            var rooted = directory[0] is '/' or '\\' || directory.Length > 2 && directory[1] == ':' && directory[2] is '/' or '\\';
            return [rooted ? directory : completed.TrimEnd('/', '\\') + "/" + directory.Replace('\\', '/')];
        }
        throw new IOException("The recorded SABnzbd category is unavailable; cleanup was deferred.");
    }
}
