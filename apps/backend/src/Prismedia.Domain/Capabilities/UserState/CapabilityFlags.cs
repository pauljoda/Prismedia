namespace Prismedia.Domain.Capabilities;

/// <summary>
/// Mutable flag capability for shared user-facing boolean state.
/// </summary>
public sealed class CapabilityFlags : EntityCapability {
    public CapabilityFlags(bool? isFavorite = null, bool? isNsfw = null, bool? isOrganized = null) {
        IsFavorite = isFavorite;
        IsNsfw = isNsfw;
        IsOrganized = isOrganized;
    }

    public bool? IsFavorite { get; private set; }
    public bool? IsNsfw { get; private set; }
    public bool? IsOrganized { get; private set; }

    /// <summary>
    /// Applies a sparse flag update.
    /// </summary>
    public void Patch(bool? isFavorite, bool? isNsfw, bool? isOrganized) {
        if (isFavorite is { } favorite) {
            IsFavorite = favorite;
        }

        if (isNsfw is { } nsfw) {
            IsNsfw = nsfw;
        }

        if (isOrganized is { } organized) {
            IsOrganized = organized;
        }
    }
}
