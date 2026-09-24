using System.Text.RegularExpressions;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media.Books;

/// <summary>
/// The rules of one <see cref="AudiobookStructure"/>: which ordered track sets it describes, whether its
/// chapter windows have exact boundaries that may be paired with readable chapters, and why a Book whose
/// audio has this structure keeps reading and listening separate. Structures are tested in the order of
/// <see cref="All"/>, so each rule only has to say what makes its own structure recognisable.
/// </summary>
public sealed partial class AudiobookStructureDefinition {
    #region Static Variables

    /// <summary>
    /// Shortest file, in seconds, that counts as a long part when every file (except the last) is about
    /// the same length. Part and length splits run for tens of minutes to hours each.
    /// </summary>
    public const double LongPartSeconds = 20 * 60;

    /// <summary>
    /// Largest ratio between the longest and shortest file (the last file excluded) that still counts as
    /// near-uniform. Length splits and one-hour parts stay inside it; chapter lengths rarely do.
    /// </summary>
    public const double UniformPartRatio = 1.25;

    /// <summary>Every track carries embedded chapters with exact start and end times.</summary>
    public static readonly AudiobookStructureDefinition Chaptered = new(
        AudiobookStructure.Chaptered,
        separateReason: null,
        describes: tracks => tracks.All(track => track.HasEmbeddedChapters));

    /// <summary>A single file without embedded chapters: it has no chapter boundaries at all.</summary>
    public static readonly AudiobookStructureDefinition Unstructured = new(
        AudiobookStructure.Unstructured,
        AlignmentGapReason.AudioUnstructured,
        describes: tracks => tracks.Count == 1);

    /// <summary>
    /// Several files named like parts, discs, or tracks, or long files of near-uniform length: splits
    /// of one recording whose file boundaries are not chapter boundaries.
    /// </summary>
    public static readonly AudiobookStructureDefinition Parts = new(
        AudiobookStructure.Parts,
        AlignmentGapReason.AudioInParts,
        describes: tracks => tracks.All(IsNamedLikePart) || HasNearUniformLongDurations(tracks));

    /// <summary>
    /// Several files that are neither chaptered nor part splits. File boundaries are exact; a file is a
    /// chapter only once exact evidence pairs it with one.
    /// </summary>
    public static readonly AudiobookStructureDefinition FilePerChapter = new(
        AudiobookStructure.FilePerChapter,
        separateReason: null,
        describes: _ => true);

    /// <summary>Every structure definition in recognition order, one per <see cref="AudiobookStructure"/> member.</summary>
    public static IReadOnlyList<AudiobookStructureDefinition> All { get; } = [Chaptered, Unstructured, Parts, FilePerChapter];

    #endregion

    #region Variables

    private readonly Func<IReadOnlyList<AudioTrackSpan>, bool> _describes;

    /// <summary>Persisted and generated identity of this structure.</summary>
    public AudiobookStructure Structure { get; }

    /// <summary>
    /// Why a Book with this audio keeps reading and listening separate regardless of pairings, or null
    /// when the structure has exact chapter boundaries.
    /// </summary>
    public AlignmentGapReason? SeparateReason { get; }

    /// <summary>
    /// Whether the chapter windows have exact boundaries, so exact evidence may pair them with readable
    /// chapters and link the Book. Automatic pairing runs only for these structures.
    /// </summary>
    public bool HasExactChapterBoundaries => SeparateReason is null;

    #endregion

    #region Constructors

    private AudiobookStructureDefinition(
        AudiobookStructure structure,
        AlignmentGapReason? separateReason,
        Func<IReadOnlyList<AudioTrackSpan>, bool> describes) {
        Structure = structure;
        SeparateReason = separateReason;
        _describes = describes;
    }

    #endregion

    #region Actions - Lookup

    /// <summary>Returns the definition of a structure identity.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined structure.</exception>
    public static AudiobookStructureDefinition For(AudiobookStructure structure) =>
        All.FirstOrDefault(definition => definition.Structure == structure)
        ?? throw new ArgumentOutOfRangeException(nameof(structure), structure, "Unknown audiobook structure.");

    /// <summary>
    /// Recognises the structure of a Book's playable tracks in playback order, or returns null when the
    /// Book has no playable audio.
    /// </summary>
    /// <param name="tracks">The Book's playable tracks in playback order.</param>
    public static AudiobookStructureDefinition? Of(IReadOnlyList<AudioTrackSpan> tracks) =>
        tracks.Count == 0 ? null : All.First(definition => definition._describes(tracks));

    #endregion

    #region Actions - Part Recognition

    [GeneratedRegex(@"(?:^|[^\p{L}])(?:part|pt|disc|disk|cd|track)[\s._#-]*\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PartName();

    /// <summary>Whether the file name, or the folder holding it, reads like a part, disc, or track split.</summary>
    private static bool IsNamedLikePart(AudioTrackSpan track) =>
        PartName().IsMatch(track.FileStem) ||
        track.SourcePath is { } path && Path.GetFileName(Path.GetDirectoryName(path)) is { Length: > 0 } folder &&
        PartName().IsMatch(folder);

    /// <summary>
    /// Whether several files (the last, usually shorter, excluded) are all long and about the same
    /// length. Unknown durations never count as uniform.
    /// </summary>
    private static bool HasNearUniformLongDurations(IReadOnlyList<AudioTrackSpan> tracks) {
        if (tracks.Count < 3) {
            return false;
        }

        var leading = tracks.Take(tracks.Count - 1).Select(track => track.KnownDuration()).ToArray();
        if (leading.Any(duration => duration is null)) {
            return false;
        }

        var shortest = leading.Min()!.Value;
        var longest = leading.Max()!.Value;
        return shortest >= LongPartSeconds && longest <= shortest * UniformPartRatio;
    }

    #endregion
}
