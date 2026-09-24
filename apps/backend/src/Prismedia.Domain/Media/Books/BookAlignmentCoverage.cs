namespace Prismedia.Domain.Media.Books;

/// <summary>How much of a work's readable and audio content is paired.</summary>
/// <param name="ReadableCount">Readable chapters.</param>
/// <param name="AudioWindowCount">Addressable audio chapter windows.</param>
/// <param name="PairedCount">Rows carrying both sides.</param>
/// <param name="ManualCount">Paired rows a user chose.</param>
/// <param name="AutomaticCount">Paired rows the matcher derived.</param>
/// <param name="ReadableOnlyCount">Readable chapters without audio.</param>
/// <param name="AudioOnlyCount">Audio windows without a readable chapter.</param>
/// <param name="PairedReadableFraction">Share (0..1) of the readable rendition covered by paired chapters.</param>
/// <param name="PairedAudioSeconds">Seconds of audio inside paired windows with known bounds.</param>
/// <param name="TotalAudioSeconds">Seconds of probed audio across every playable track.</param>
public sealed record BookAlignmentCoverage(
    int ReadableCount,
    int AudioWindowCount,
    int PairedCount,
    int ManualCount,
    int AutomaticCount,
    int ReadableOnlyCount,
    int AudioOnlyCount,
    double PairedReadableFraction,
    double PairedAudioSeconds,
    double TotalAudioSeconds);
