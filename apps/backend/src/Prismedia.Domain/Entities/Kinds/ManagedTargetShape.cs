namespace Prismedia.Domain.Entities;

/// <summary>
/// How a connected manager identifies one target inside a holding: the remote item itself, an unnumbered
/// part, a season and episode, or an exact installment label. Validation is written once over each shape's
/// requirements, so a new target form adds one instance.
/// </summary>
public sealed class ManagedTargetShape {
    #region Static Variables

    /// <summary>The target is the remote item itself, with no coordinates.</summary>
    public static readonly ManagedTargetShape Item = new("item", isItem: true);

    /// <summary>An unnumbered part of the item, such as one audiobook file.</summary>
    public static readonly ManagedTargetShape Part = new("part");

    /// <summary>An episode identified by its season and episode numbers.</summary>
    public static readonly ManagedTargetShape Episode = new("episode", requiresEpisode: true);

    /// <summary>An installment identified by its exact issue label.</summary>
    public static readonly ManagedTargetShape Issue = new("issue", requiresIssueLabel: true);

    /// <summary>Every target shape.</summary>
    public static IReadOnlyList<ManagedTargetShape> All { get; } = [Item, Part, Episode, Issue];

    #endregion

    #region Variables

    /// <summary>Plain name used in validation messages.</summary>
    public string Name { get; }

    /// <summary>Whether the target must be the remote item itself.</summary>
    public bool IsItem { get; }

    /// <summary>Whether the target must carry season and episode numbers.</summary>
    public bool RequiresEpisode { get; }

    /// <summary>Whether the target must carry its exact installment label.</summary>
    public bool RequiresIssueLabel { get; }

    #endregion

    #region Constructors

    private ManagedTargetShape(string name, bool isItem = false, bool requiresEpisode = false, bool requiresIssueLabel = false) {
        Name = name;
        IsItem = isItem;
        RequiresEpisode = requiresEpisode;
        RequiresIssueLabel = requiresIssueLabel;
    }

    #endregion

    #region Actions - Validation

    /// <summary>Whether a reported target has exactly the identity this shape requires.</summary>
    public bool Accepts(string itemRemoteId, string targetRemoteId, int? seasonNumber, int? episodeNumber, int? absoluteNumber,
        string? issueLabel) {
        var identityMatches = !IsItem || string.Equals(targetRemoteId, itemRemoteId, StringComparison.Ordinal);
        var coordinatesMatch = RequiresEpisode
            ? seasonNumber is not null && episodeNumber is not null
            : seasonNumber is null && episodeNumber is null && absoluteNumber is null;
        var labelMatches = RequiresIssueLabel ? !string.IsNullOrWhiteSpace(issueLabel) : issueLabel is null;
        return identityMatches && coordinatesMatch && labelMatches;
    }

    #endregion
}
