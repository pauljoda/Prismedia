using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media;

/// <summary>
/// Domain aggregate for a user collection plus its ordered member entities.
/// </summary>
public sealed class Collection : Entity {
    private static readonly HashSet<EntityKind> ContainableKinds =
    [
        EntityKind.Video,
        EntityKind.Movie,
        EntityKind.VideoSeries,
        EntityKind.Gallery,
        EntityKind.Image,
        EntityKind.Book,
        EntityKind.MusicArtist,
        EntityKind.AudioLibrary,
        EntityKind.AudioTrack,
    ];

    public Collection(
        Guid id,
        string title,
        Guid ownerUserId,
        CollectionMode mode = CollectionMode.Manual,
        string? ruleTreeJson = null,
        CollectionCoverMode coverMode = CollectionCoverMode.Item,
        Guid? coverItemId = null,
        DateTimeOffset? lastRefreshedAt = null,
        bool isShared = false,
        IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
        if (ownerUserId == Guid.Empty) {
            throw new ArgumentException("Collections require an owning user.", nameof(ownerUserId));
        }

        OwnerUserId = ownerUserId;
        IsShared = isShared;
        ConfigureRules(mode, ruleTreeJson);
        SetCover(coverMode, coverItemId);
        LastRefreshedAt = lastRefreshedAt;
    }

    public override EntityKind Kind => EntityKind.Collection;
    public Guid OwnerUserId { get; }
    public bool IsShared { get; private set; }
    public CollectionMode Mode { get; private set; }
    public string? RuleTreeJson { get; private set; }
    public CollectionCoverMode CoverMode { get; private set; }
    public Guid? CoverItemId { get; private set; }
    public DateTimeOffset? LastRefreshedAt { get; private set; }

    /// <summary>True when users can directly add, remove, or reorder manual members.</summary>
    public bool CanEditManualMembership => Mode is CollectionMode.Manual or CollectionMode.Hybrid;

    /// <summary>True when collection membership is at least partly produced from a rule tree.</summary>
    public bool UsesRules => Mode is CollectionMode.Dynamic or CollectionMode.Hybrid;

    /// <summary>Returns whether the supplied user owns and may mutate this collection.</summary>
    public bool IsOwnedBy(Guid userId) => userId != Guid.Empty && OwnerUserId == userId;

    /// <summary>Returns whether the supplied user may view this collection.</summary>
    public bool CanView(Guid userId) => IsShared || IsOwnedBy(userId);

    /// <summary>Sets whether other household users may view this collection.</summary>
    public void SetSharing(bool isShared) {
        IsShared = isShared;
    }

    /// <summary>Returns whether collections may directly contain the supplied entity kind.</summary>
    public static bool CanContain(EntityKind kind) => ContainableKinds.Contains(kind);

    /// <summary>Updates the rule mode and normalized rule tree for this collection.</summary>
    public void ConfigureRules(CollectionMode mode, string? ruleTreeJson) {
        if (mode is CollectionMode.Manual) {
            Mode = mode;
            RuleTreeJson = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(ruleTreeJson)) {
            throw new ArgumentException("Rule-driven collections require a rule tree.", nameof(ruleTreeJson));
        }

        Mode = mode;
        RuleTreeJson = ruleTreeJson.Trim();
    }

    /// <summary>Updates how this collection chooses its cover artwork.</summary>
    public void SetCover(CollectionCoverMode coverMode, Guid? coverItemId) {
        CoverMode = coverMode;
        CoverItemId = coverItemId;
    }

    /// <summary>Records when dynamic collection membership was last refreshed.</summary>
    public void MarkRefreshed(DateTimeOffset refreshedAt) {
        LastRefreshedAt = refreshedAt;
    }

    protected override IEnumerable<EntityCapability> CreateDefaultCapabilities() => [];
}
