using System.Text.RegularExpressions;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Rejects video releases advertised as a sample or trailer before download. Provider-authored work
/// and episode titles can contain those words; only additional standalone markers count as evidence.
/// </summary>
public sealed partial class VideoPreviewSpecification : IReleaseSpecification {
    /// <inheritdoc />
    public ReleaseRejectionReason Reason => ReleaseRejectionReason.UnsupportedFormat;

    /// <inheritdoc />
    public ReleaseRejectionReason? Evaluate(IndexerRelease release, BookAcquisitionRules rules) {
        var known = PreviewTokens().Matches(rules.TargetTitle ?? string.Empty)
            .Concat(PreviewTokens().Matches(rules.TargetEpisodeTitle ?? string.Empty))
            .GroupBy(match => match.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        return PreviewTokens().Matches(release.Title)
            .GroupBy(match => match.Value, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > known.GetValueOrDefault(group.Key))
                ? Reason : null;
    }

    [GeneratedRegex(@"(?<![a-z0-9])(?:sample|trailer)(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PreviewTokens();
}
