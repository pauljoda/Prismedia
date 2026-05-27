namespace Prismedia.Contracts.Entities;

/// <summary>API-facing subtitle or caption track attached to a media entity.</summary>
/// <param name="Id">Stable subtitle track identifier.</param>
/// <param name="Language">BCP-47 or provider language code for the subtitle text.</param>
/// <param name="Label">Optional display label shown to the user.</param>
/// <param name="Format">Served subtitle format, such as vtt.</param>
/// <param name="Source">Stable discovery or generation source code.</param>
/// <param name="StoragePath">Path to the normalized subtitle file served by Prismedia.</param>
/// <param name="SourceFormat">Original subtitle format before normalization.</param>
/// <param name="SourcePath">Optional path to the original subtitle source file.</param>
/// <param name="IsDefault">Whether this subtitle should be selected by default.</param>
public sealed record EntitySubtitle(
    Guid Id,
    string Language,
    string? Label,
    string Format,
    string Source,
    string StoragePath,
    string SourceFormat,
    string? SourcePath,
    bool IsDefault);

/// <summary>API-facing subtitle capability.</summary>
[CapabilityKind("subtitles")]
public sealed record SubtitlesCapability(IReadOnlyList<EntitySubtitle> Items) : EntityCapability;
