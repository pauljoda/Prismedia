using System.Text.RegularExpressions;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Rejects an individually posted CD part as a complete movie. A total-disc label such as 2CD is not
/// an individual part; full multipart payloads still receive the importer's file-level review.
/// </summary>
public sealed partial class MoviePartialReleaseSpecification : IReleaseSpecification {
    /// <inheritdoc />
    public ReleaseRejectionReason Reason => ReleaseRejectionReason.UnsupportedFormat;

    /// <inheritdoc />
    public ReleaseRejectionReason? Evaluate(IndexerRelease release, BookAcquisitionRules rules) {
        var expected = Parts(rules.TargetTitle ?? string.Empty)
            .GroupBy(part => part)
            .ToDictionary(group => group.Key, group => group.Count());
        return Parts(release.Title).GroupBy(part => part)
            .Any(group => group.Count() > expected.GetValueOrDefault(group.Key)) ? Reason : null;
    }

    private static IEnumerable<string> Parts(string title) =>
        CdPart().Matches(title).Select(match => match.Groups[1].Value);

    [GeneratedRegex(@"(?<![a-z0-9])cd[\s._-]*0*([1-9][0-9]*)(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CdPart();
}
