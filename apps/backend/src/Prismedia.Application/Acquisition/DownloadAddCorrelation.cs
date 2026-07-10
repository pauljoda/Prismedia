namespace Prismedia.Application.Acquisition;

/// <summary>
/// Canonical, bounded correlation persisted before a remote Add. A known torrent hash resolves directly;
/// otherwise a normalized release/file title can recover exactly one same-category client item after a
/// lost response. Multiple title matches are deliberately ambiguous and cleanup fails closed.
/// </summary>
public static class DownloadAddCorrelation {
    /// <summary>Maximum persisted correlation length, aligned with download_transfers.client_item_id.</summary>
    public const int MaxLength = 256;

    /// <summary>Creates the persisted hash-or-title correlation.</summary>
    public static string Create(string? infoHash, string title) {
        var value = string.IsNullOrWhiteSpace(infoHash) ? title.Trim() : infoHash.Trim().ToLowerInvariant();
        return value.Length <= MaxLength ? value : value[..MaxLength];
    }

    /// <summary>Whether a client item's display name confidently matches a title correlation.</summary>
    public static bool MatchesName(string correlation, string? itemName) =>
        !string.IsNullOrWhiteSpace(itemName)
        && Normalize(correlation) is { Length: > 0 } expected
        && string.Equals(expected, Normalize(itemName), StringComparison.Ordinal);

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
