using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Entities;

/// <summary>
/// The two progresses of a work that keeps reading and listening Separate (a Book whose audio has no
/// exact chapter pairing). Each share comes only from its own format's exact position; neither is ever
/// converted into the other, so clients draw two meters instead of one.
/// </summary>
/// <param name="Reason">Why the work keeps reading and listening separate.</param>
/// <param name="ReadingPercent">Share (0..1) of the readable rendition before the reading position, when there is one.</param>
/// <param name="ListeningPercent">Share (0..1) of the known audio listened before the listening position, when there is one.</param>
public sealed record SeparateProgress(
    AlignmentGapReason Reason,
    double? ReadingPercent,
    double? ListeningPercent);
