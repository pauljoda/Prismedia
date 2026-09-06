namespace Prismedia.Infrastructure.Persistence.Entities;

/// <summary>
/// A formal title captured from an accepted provider work. The captured route must still match the
/// Entity's current identity before acquisition may use the name as evidence.
/// </summary>
public sealed class EntityAlternativeTitleRow {
    public Guid EntityId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string PluginId { get; set; } = string.Empty;
    public string IdentityNamespace { get; set; } = string.Empty;
    public string IdentityValue { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}
