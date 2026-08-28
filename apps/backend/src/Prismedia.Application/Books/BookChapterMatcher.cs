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

/// <summary>One addressable audiobook chapter backed by a whole track or embedded marker.</summary>
public sealed record MatchableAudioChapter(
    Guid AudioTrackId,
    Guid? AudioMarkerId,
    string Title,
    int TrackSortOrder,
    int MarkerOrder,
    double StartSeconds,
    double? EndSeconds);

/// <summary>One automatic association between readable content and an audio time window.</summary>
public sealed record MatchedBookAudioChapter(
    string ChapterKey,
    Guid AudioTrackId,
    Guid? AudioMarkerId,
    double StartSeconds,
    double? EndSeconds);

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
        return ComputeAutoChapterPairs(
                readableChapters,
                audioTracks.Select(track => new MatchableAudioChapter(
                    track.Id,
                    null,
                    track.Title,
                    track.SortOrder,
                    0,
                    0,
                    null)).ToArray(),
                manualMappings.Select(mapping => (mapping.ChapterKey, mapping.AudioTrackId, (Guid?)null)).ToArray())
            .Select(pair => (pair.ChapterKey, pair.AudioTrackId))
            .ToArray();
    }

    /// <summary>
    /// Computes automatic readable-to-audio chapter pairs. Exact normalized titles win. When the
    /// source provides a complete embedded marker set with exactly one marker per readable chapter,
    /// remaining unmatched markers may align by ordinal; ordinary audio files are never guessed.
    /// </summary>
    public static IReadOnlyList<MatchedBookAudioChapter> ComputeAutoChapterPairs(
        IReadOnlyList<MatchableReadableChapter> readableChapters,
        IReadOnlyList<MatchableAudioChapter> audioChapters,
        IReadOnlyList<(string ChapterKey, Guid AudioTrackId, Guid? AudioMarkerId)> manualMappings) {
        var readable = readableChapters
            .OrderBy(chapter => chapter.Order)
            .ThenBy(chapter => chapter.Title, StringComparer.Ordinal)
            .ThenBy(chapter => chapter.Key, StringComparer.Ordinal)
            .ToArray();
        var chapters = audioChapters
            .OrderBy(chapter => chapter.TrackSortOrder)
            .ThenBy(chapter => chapter.MarkerOrder)
            .ThenBy(chapter => chapter.Title, StringComparer.Ordinal)
            .ThenBy(chapter => chapter.AudioTrackId)
            .ThenBy(chapter => chapter.AudioMarkerId)
            .ToArray();

        var readableKeys = readable.Select(chapter => chapter.Key).ToHashSet(StringComparer.Ordinal);
        var consumedChapterKeys = new HashSet<string>(StringComparer.Ordinal);
        var consumedAudioChapters = new HashSet<(Guid TrackId, Guid? MarkerId)>();
        var audioChapterIds = chapters
            .Select(chapter => (chapter.AudioTrackId, chapter.AudioMarkerId))
            .ToHashSet();
        foreach (var (chapterKey, trackId, markerId) in manualMappings) {
            var audioChapterId = (trackId, markerId);
            if (!readableKeys.Contains(chapterKey) || !audioChapterIds.Contains(audioChapterId)) {
                continue;
            }
            if (consumedChapterKeys.Contains(chapterKey) || consumedAudioChapters.Contains(audioChapterId)) {
                continue;
            }

            consumedChapterKeys.Add(chapterKey);
            consumedAudioChapters.Add(audioChapterId);
        }

        var audioKeys = chapters.Select(chapter => MatchKey(chapter.Title)).ToArray();
        var pairs = new List<MatchedBookAudioChapter>();
        foreach (var chapter in readable) {
            if (consumedChapterKeys.Contains(chapter.Key)) {
                continue;
            }

            var key = MatchKey(chapter.Title);
            if (key.Length == 0) {
                continue;
            }

            for (var index = 0; index < chapters.Length; index++) {
                var audioChapter = chapters[index];
                var audioChapterId = (audioChapter.AudioTrackId, audioChapter.AudioMarkerId);
                if (consumedAudioChapters.Contains(audioChapterId) ||
                    !string.Equals(audioKeys[index], key, StringComparison.Ordinal)) {
                    continue;
                }

                consumedAudioChapters.Add(audioChapterId);
                consumedChapterKeys.Add(chapter.Key);
                pairs.Add(new MatchedBookAudioChapter(
                    chapter.Key,
                    audioChapter.AudioTrackId,
                    audioChapter.AudioMarkerId,
                    audioChapter.StartSeconds,
                    audioChapter.EndSeconds));
                break;
            }
        }

        var isCompleteEmbeddedChapterSet = chapters.Length == readable.Length &&
            chapters.All(chapter => chapter.AudioMarkerId is not null);
        if (isCompleteEmbeddedChapterSet) {
            var unmatchedReadable = readable
                .Where(chapter => !consumedChapterKeys.Contains(chapter.Key))
                .ToArray();
            var unmatchedAudio = chapters
                .Where(chapter => !consumedAudioChapters.Contains((chapter.AudioTrackId, chapter.AudioMarkerId)))
                .ToArray();
            if (unmatchedReadable.Length == unmatchedAudio.Length) {
                for (var index = 0; index < unmatchedReadable.Length; index++) {
                    var readableChapter = unmatchedReadable[index];
                    var audioChapter = unmatchedAudio[index];
                    pairs.Add(new MatchedBookAudioChapter(
                        readableChapter.Key,
                        audioChapter.AudioTrackId,
                        audioChapter.AudioMarkerId,
                        audioChapter.StartSeconds,
                        audioChapter.EndSeconds));
                }
            }
        }

        return pairs
            .OrderBy(pair => Array.FindIndex(readable, chapter => chapter.Key == pair.ChapterKey))
            .ToArray();
    }
}
