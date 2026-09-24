namespace Prismedia.Application.Acquisition;

/// <summary>
/// How an audiobook release lays out its audio, judged from its file list (names, and sizes when known)
/// or, before any file list is available, from its title. The shape decides whether a release can be
/// imported at all, how releases rank against each other, which files a download imports, and whether an
/// owned audiobook stays eligible for an upgrade. A single M4B file or one file per chapter carries exact
/// chapter boundaries; part splits and one long MP3 do not. Classification is written once over each
/// shape's own rule, so a new layout adds one instance.
/// </summary>
/// <remarks>
/// A shape is release-level evidence, not a probe result: a single M4B usually carries chapter markers but
/// is not guaranteed to. The recorded probe structure of the imported files is the stronger fact once it
/// exists.
/// </remarks>
public sealed class AudiobookReleaseShape {
    #region Static Variables

    /// <summary>Smallest size of one split part for near-uniform sizes to count as a length split (25 MiB).</summary>
    private const long UniformPartMinimumBytes = 25L * 1024 * 1024;

    /// <summary>Largest ratio between the biggest and the smallest part of a near-uniform length split.</summary>
    private const double UniformPartSizeRatio = 1.10;

    /// <summary>Fewest parts, besides the shorter final part, that make near-uniform sizes meaningful evidence.</summary>
    private const int UniformPartMinimumCount = 2;

