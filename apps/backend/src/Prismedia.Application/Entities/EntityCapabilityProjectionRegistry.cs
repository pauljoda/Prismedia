using System.Reflection;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using ContractCapability = Prismedia.Contracts.Entities.EntityCapability;

namespace Prismedia.Application.Entities;

/// <summary>
/// Marks a typed entity-capability projector for automatic discovery and defines its stable
/// output position. The order preserves deterministic API documents; registration itself is
/// convention-based and requires no central list.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
internal sealed class EntityCapabilityProjectorAttribute(int order) : Attribute {
    /// <summary>Stable capability position in an entity document.</summary>
    public int Order { get; } = order;
}

/// <summary>
/// All hydrated facts available to a capability projector. This application-owned context keeps
/// transport projection concerns off the domain entity while giving every discovered module the
/// same explicit inputs.
/// </summary>
internal sealed record EntityCapabilityProjectionContext(
    Entity Entity,
    EntityFileManagementState FileManagementState,
    Guid? CurrentUserId,
    IReadOnlyList<EntityCreditMetadata>? ProjectedCreditMetadata,
    IReadOnlySet<EntityKind> SourceBackedChildKinds);

/// <summary>Runtime contract used by the assembly-discovered projection registry.</summary>
internal interface IEntityCapabilityProjector {
    /// <summary>The contract capability emitted by this projector.</summary>
    Type CapabilityType { get; }

    /// <summary>Returns the capability when it applies to the supplied entity, otherwise null.</summary>
    ContractCapability? Project(EntityCapabilityProjectionContext context);
}

/// <summary>
/// Compile-time projection contract for one capability type. Adding a projector requires an
/// implementation of <see cref="Project"/> and the discovery attribute, but no registry edit.
/// </summary>
internal abstract class EntityCapabilityProjector<TCapability> : IEntityCapabilityProjector
    where TCapability : ContractCapability {
    public Type CapabilityType => typeof(TCapability);

    public abstract TCapability? Project(EntityCapabilityProjectionContext context);

    ContractCapability? IEntityCapabilityProjector.Project(EntityCapabilityProjectionContext context) =>
        Project(context);
}

/// <summary>
/// Discovers cross-kind projectors in the Application assembly and combines them with the
/// kind-specific projection owned by the Entity's discovered definition. Shared capability logic
/// joins by adding one attributed projector; kind logic joins at its definition with no second class.
/// </summary>
internal static class EntityCapabilityProjectionRegistry {
    private static readonly ProjectionRegistration[] Registrations = Discover(
        typeof(EntityCapabilityProjectionRegistry).Assembly);

    /// <summary>Contract capability types covered by discovered projection modules.</summary>
    internal static IReadOnlyList<Type> RegisteredCapabilityTypes { get; } =
        Registrations.Select(registration => registration.Projector.CapabilityType)
            .Concat(EntityKindRegistry.All.SelectMany(definition => definition.ProjectedCapabilityTypes))
            .ToArray();

    /// <summary>Concrete projector types found without any hand-maintained registration table.</summary>
    internal static IReadOnlyList<Type> RegisteredProjectionTypes { get; } =
        Registrations.Select(registration => registration.Projector.GetType()).ToArray();

    /// <summary>Runs every discovered projection in stable output order.</summary>
    internal static IReadOnlyList<ContractCapability> Project(
        Entity entity,
        EntityFileManagementState fileManagementState,
        Guid? currentUserId,
        IReadOnlyList<EntityCreditMetadata>? projectedCreditMetadata,
        IReadOnlySet<EntityKind> sourceBackedChildKinds) {
        var context = new EntityCapabilityProjectionContext(
            entity,
            fileManagementState,
            currentUserId,
            projectedCreditMetadata,
            sourceBackedChildKinds);
        var sharedCapabilities = Registrations
            .Select(registration => registration.Projector.Project(context))
            .OfType<ContractCapability>()
            .ToArray();
        var kindCapabilities = entity.Definition.ProjectCapabilities(
            entity,
            new EntityKindProjectionContext(currentUserId));
        var capabilities = sharedCapabilities.Concat(kindCapabilities).ToArray();
        var duplicate = capabilities
            .GroupBy(capability => capability.GetType())
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null) {
            throw new InvalidOperationException(
                $"Entity '{entity.Id}' projected capability '{duplicate.Key.Name}' more than once.");
        }

        return capabilities;
    }

    private static ProjectionRegistration[] Discover(Assembly assembly) =>
        assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } &&
                           typeof(IEntityCapabilityProjector).IsAssignableFrom(type))
            .Select(CreateRegistration)
            .OrderBy(registration => registration.Order)
            .ThenBy(registration => registration.Projector.GetType().FullName, StringComparer.Ordinal)
            .ToArray();

    private static ProjectionRegistration CreateRegistration(Type projectorType) {
        var attribute = projectorType.GetCustomAttribute<EntityCapabilityProjectorAttribute>()
            ?? throw new InvalidOperationException(
                $"Capability projector '{projectorType.FullName}' is missing [EntityCapabilityProjector].");
        var projector = Activator.CreateInstance(projectorType, nonPublic: true) as IEntityCapabilityProjector
            ?? throw new InvalidOperationException(
                $"Capability projector '{projectorType.FullName}' must have a parameterless constructor.");
        return new ProjectionRegistration(attribute.Order, projector);
    }

    private sealed record ProjectionRegistration(int Order, IEntityCapabilityProjector Projector);
}
