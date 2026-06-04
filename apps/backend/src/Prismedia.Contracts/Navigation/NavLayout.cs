namespace Prismedia.Contracts.Navigation;

/// <summary>
/// One user-defined navigation section in display order.
/// </summary>
/// <param name="Id">Stable section identity assigned by the client.</param>
/// <param name="Label">User-facing section label.</param>
/// <param name="Items">Ordered route hrefs that belong to this section.</param>
/// <param name="Collapsed">Whether the section renders collapsed in the expanded sidebar.</param>
public sealed record NavLayoutSection(
    string Id,
    string Label,
    IReadOnlyList<string> Items,
    bool Collapsed);

/// <summary>
/// The complete, server-persisted navigation layout shared across a user's devices.
/// Sections, order, and hidden items are common to mobile and desktop; the mobile
/// dock (<see cref="MobileFavorites"/>) only renders on mobile but is stored here too.
/// The backend persists this document opaquely — route hrefs are reconciled against
/// the live client-side catalog at render time, so no server-side href validation is done.
/// </summary>
/// <param name="Version">Schema version of the layout document.</param>
/// <param name="Sections">Sections in display order.</param>
/// <param name="Hidden">Route hrefs hidden from normal (non-edit) rendering.</param>
/// <param name="MobileFavorites">Ordered hrefs shown in the mobile bottom dock.</param>
public sealed record NavLayoutDocument(
    int Version,
    IReadOnlyList<NavLayoutSection> Sections,
    IReadOnlyList<string> Hidden,
    IReadOnlyList<string> MobileFavorites);

/// <summary>
/// Response for the navigation layout endpoints.
/// </summary>
/// <param name="Layout">
/// The stored layout, or <c>null</c> when none has been saved yet — in which case the
/// client falls back to its seeded default layout.
/// </param>
public sealed record NavLayoutResponse(NavLayoutDocument? Layout);
