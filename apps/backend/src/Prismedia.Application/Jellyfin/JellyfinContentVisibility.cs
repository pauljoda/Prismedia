using Prismedia.Contracts.Entities;

namespace Prismedia.Application.Jellyfin;

/// <summary>
/// Content visibility granted to one Jellyfin-compatible profile.
/// </summary>
public readonly record struct JellyfinContentVisibility(bool AllowSfw, bool AllowNsfw) {
    /// <summary>Default Prismedia visibility: SFW content only.</summary>
    public static JellyfinContentVisibility SfwOnly { get; } = new(AllowSfw: true, AllowNsfw: false);

    /// <summary>Creates visibility equivalent to the legacy hide-NSFW flag.</summary>
    public static JellyfinContentVisibility FromHideNsfw(bool hideNsfw) =>
        new(AllowSfw: true, AllowNsfw: !hideNsfw);

    /// <summary>True when no content rows are visible.</summary>
    public bool AllowsAny => AllowSfw || AllowNsfw;

    /// <summary>Legacy read-service privacy flag for hiding NSFW content.</summary>
    public bool HideNsfw => !AllowNsfw;

    /// <summary>
    /// Explicit NSFW filter for entity list queries. Null means both privacy-allowed content classes
    /// remain in the result set.
    /// </summary>
    public bool? NsfwFilter => AllowSfw ? null : AllowNsfw ? true : false;

    /// <summary>Returns true when a thumbnail's content class is visible.</summary>
    public bool Allows(EntityThumbnail item) => item.IsNsfw ? AllowNsfw : AllowSfw;

    /// <summary>Returns true when a card/detail's content class is visible.</summary>
    public bool Allows(IEntityCard item) {
        var isNsfw = item.Capabilities.OfType<FlagsCapability>().FirstOrDefault()?.IsNsfw == true;
        return isNsfw ? AllowNsfw : AllowSfw;
    }
}
