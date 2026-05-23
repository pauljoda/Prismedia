using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Capabilities;

/// <summary>
/// Mutable subtitle capability for subtitle or caption tracks.
/// </summary>
public sealed class CapabilitySubtitles(IEnumerable<CapabilitySubtitles.Item>? items = null)
    : CollectionCapability<CapabilitySubtitles.Item>(items) {
    /// <summary>
    /// Describes a subtitle or caption track attached to a media entity.
    /// </summary>
    /// <param name="Id">Stable subtitle track identifier.</param>
    /// <param name="Language">BCP-47 or provider language code for the subtitle text.</param>
    /// <param name="Label">Optional display label shown to the user.</param>
    /// <param name="Format">Served subtitle format, such as vtt.</param>
    /// <param name="Source">How the subtitle was discovered or generated.</param>
    /// <param name="StoragePath">Path to the normalized subtitle file served by Prismedia.</param>
    /// <param name="SourceFormat">Original subtitle format before normalization.</param>
    /// <param name="SourcePath">Optional path to the original subtitle source file.</param>
    /// <param name="IsDefault">Whether this subtitle should be selected by default.</param>
    public sealed record Item(
        Guid Id,
        string Language,
        string? Label,
        string Format,
        EntitySubtitleSource Source,
        string StoragePath,
        string SourceFormat,
        string? SourcePath,
        bool IsDefault);

    /// <summary>Adds a subtitle track and returns its identifier.</summary>
    public Guid Add(
        string language,
        string? label,
        string format,
        EntitySubtitleSource source,
        string storagePath,
        string sourceFormat,
        string? sourcePath,
        bool isDefault) {
        var item = new Item(Guid.NewGuid(), language, label, format, source, storagePath, sourceFormat, sourcePath, isDefault);
        AddItem(item);
        return item.Id;
    }

    /// <summary>Removes a subtitle track by identifier.</summary>
    /// <param name="id">Subtitle track identifier.</param>
    /// <returns>True when a track was removed.</returns>
    public bool Remove(Guid id) => RemoveItems(item => item.Id == id) > 0;
}
