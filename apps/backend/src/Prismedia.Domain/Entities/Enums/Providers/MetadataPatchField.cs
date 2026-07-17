namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of field selectors naming the sections of an entity metadata patch to
/// import. The identify review UI (and unattended applies via
/// <c>ProposalApplySelection</c>) send these codes; the apply service and patch
/// validator match on them. Codes keep their camelCase spelling because it is the
/// existing identify-apply wire contract.
/// </summary>
public enum MetadataPatchField {
    /// <summary>Entity title.</summary>
    [Code("title")]
    Title,

    /// <summary>Entity description.</summary>
    [Code("description")]
    Description,

    /// <summary>External provider ids.</summary>
    [Code("externalIds")]
    ExternalIds,

    /// <summary>External URLs.</summary>
    [Code("urls")]
    Urls,

    /// <summary>Dated fields such as release dates.</summary>
    [Code("dates")]
    Dates,

    /// <summary>Numeric stats such as runtime.</summary>
    [Code("stats")]
    Stats,

    /// <summary>Ordering positions such as series index.</summary>
    [Code("positions")]
    Positions,

    /// <summary>Content classification / rating system value.</summary>
    [Code("classification")]
    Classification,

    /// <summary>Boolean flags such as NSFW.</summary>
    [Code("flags")]
    Flags,

    /// <summary>Tag relationship links.</summary>
    [Code("tags")]
    Tags,

    /// <summary>Studio relationship link.</summary>
    [Code("studio")]
    Studio,

    /// <summary>Credit relationship links (cast and crew).</summary>
    [Code("credits")]
    Credits,

    /// <summary>Artwork image selections.</summary>
    [Code("images")]
    Images
}
