namespace Prismedia.Infrastructure.StashCompat.Model;

/// <summary>A studio scraped from a Stash scraper definition.</summary>
public sealed class StashScrapedStudio {
    /// <summary>Studio name.</summary>
    public string? Name { get; set; }

    /// <summary>Studio homepage URL.</summary>
    public string? Url { get; set; }
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

    /// <summary>Performer names credited on the scene.</summary>
    public IReadOnlyList<string> Performers { get; set; } = [];

    /// <summary>Tag names attached to the scene.</summary>
    public IReadOnlyList<string> Tags { get; set; } = [];

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
