namespace Prismedia.Domain.Capabilities;

/// <summary>
/// Mutable marker capability for timeline, page, or navigation markers.
/// </summary>
public sealed class CapabilityMarkers : CollectionCapability<CapabilityMarkers.Item> {
    /// <summary>
    /// Creates the marker capability, keeping markers ordered by start time.
    /// </summary>
    /// <param name="items">Initial markers, or null for none.</param>
    public CapabilityMarkers(IEnumerable<Item>? items = null) : base(items) =>
        SortBy(item => item.Seconds);

    /// <summary>
    /// Marks a named time range or point inside time-based media such as video or audio.
    /// </summary>
    /// <param name="Id">Stable marker identifier.</param>
    /// <param name="Title">Human-readable marker label.</param>
    /// <param name="Seconds">Start time in seconds from the beginning of the media.</param>
    /// <param name="EndSeconds">Optional end time in seconds when the marker spans a range.</param>
    public sealed record Item(Guid Id, string Title, double Seconds, double? EndSeconds);

    /// <summary>Adds a marker and returns its stable identifier.</summary>
    public Guid Add(string title, double seconds, double? endSeconds = null) {
        var marker = new Item(Guid.NewGuid(), NormalizeTitle(title), ClampSeconds(seconds), ClampEndSeconds(seconds, endSeconds));
        AddItem(marker);
        SortBy(item => item.Seconds);
        return marker.Id;
    }

    /// <summary>Updates an existing marker.</summary>
    public bool Update(Guid markerId, string title, double seconds, double? endSeconds = null) {
        if (RemoveItems(item => item.Id == markerId) == 0) {
            return false;
        }

        AddItem(new Item(markerId, NormalizeTitle(title), ClampSeconds(seconds), ClampEndSeconds(seconds, endSeconds)));
        SortBy(item => item.Seconds);
        return true;
    }

    /// <summary>Deletes an existing marker.</summary>
    public bool Delete(Guid markerId) => RemoveItems(item => item.Id == markerId) > 0;

    private static string NormalizeTitle(string title) =>
        string.IsNullOrWhiteSpace(title)
            ? throw new ArgumentException("Marker title cannot be empty.", nameof(title))
            : title.Trim();

    private static double ClampSeconds(double seconds) =>
        double.IsFinite(seconds) ? Math.Max(0, seconds) : 0;

    private static double? ClampEndSeconds(double seconds, double? endSeconds) =>
        endSeconds is null ? null : Math.Max(ClampSeconds(seconds), ClampSeconds(endSeconds.Value));
}
