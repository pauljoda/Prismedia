using Prismedia.Contracts.Plugins;

namespace Prismedia.Contracts.Entities;

/// <summary>
/// Request body for applying a capability-shaped metadata patch to an entity.
/// Only fields listed in <paramref name="Fields" /> are applied, which lets callers clear
/// included nullable or collection fields while leaving omitted sections unchanged.
/// </summary>
/// <param name="Fields">Patch field keys to apply, such as title, description, urls, dates, or flags. Rating fields are ignored.</param>
/// <param name="Patch">Capability-shaped metadata values for the selected fields.</param>
/// <param name="SelectedImages">Optional selected artwork URLs keyed by image role for plugin-driven updates.</param>
/// <param name="Children">Optional structural child proposals for plugin-driven cascade updates.</param>
/// <param name="Relationships">Optional relationship proposals for plugin-driven artwork cascade updates.</param>
public sealed record EntityMetadataUpdateRequest(
    IReadOnlyList<string> Fields,
    EntityMetadataPatch Patch,
    IReadOnlyDictionary<string, string?>? SelectedImages = null,
    IReadOnlyList<EntityMetadataProposal>? Children = null,
    IReadOnlyList<EntityMetadataProposal>? Relationships = null);
