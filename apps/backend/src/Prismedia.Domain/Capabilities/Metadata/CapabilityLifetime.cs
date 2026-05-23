namespace Prismedia.Domain.Capabilities;

/// <summary>
/// Mutable semantic lifetime range for entities with meaningful beginning and ending dates.
/// </summary>
public sealed class CapabilityLifetime : EntityCapability {
    public CapabilityLifetime(EntityDate? start = null, EntityDate? end = null, string? label = null) {
        Start = start;
        End = end;
        Label = label;
    }

    public EntityDate? Start { get; private set; }
    public EntityDate? End { get; private set; }
    public string? Label { get; private set; }
}
