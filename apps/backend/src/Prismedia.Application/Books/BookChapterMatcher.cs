using System.Globalization;
using System.Text;

namespace Prismedia.Application.Books;

/// <summary>One readable chapter offered to the automatic matcher.</summary>
/// <param name="Key">Stable chapter key (EPUB navigation target or chapter entity id).</param>
/// <param name="Title">Human-readable chapter title used for exact-title matching.</param>
/// <param name="Order">Zero-based display order.</param>
public sealed record MatchableReadableChapter(string Key, string Title, int Order);

/// <summary>One addressable audiobook chapter backed by a whole track or embedded marker.</summary>
/// <param name="AudioTrackId">Physical playable audio track.</param>
/// <param name="AudioMarkerId">Embedded marker, or null for the whole track.</param>
/// <param name="Title">Chapter label shown to listeners (a file name or placeholder when nothing better exists).</param>
/// <param name="IdentifyingTitle">
/// The title that can prove which chapter this is: an embedded chapter's own title or a whole file's
/// title tag. Null for untitled chapters and file-name titles, which never match.
/// </param>
/// <param name="TrackSortOrder">Zero-based playback position of the track in the Book.</param>
/// <param name="MarkerOrder">Zero-based position of the window inside its track.</param>
/// <param name="StartSeconds">Window start inside the track.</param>
/// <param name="EndSeconds">Window end inside the track, when known.</param>
public sealed record MatchableAudioChapter(
    Guid AudioTrackId,
    Guid? AudioMarkerId,
    string Title,
    string? IdentifyingTitle,
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
/// Derives automatic readable-to-audio chapter pairs from exact evidence only. Person-confirmed pairs
/// (hand-picked or reviewed in-order fills) always win. Remaining chapters pair only when their titles
/// are equal after case, accent, punctuation, and whitespace normalisation with every number kept, when
/// that title is unique on both sides, and when the pair keeps reading order on both sides. Nothing is
/// ever paired by position, file name, or an invented placeholder title.
/// </summary>
public static class BookChapterMatcher {
    #region Static Variables

    /// <summary>
    /// Version of the pairing rules. It is stamped into every persisted mapping signature, so raising it
    /// makes every Book's automatic pairs recompute once and marks older automatic pairs as untrusted
    /// until they do.
    /// </summary>
    public const int Version = 2;

    private static readonly string SignaturePrefix = $"m{Version}:";

    #endregion

    #region Actions - Titles

    /// <summary>
    /// Comparison key for a chapter title: compatibility-decomposed, accents removed, lower-cased, with
    /// every run of characters that are not letters or digits collapsed to one space. Numbers and words
    /// are kept exactly, so "Chapter 3" matches "chapter 3" but never "Track 17" or "Chapter 4".
    /// </summary>
    public static string MatchKey(string value) {
        var decomposed = value.Normalize(NormalizationForm.FormKD);
        var builder = new StringBuilder(decomposed.Length);
        var separated = false;
        foreach (var character in decomposed) {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) {
                continue;
            }
            if (!char.IsLetterOrDigit(character)) {
                separated = true;
                continue;
            }
            if (separated && builder.Length > 0) {
                builder.Append(' ');
            }
            separated = false;
            builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }

    #endregion

    #region Actions - Pairing

    /// <summary>
    /// Computes the automatic readable-to-audio chapter pairs left open by the confirmed map.
    /// </summary>
    /// <param name="readableChapters">Every readable chapter of the Book.</param>
    /// <param name="audioChapters">Every addressable audio chapter of the Book.</param>
    /// <param name="confirmedMappings">Person-confirmed pairs; their chapters are consumed first.</param>
    /// <returns>
    /// Automatic pairs in reading order: exact unique titles on both sides that cross no other pair.
    /// </returns>
    public static IReadOnlyList<MatchedBookAudioChapter> ComputeAutoChapterPairs(
        IReadOnlyList<MatchableReadableChapter> readableChapters,
        IReadOnlyList<MatchableAudioChapter> audioChapters,
        IReadOnlyList<(string ChapterKey, Guid AudioTrackId, Guid? AudioMarkerId)> confirmedMappings) {
        var readable = readableChapters
            .OrderBy(chapter => chapter.Order)
            .ThenBy(chapter => chapter.Title, StringComparer.Ordinal)
            .ThenBy(chapter => chapter.Key, StringComparer.Ordinal)
            .ToArray();
        var audio = audioChapters
            .OrderBy(chapter => chapter.TrackSortOrder)
            .ThenBy(chapter => chapter.MarkerOrder)
            .ThenBy(chapter => chapter.StartSeconds)
            .ThenBy(chapter => chapter.AudioTrackId)
            .ThenBy(chapter => chapter.AudioMarkerId)
            .ToArray();
        var readableIndexByKey = readable
            .Select((chapter, index) => (chapter.Key, Index: index))
            .GroupBy(entry => entry.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.Ordinal);
        var audioIndexById = audio
            .Select((chapter, index) => ((chapter.AudioTrackId, chapter.AudioMarkerId), Index: index))
            .GroupBy(entry => entry.Item1)
            .ToDictionary(group => group.Key, group => group.First().Index);

        var confirmed = new List<(int Readable, int Audio)>();
        var consumedReadable = new HashSet<int>();
        var consumedAudio = new HashSet<int>();
        foreach (var (chapterKey, trackId, markerId) in confirmedMappings) {
            if (!readableIndexByKey.TryGetValue(chapterKey, out var readableIndex) ||
                !audioIndexById.TryGetValue((trackId, markerId), out var audioIndex) ||
                !consumedReadable.Add(readableIndex)) {
                continue;
            }
            if (!consumedAudio.Add(audioIndex)) {
                consumedReadable.Remove(readableIndex);
                continue;
            }
            confirmed.Add((readableIndex, audioIndex));
        }

        var readableKeys = readable.Select(chapter => MatchKey(chapter.Title)).ToArray();
        var audioKeys = audio.Select(chapter => chapter.IdentifyingTitle is { } title ? MatchKey(title) : string.Empty).ToArray();
        var uniqueAudioIndexByKey = UniqueIndexByKey(audioKeys);
        var uniqueReadableKeys = UniqueIndexByKey(readableKeys);
        var candidates = new List<(int Readable, int Audio)>();
        foreach (var (key, readableIndex) in uniqueReadableKeys) {
            if (!consumedReadable.Contains(readableIndex) &&
                uniqueAudioIndexByKey.TryGetValue(key, out var audioIndex) &&
                !consumedAudio.Contains(audioIndex)) {
                candidates.Add((readableIndex, audioIndex));
            }
        }

        var every = confirmed.Concat(candidates).ToArray();
        return candidates
            .Where(candidate => !every.Any(other => Crosses(candidate, other)))
            .OrderBy(candidate => candidate.Readable)
            .Select(candidate => new MatchedBookAudioChapter(
                readable[candidate.Readable].Key,
                audio[candidate.Audio].AudioTrackId,
                audio[candidate.Audio].AudioMarkerId,
                audio[candidate.Audio].StartSeconds,
                audio[candidate.Audio].EndSeconds))
            .ToArray();
    }

    /// <summary>Non-empty keys that occur exactly once, with the index of that occurrence.</summary>
    private static Dictionary<string, int> UniqueIndexByKey(IReadOnlyList<string> keys) =>
        keys
            .Select((key, index) => (Key: key, Index: index))
            .Where(entry => entry.Key.Length > 0)
            .GroupBy(entry => entry.Key, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().Index, StringComparer.Ordinal);

    /// <summary>Whether two pairs disagree about order: one is earlier on one side and later on the other.</summary>
    private static bool Crosses((int Readable, int Audio) pair, (int Readable, int Audio) other) =>
        (pair.Readable < other.Readable && pair.Audio > other.Audio) ||
        (pair.Readable > other.Readable && pair.Audio < other.Audio);

    #endregion

    #region Actions - Signatures

    /// <summary>Stamps the current matcher <see cref="Version"/> onto a mapping-input hash.</summary>
    /// <param name="inputHash">Hash of every input the automatic pairs depend on.</param>
    public static string StampSignature(string inputHash) => SignaturePrefix + inputHash;

    /// <summary>
    /// Whether a persisted mapping signature was produced by the current matcher, so the automatic pairs
    /// stored beside it are exact evidence. Older signatures mean the pairs predate these rules.
    /// </summary>
    public static bool IsCurrentSignature(string? signature) =>
        signature?.StartsWith(SignaturePrefix, StringComparison.Ordinal) == true;

    #endregion
}
