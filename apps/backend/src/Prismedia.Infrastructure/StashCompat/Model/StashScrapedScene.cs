namespace Prismedia.Infrastructure.StashCompat.Model;

/// <summary>A studio scraped from a Stash scraper definition.</summary>
public sealed class StashScrapedStudio {
    /// <summary>Studio name.</summary>
    public string? Name { get; set; }

    /// <summary>Studio homepage URL.</summary>
    public string? Url { get; set; }

    /// <summary>Studio logo/image URL.</summary>
    public string? Image { get; set; }

    /// <summary>Long-form studio description.</summary>
    public string? Description { get; set; }

    /// <summary>True when any usable field was extracted.</summary>
    public bool HasData =>
        !string.IsNullOrWhiteSpace(Name) ||
        !string.IsNullOrWhiteSpace(Url) ||
        !string.IsNullOrWhiteSpace(Image) ||
        !string.IsNullOrWhiteSpace(Description);
}

/// <summary>A performer scraped from a Stash scraper definition (scene credit or performer page).</summary>
public sealed class StashScrapedPerformer {
    /// <summary>Performer name.</summary>
    public string? Name { get; set; }

    /// <summary>Performer profile URL.</summary>
    public string? Url { get; set; }

    /// <summary>Performer image/avatar URL.</summary>
    public string? Image { get; set; }

    /// <summary>Performer gender.</summary>
    public string? Gender { get; set; }

    /// <summary>Long-form bio/details.</summary>
    public string? Details { get; set; }

    /// <summary>Birth date, normalized to YYYY-MM-DD where possible.</summary>
    public string? Birthdate { get; set; }

    /// <summary>Country.</summary>
    public string? Country { get; set; }

    /// <summary>True when any usable field was extracted.</summary>
    public bool HasData =>
        !string.IsNullOrWhiteSpace(Name) ||
        !string.IsNullOrWhiteSpace(Image) ||
        !string.IsNullOrWhiteSpace(Details);
}

/// <summary>A tag scraped from a Stash scraper definition.</summary>
public sealed class StashScrapedTag {
    /// <summary>Tag display name.</summary>
    public string? Name { get; set; }

    /// <summary>Tag page URL.</summary>
    public string? Url { get; set; }

    /// <summary>Tag image URL, when an upstream source exposes one.</summary>
    public string? Image { get; set; }

    /// <summary>Long-form tag description.</summary>
    public string? Description { get; set; }

    /// <summary>Comma- or newline-separated aliases from the upstream source.</summary>
    public string? Aliases { get; set; }

    /// <summary>True when any usable field was extracted.</summary>
    public bool HasData =>
        !string.IsNullOrWhiteSpace(Name) ||
        !string.IsNullOrWhiteSpace(Url) ||
        !string.IsNullOrWhiteSpace(Description) ||
        !string.IsNullOrWhiteSpace(Aliases) ||
        !string.IsNullOrWhiteSpace(Image);
}

/// <summary>A scene scraped from a Stash scraper definition (the common scene shape).</summary>
public sealed class StashScrapedScene {
    /// <summary>Scene title.</summary>
    public string? Title { get; set; }

    /// <summary>Studio-specific code or stock number.</summary>
    public string? Code { get; set; }

    /// <summary>Release date string, normalized to YYYY-MM-DD where possible.</summary>
    public string? Date { get; set; }

    /// <summary>Long-form synopsis or details.</summary>
    public string? Details { get; set; }

    /// <summary>Director name.</summary>
    public string? Director { get; set; }

    /// <summary>Canonical scene URL reported by the scraper.</summary>
    public string? Url { get; set; }

    /// <summary>Cover or poster image URL.</summary>
    public string? Image { get; set; }

    /// <summary>Studio reference.</summary>
    public StashScrapedStudio? Studio { get; set; }

    /// <summary>Performers credited on the scene (name plus any URL/image the scraper exposes).</summary>
    public IReadOnlyList<StashScrapedPerformer> Performers { get; set; } = [];

    /// <summary>Tags attached to the scene.</summary>
    public IReadOnlyList<StashScrapedTag> Tags { get; set; } = [];

    /// <summary>True when the scrape produced any usable field.</summary>
    public bool HasData =>
        !string.IsNullOrWhiteSpace(Title) ||
        !string.IsNullOrWhiteSpace(Url) ||
        !string.IsNullOrWhiteSpace(Date) ||
        !string.IsNullOrWhiteSpace(Details) ||
        Performers.Count > 0 ||
        Tags.Count > 0 ||
        Studio is not null;
}
