namespace Prismedia.Domain.Entities;

/// <summary>
/// Cross-client navigation contract for one Entity kind. The domain definition owns the stable
/// destination identity and route template so generated clients do not maintain parallel kind
/// switches. Clients remain responsible only for rendering and performing navigation.
/// </summary>
public sealed record EntityKindNavigation {
    /// <summary>Creates one validated cross-client navigation contract.</summary>
    /// <param name="canonicalBrowseKind">Kind whose list destination represents this kind.</param>
    /// <param name="destinationId">Stable native/app-shell destination identifier.</param>
    /// <param name="browsePath">Canonical web browse path.</param>
    /// <param name="detailPathTemplate">
    /// Optional web detail template containing <c>{id}</c> and, for nested routes,
    /// <c>{parentId}</c>.
    /// </param>
    /// <param name="requiredAncestorKind">Ancestor required to populate <c>{parentId}</c>.</param>
    public EntityKindNavigation(
        EntityKind canonicalBrowseKind,
        string destinationId,
        string browsePath,
        string? detailPathTemplate,
        EntityKind? requiredAncestorKind = null) {
        CanonicalBrowseKind = canonicalBrowseKind;
        DestinationId = RequireText(destinationId, nameof(destinationId));
        BrowsePath = RequirePath(browsePath, nameof(browsePath));
        DetailPathTemplate = detailPathTemplate is null
            ? null
            : RequirePath(detailPathTemplate, nameof(detailPathTemplate));
        RequiredAncestorKind = requiredAncestorKind;

        if (DetailPathTemplate is not null && !DetailPathTemplate.Contains("{id}", StringComparison.Ordinal)) {
            throw new ArgumentException("Entity detail route templates must contain '{id}'.", nameof(detailPathTemplate));
        }

        var containsParent = DetailPathTemplate?.Contains("{parentId}", StringComparison.Ordinal) == true;
        if (containsParent != RequiredAncestorKind.HasValue) {
            throw new ArgumentException(
                "Nested detail routes must declare both '{parentId}' and a required ancestor kind.",
                nameof(requiredAncestorKind));
        }
    }

    /// <summary>Kind whose list destination represents this kind.</summary>
    public EntityKind CanonicalBrowseKind { get; }

    /// <summary>Stable destination identifier used by native app shells.</summary>
    public string DestinationId { get; }

    /// <summary>Canonical web path for browsing this kind's destination.</summary>
    public string BrowsePath { get; }

    /// <summary>Optional web detail route template.</summary>
    public string? DetailPathTemplate { get; }

    /// <summary>Ancestor kind required by a nested detail route.</summary>
    public EntityKind? RequiredAncestorKind { get; }

    /// <summary>Whether entities of this kind have a detail route without ancestor context.</summary>
    public bool IsTopLevel => DetailPathTemplate is not null && RequiredAncestorKind is null;

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Navigation text cannot be empty.", parameterName)
            : value.Trim();

    private static string RequirePath(string value, string parameterName) {
        var path = RequireText(value, parameterName);
        if (!path.StartsWith("/", StringComparison.Ordinal) ||
            path.Length > 1 && path.EndsWith("/", StringComparison.Ordinal)) {
            throw new ArgumentException("Navigation paths must start with '/' and omit a trailing '/'.", parameterName);
        }

        return path;
    }
}
