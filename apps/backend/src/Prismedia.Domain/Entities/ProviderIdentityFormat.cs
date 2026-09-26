using System.Globalization;
using ExternalIdProviders = Prismedia.Contracts.Entities.ExternalIdProviders;

namespace Prismedia.Domain.Entities;

/// <summary>
/// The canonical spelling of one kind of provider identity that pins work at a connected manager: its provider
/// namespace and a positive number wrapped in an optional prefix and suffix. Validation is written once over
/// those values, so a new provider identity adds one instance.
/// </summary>
public sealed class ProviderIdentityFormat {
    #region Static Variables

    /// <summary>A TMDB movie or series identity.</summary>
    public static readonly ProviderIdentityFormat Tmdb = new(ExternalIdProviders.Tmdb);

    /// <summary>A TVDB series or episode identity.</summary>
    public static readonly ProviderIdentityFormat Tvdb = new(ExternalIdProviders.Tvdb);

    /// <summary>A Comic Vine volume identity, such as <c>4050-1234</c>.</summary>
    public static readonly ProviderIdentityFormat ComicVineSeries = new(ExternalIdProviders.ComicVine, prefix: "4050-");

    /// <summary>A Comic Vine issue identity, such as <c>4000-5678</c>.</summary>
    public static readonly ProviderIdentityFormat ComicVineIssue = new(ExternalIdProviders.ComicVine, prefix: "4000-");

    /// <summary>An Open Library work identity, such as <c>OL45804W</c>.</summary>
    public static readonly ProviderIdentityFormat OpenLibraryWork = new(ExternalIdProviders.OpenLibraryWork, prefix: "OL", suffix: "W");

    /// <summary>Every provider identity format.</summary>
    public static IReadOnlyList<ProviderIdentityFormat> All { get; } = [Tmdb, Tvdb, ComicVineSeries, ComicVineIssue, OpenLibraryWork];

    #endregion

    #region Variables

    /// <summary>Provider namespace the identity is stored under.</summary>
    public string Provider { get; }

    /// <summary>Text before the number.</summary>
    public string Prefix { get; }

    /// <summary>Text after the number.</summary>
    public string Suffix { get; }

    #endregion

    #region Constructors

    private ProviderIdentityFormat(string provider, string prefix = "", string suffix = "") {
        Provider = provider;
        Prefix = prefix;
        Suffix = suffix;
    }

    #endregion

    #region Actions - Validation

    /// <summary>Whether a value is this identity's canonical spelling: the prefix, a positive number without leading zeros, and the suffix.</summary>
    public bool IsCanonical(string? value) {
        if (value is null || value.Length <= Prefix.Length + Suffix.Length
            || !value.StartsWith(Prefix, StringComparison.Ordinal) || !value.EndsWith(Suffix, StringComparison.Ordinal)) {
            return false;
        }

        var number = value.AsSpan(Prefix.Length, value.Length - Prefix.Length - Suffix.Length);
        return long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0
            && number.SequenceEqual(parsed.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Returns this identity from a provider map when present and canonical, otherwise null.</summary>
    public ExternalIdentity? Find(IReadOnlyDictionary<string, string> identities) =>
        identities.TryGetValue(Provider, out var value) && IsCanonical(value) ? new(Provider, value) : null;

    #endregion
}
