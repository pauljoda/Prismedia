namespace Prismedia.Domain.Entities;

/// <summary>
/// The closed set of provider-supplied artwork kinds carried by an identify proposal's images
/// (the <c>kind</c> tag on a plugin <c>ImageCandidate</c>). Distinct from <see cref="EntityFileRole"/>,
/// which is the role a downloaded image is persisted under: a provider classifies artwork
/// (poster, still, banner, …) and the apply pipeline maps that classification to a stored role via
/// <c>ImageKindRoleResolver</c>.
/// </summary>
public enum MediaImageKind {
    /// <summary>Portrait key art.</summary>
    [Code("poster")]
    Poster,

    /// <summary>Episode/scene still frame.</summary>
    [Code("still")]
    Still,

    /// <summary>Square or front cover art (books, music).</summary>
    [Code("cover")]
    Cover,

    /// <summary>Wide background art.</summary>
    [Code("backdrop")]
    Backdrop,

    /// <summary>Transparent logo / wordmark.</summary>
    [Code("logo")]
    Logo,

    /// <summary>Wide banner art.</summary>
    [Code("banner")]
    Banner,

    /// <summary>Hero/feature art.</summary>
    [Code("hero")]
    Hero,

    /// <summary>Generic thumbnail.</summary>
    [Code("thumbnail")]
    Thumbnail,

    /// <summary>Person profile portrait.</summary>
    [Code("profile")]
    Profile
}
