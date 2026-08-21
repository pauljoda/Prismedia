namespace Prismedia.Domain.Entities;

/// <summary>Semantic role of one page inside an ordered readable image sequence.</summary>
public enum PageType {
    /// <summary>Ordinary narrative content.</summary>
    [Code("story")]
    Story,

    /// <summary>Front cover artwork.</summary>
    [Code("front-cover")]
    FrontCover,

    /// <summary>Back cover artwork.</summary>
    [Code("back-cover")]
    BackCover,

    /// <summary>Advertisement or promotional insert.</summary>
    [Code("advertisement")]
    Advertisement,

    /// <summary>Letters, editorial, or correspondence page.</summary>
    [Code("letters")]
    Letters,

    /// <summary>Known page that does not fit another canonical role.</summary>
    [Code("other")]
    Other
}