    /// <summary>MPEG-4 audiobook containers, which carry chapter tracks.</summary>
    private static readonly IReadOnlySet<string> Mpeg4Audio =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".m4b", ".m4a" };

    /// <summary>MP3 audio, which rarely carries chapter markers.</summary>
    private static readonly IReadOnlySet<string> Mp3Audio =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp3" };

    /// <summary>Importable audio formats, most preferred first. A download imports only one of them.</summary>
    private static readonly IReadOnlyList<IReadOnlySet<string>> ImportableFormats = [Mpeg4Audio, Mp3Audio];

    /// <summary>Every audio extension a release may be imported from.</summary>
    public static IReadOnlySet<string> ImportableExtensions { get; } =
        new HashSet<string>(ImportableFormats.SelectMany(format => format), StringComparer.OrdinalIgnoreCase);

    /// <summary>No evidence yet: an empty file list, or a title that names no audio format.</summary>
    public static readonly AudiobookReleaseShape Unknown = new(
        "unknown", "an unknown layout", rank: 3,
        matchesFiles: payload => payload.Files.Count == 0,
        matchesTitle: _ => false);

    /// <summary>Only formats Prismedia cannot import (FLAC, Opus, OGG, AAX, MKA, AAC), or no audio at all.</summary>
    public static readonly AudiobookReleaseShape Unimportable = new(
        "unimportable", "no audio Prismedia can import", rank: 0, isAdmissible: false,
        matchesFiles: payload => payload.Formats.Count == 0,
        matchesTitle: title => title.Extensions.Count > 0 && !title.Extensions.Overlaps(ImportableExtensions));

    /// <summary>Several importable formats in one download; it imports the preferred one and ranks as that set.</summary>
    public static readonly AudiobookReleaseShape Mixed = new(
        "mixed", "several audio formats", rank: 3, resolvesToImportSet: true,
        matchesFiles: payload => payload.Formats.Count > 1,
        matchesTitle: _ => false);

    /// <summary>One M4B or M4A file, whose chapter tracks usually mark every chapter.</summary>
    public static readonly AudiobookReleaseShape SingleM4b = new(
        "single-m4b", "one M4B file", rank: 5,
        matchesFiles: payload => payload.IsSingleFileOf(Mpeg4Audio),
        matchesTitle: title => title.Extensions.Overlaps(Mpeg4Audio));

    /// <summary>One MP3 file for the whole book, with no chapter boundaries.</summary>
    public static readonly AudiobookReleaseShape SingleLargeFile = new(
        "single-large-file", "one long MP3 file", rank: 1,
        matchesFiles: payload => payload.IsSingleFileOf(Mp3Audio),
        matchesTitle: title => title.FileCount == 1 && title.Extensions.Overlaps(Mp3Audio));

    /// <summary>Files split by length, part, disc, or track rather than at chapter boundaries.</summary>
    public static readonly AudiobookReleaseShape PartFiles = new(
        "part-files", "part files", rank: 2,
        matchesFiles: payload => payload.LooksLikeParts,
        matchesTitle: _ => false);

    /// <summary>Several ordered files, each one chapter.</summary>
    public static readonly AudiobookReleaseShape ChapterFiles = new(
        "chapter-files", "one file per chapter", rank: 4,
        matchesFiles: _ => true,
        matchesTitle: _ => false);

    /// <summary>Every shape, in classification precedence.</summary>
    public static IReadOnlyList<AudiobookReleaseShape> All { get; } =
        [Unknown, Unimportable, Mixed, SingleM4b, SingleLargeFile, PartFiles, ChapterFiles];

    /// <summary>The lowest-ranked shape that carries chapter boundaries: the target an upgrade must reach.</summary>
    public static AudiobookReleaseShape ChapteredFloor { get; } =
        All.Where(shape => shape.IsChaptered).MinBy(shape => shape.Rank)!;

    #endregion

    #region Variables

    /// <summary>Stored spelling of this shape.</summary>
    public string Code { get; }

    /// <summary>User-facing phrase describing the layout, for messages.</summary>
    public string Label { get; }

    /// <summary>Preference order; higher is better. <see cref="Unknown"/> separates chaptered from chapterless shapes.</summary>
    public int Rank { get; }

    /// <summary>Whether a release of this shape can be imported as an audiobook.</summary>
    public bool IsAdmissible { get; }

    /// <summary>Whether this shape is expected to carry exact chapter boundaries.</summary>
    public bool IsChaptered => IsAdmissible && Rank > Unknown.Rank;

    /// <summary>Whether this shape is known to lack chapter boundaries, so a chaptered release would improve it.</summary>
    public bool IsChapterless => IsAdmissible && Rank < Unknown.Rank;

    private readonly Func<AudiobookPayload, bool> matchesFiles;
    private readonly Func<ReleaseTitleEvidence, bool> matchesTitle;
    private readonly bool resolvesToImportSet;

    #endregion

    #region Constructors

    private AudiobookReleaseShape(
        string code,
        string label,
        int rank,
        Func<AudiobookPayload, bool> matchesFiles,
        Func<ReleaseTitleEvidence, bool> matchesTitle,
        bool isAdmissible = true,
        bool resolvesToImportSet = false) {
        Code = code;
        Label = label;
        Rank = rank;
        IsAdmissible = isAdmissible;
        this.matchesFiles = matchesFiles;
        this.matchesTitle = matchesTitle;
        this.resolvesToImportSet = resolvesToImportSet;
    }

    #endregion

    #region Actions - Classification

    /// <summary>The stored shape spelled <paramref name="code"/>; an unrecognised spelling reads as <see cref="Unknown"/>.</summary>
    public static AudiobookReleaseShape Parse(string code) =>
        All.FirstOrDefault(shape => string.Equals(shape.Code, code, StringComparison.Ordinal)) ?? Unknown;

    /// <summary>
    /// Classifies a whole download. A download holding several importable formats is <see cref="Mixed"/>;
    /// use <see cref="Resolve"/> for the shape of the files it would actually import.
    /// </summary>
    /// <param name="files">Payload-relative paths with sizes; a size of 0 means unknown.</param>
    public static AudiobookReleaseShape Of(IReadOnlyList<ImportCandidateFile> files) {
        var payload = new AudiobookPayload(files);
        return All.First(shape => shape.matchesFiles(payload));
    }

    /// <summary>
    /// The shape of the files a download imports: the download's own shape, or for a <see cref="Mixed"/>
    /// download the shape of its preferred format. Use this to rank, admit, and record a payload.
    /// </summary>
    /// <param name="files">Payload-relative paths with sizes; a size of 0 means unknown.</param>
    public static AudiobookReleaseShape Resolve(IReadOnlyList<ImportCandidateFile> files) {
        var shape = Of(files);
        return shape.resolvesToImportSet ? Of(ImportSet(files)) : shape;
    }

    /// <summary>
    /// The shape a search result is expected to have: from its advertised file names when the provider
    /// lists them (Soulseek), otherwise from the audio formats its title names and, when an indexer
    /// advertises it, its file count. A title naming M4B or M4A is expected to be one M4B; a title naming
    /// only MP3 is one long file when it advertises exactly one file and unknown otherwise; a title naming
    /// only formats Prismedia cannot import is <see cref="Unimportable"/>.
    /// </summary>
    public static AudiobookReleaseShape Expected(IndexerRelease release) {
        if (release.KnownFileNames.Count > 0) {
            return Resolve(release.KnownFileNames.Select(name => new ImportCandidateFile(name, 0)).ToArray());
        }

        var title = new ReleaseTitleEvidence(
            new HashSet<string>(BookFormatDetection.NamedAudioExtensions(release.Title), StringComparer.OrdinalIgnoreCase),
            release.AdvertisedFileCount);
        return All.FirstOrDefault(shape => shape.matchesTitle(title)) ?? Unknown;
    }

    #endregion

    #region Actions - Import

    /// <summary>
    /// The files a download imports as one coherent audiobook: every file of the most preferred importable
    /// format present (M4B/M4A before MP3), never a mix of formats. Empty when nothing is importable.
    /// </summary>
    /// <param name="files">Payload-relative paths with sizes.</param>
    public static IReadOnlyList<ImportCandidateFile> ImportSet(IReadOnlyList<ImportCandidateFile> files) =>
        new AudiobookPayload(files).Preferred;

    #endregion

    #region Actions - Ranking

    /// <summary>
    /// Whether a release of this shape improves an owned audiobook of shape <paramref name="owned"/>: it must
    /// carry chapter boundaries and rank strictly higher. An unknown layout never proves an improvement.
    /// </summary>
    public bool Upgrades(AudiobookReleaseShape owned) => IsChaptered && Rank > owned.Rank;

    /// <summary>
    /// Whether a release of this shape is known to be worse structured than an owned audiobook of shape
    /// <paramref name="owned"/>. An unknown layout makes no claim either way.
    /// </summary>
    public bool Downgrades(AudiobookReleaseShape owned) =>
        IsAdmissible && (IsChaptered || IsChapterless) && Rank < owned.Rank;

    #endregion

    /// <summary>What a release title says about its audio: the extensions it names and any advertised file count.</summary>
    private sealed record ReleaseTitleEvidence(IReadOnlySet<string> Extensions, int? FileCount);

    /// <summary>A download's files grouped by importable format, with the evidence the shape rules read.</summary>
    private sealed class AudiobookPayload {
        #region Variables

        /// <summary>Every file in the download, importable or not.</summary>
        public IReadOnlyList<ImportCandidateFile> Files { get; }

        /// <summary>Importable files grouped by format, most preferred format first; empty formats are omitted.</summary>
        public IReadOnlyList<IReadOnlyList<ImportCandidateFile>> Formats { get; }

        /// <summary>The files of the most preferred importable format present.</summary>
        public IReadOnlyList<ImportCandidateFile> Preferred => Formats.Count == 0 ? [] : Formats[0];

        /// <summary>
        /// Whether the preferred files were split by part, disc, track, or length rather than at chapters:
        /// disc folders, varying part/track numbers, or near-uniform long sizes. Chapter-marked names win.
        /// </summary>
        public bool LooksLikeParts {
            get {
                var files = Preferred;
                if (files.Count < 2) {
                    return false;
                }

                var names = files.Select(file => Path.GetFileNameWithoutExtension(FileName(file))).ToArray();
                if (names.Count(BookReleaseTokens.NamesAudioChapter) * 2 >= files.Count) {
                    return false;
                }

                if (files.Any(file => Folders(file).Any(folder => BookReleaseTokens.ParseAudioDisc(folder) is not null))) {
                    return true;
                }

                var parts = names.Select(BookReleaseTokens.ParseAudioPart).OfType<int>().ToArray();
                return (parts.Length * 2 >= files.Count && parts.Distinct().Count() > 1) || HasNearUniformLongSizes(files);
            }
        }

        #endregion

        #region Constructors

        public AudiobookPayload(IReadOnlyList<ImportCandidateFile> files) {
            Files = files;
            Formats = ImportableFormats
                .Select(format => (IReadOnlyList<ImportCandidateFile>)files
                    .Where(file => format.Contains(Path.GetExtension(FileName(file))))
                    .ToArray())
                .Where(group => group.Count > 0)
                .ToArray();
        }

        #endregion

        #region Actions - Evidence

        /// <summary>Whether the download imports exactly one file, of <paramref name="format"/>.</summary>
        public bool IsSingleFileOf(IReadOnlySet<string> format) =>
            Preferred.Count == 1 && format.Contains(Path.GetExtension(FileName(Preferred[0])));

        /// <summary>
        /// A length split cuts parts of (nearly) equal size, except usually a shorter final part. Chapters are
        /// rarely that even, so equal long files without chapter names are parts.
        /// </summary>
        private static bool HasNearUniformLongSizes(IReadOnlyList<ImportCandidateFile> files) {
            if (files.Any(file => file.SizeBytes <= 0)) {
                return false;
            }

            var body = files.Select(file => file.SizeBytes).OrderByDescending(size => size).SkipLast(1).ToArray();
            return body.Length >= UniformPartMinimumCount
                && body[^1] >= UniformPartMinimumBytes
                && body[0] <= body[^1] * UniformPartSizeRatio;
        }

        private static string FileName(ImportCandidateFile file) => Segments(file)[^1];

        private static IEnumerable<string> Folders(ImportCandidateFile file) => Segments(file)[..^1];

        // Provider paths use either separator (Soulseek reports Windows paths), so split on both.
        private static string[] Segments(ImportCandidateFile file) =>
            file.RelativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } segments
                ? segments
                : [file.RelativePath];

        #endregion
    }
}
