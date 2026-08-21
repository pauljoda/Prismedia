using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Prismedia.Application.Books;

/// <summary>One readable chapter offered to the automatic matcher.</summary>
/// <param name="Key">Stable chapter key (EPUB navigation target or chapter entity id).</param>
/// <param name="Title">Human-readable chapter title used for normalized-title matching.</param>
/// <param name="Order">Zero-based display order.</param>
public sealed record MatchableReadableChapter(string Key, string Title, int Order);

/// <summary>One audiobook track offered to the automatic matcher.</summary>
/// <param name="Id">Audio track entity identifier.</param>
/// <param name="Title">Track title used for normalized-title matching.</param>
/// <param name="SortOrder">The track's structural sort order under its Book.</param>
public sealed record MatchableAudioTrack(Guid Id, string Title, int SortOrder);

/// <summary>
/// Server-side port of the reference client's chapter matcher. Manual pairs always win; remaining
/// chapters match a track only through normalized-title equality. Numbers and sort order never
/// determine chapter identity, so an unmatched title deliberately stays unmatched.
/// </summary>
public static partial class BookChapterMatcher {
    [GeneratedRegex(@"^\s*(?:chapter|ch\.?|track|part)\s*[ivxlcdm]+\s*(?:[.\-–—:_]|\s)+", RegexOptions.IgnoreCase)]
    private static partial Regex RomanNumberedPrefix();

    [GeneratedRegex(@"^\s*(?:chapter|ch\.?|track|part)\s*0*\d+\s*(?:[.\-–—:_]|\s)*", RegexOptions.IgnoreCase)]
    private static partial Regex ArabicNumberedPrefix();

    [GeneratedRegex(@"^\s*0*\d+\s*(?:[.\-–—:_]|\s)+")]
    private static partial Regex BareNumberPrefix();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumericRuns();

    /// <summary>
    /// Stable comparison key for common EPUB/audio chapter labels: diacritics stripped, common
    /// "Chapter/Track/Part N" prefixes removed, punctuation collapsed to single spaces.
    /// </summary>
    public static string MatchKey(string value) {
        var decomposed = value.Normalize(NormalizationForm.FormKD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed) {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) {
                builder.Append(ch);
            }
        }

        var normalized = builder.ToString().ToLowerInvariant();
        normalized = RomanNumberedPrefix().Replace(normalized, string.Empty, 1);
        normalized = ArabicNumberedPrefix().Replace(normalized, string.Empty, 1);
        normalized = BareNumberPrefix().Replace(normalized, string.Empty, 1);
        return NonAlphanumericRuns().Replace(normalized, " ").Trim();
    }

    /// <summary>
    /// Computes the automatic chapter-to-track pairs left open by the manual map. Both sides are
    /// ordered exactly like the reference client (order, then title, then id) so the first
    /// unconsumed title match is deterministic.
    /// </summary>
    /// <param name="readableChapters">Every readable chapter of the book.</param>
    /// <param name="audioTracks">Every playable audiobook track owned by the book.</param>
    /// <param name="manualMappings">User-curated pairs; their chapters and tracks are consumed first.</param>
    /// <returns>Automatic (chapter key, track id) pairs, excluding everything manually claimed.</returns>
    public static IReadOnlyList<(string ChapterKey, Guid AudioTrackId)> ComputeAutoPairs(
        IReadOnlyList<MatchableReadableChapter> readableChapters,
        IReadOnlyList<MatchableAudioTrack> audioTracks,
        IReadOnlyList<(string ChapterKey, Guid AudioTrackId)> manualMappings) {
        var readable = readableChapters
            .OrderBy(chapter => chapter.Order)
            .ThenBy(chapter => chapter.Title, StringComparer.Ordinal)
            .ThenBy(chapter => chapter.Key, StringComparer.Ordinal)
            .ToArray();
        var tracks = audioTracks
            .OrderBy(track => track.SortOrder)
            .ThenBy(track => track.Title, StringComparer.Ordinal)
            .ThenBy(track => track.Id)
            .ToArray();

        var readableKeys = readable.Select(chapter => chapter.Key).ToHashSet(StringComparer.Ordinal);
        var consumedChapterKeys = new HashSet<string>(StringComparer.Ordinal);
        var consumedTrackIds = new HashSet<Guid>();
        var trackIds = tracks.Select(track => track.Id).ToHashSet();
        foreach (var (chapterKey, trackId) in manualMappings) {
            if (!readableKeys.Contains(chapterKey) || !trackIds.Contains(trackId)) {
                continue;
            }
            if (consumedChapterKeys.Contains(chapterKey) || consumedTrackIds.Contains(trackId)) {
                continue;
            }

            consumedChapterKeys.Add(chapterKey);
            consumedTrackIds.Add(trackId);
        }

        var trackKeys = tracks.Select(track => MatchKey(track.Title)).ToArray();
        var pairs = new List<(string ChapterKey, Guid AudioTrackId)>();
        foreach (var chapter in readable) {
            if (consumedChapterKeys.Contains(chapter.Key)) {
                continue;
            }

            var key = MatchKey(chapter.Title);
            if (key.Length == 0) {
                continue;
            }

            for (var index = 0; index < tracks.Length; index++) {
                if (consumedTrackIds.Contains(tracks[index].Id) || !string.Equals(trackKeys[index], key, StringComparison.Ordinal)) {
                    continue;
                }

                consumedTrackIds.Add(tracks[index].Id);
                consumedChapterKeys.Add(chapter.Key);
                pairs.Add((chapter.Key, tracks[index].Id));
                break;
            }
        }

        return pairs;
    }
}
