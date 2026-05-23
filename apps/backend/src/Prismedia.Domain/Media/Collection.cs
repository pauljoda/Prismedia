using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media;

/// <summary>
/// Domain aggregate for a user collection plus its ordered member entities.
/// </summary>
public sealed class Collection : Entity {
    public Collection(
        Guid id,
        string title,
        CollectionMode mode = CollectionMode.Manual,
        string? ruleTreeJson = null,
        CollectionCoverMode coverMode = CollectionCoverMode.Item,
        Guid? coverItemId = null,
        TimeSpan? slideshowDuration = null,
        bool slideshowAutoAdvance = false,
        DateTimeOffset? lastRefreshedAt = null,
        IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
        Mode = mode;
        RuleTreeJson = ruleTreeJson;
        CoverMode = coverMode;
        CoverItemId = coverItemId;
        SlideshowDuration = slideshowDuration ?? TimeSpan.FromSeconds(5);
        SlideshowAutoAdvance = slideshowAutoAdvance;
        LastRefreshedAt = lastRefreshedAt;
    }

    public override EntityKind Kind => EntityKind.Collection;
    public CollectionMode Mode { get; private set; }
    public string? RuleTreeJson { get; private set; }
    public CollectionCoverMode CoverMode { get; private set; }
    public Guid? CoverItemId { get; private set; }
    public TimeSpan SlideshowDuration { get; private set; }
    public bool SlideshowAutoAdvance { get; private set; }
    public DateTimeOffset? LastRefreshedAt { get; private set; }

    protected override IEnumerable<EntityCapability> CreateDefaultCapabilities() =>
    [
        new CapabilityRating(),
        new CapabilityLinks(),
        new CapabilityFlags(),
        new CapabilityFiles()
    ];
}
