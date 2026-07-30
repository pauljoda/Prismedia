using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;

namespace Prismedia.Application.Tests.Entities;

/// <summary>
/// Guards the entity-document projection extension point so a newly published capability
/// cannot be silently omitted from hydrated entity reads.
/// </summary>
public sealed class EntityCapabilityProjectionRegistryTests {
    [Fact]
    public void EveryProjectionImplementationIsDiscoveredAutomatically() {
        var implementations = typeof(EntityCapabilityProjectionRegistry).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } &&
                           typeof(IEntityCapabilityProjector).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        var discovered = EntityCapabilityProjectionRegistry.RegisteredProjectionTypes
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(implementations, discovered);
    }

    [Fact]
    public void EveryContractCapabilityHasARegisteredProjection() {
        var contractCapabilities = new[] { typeof(EntityCapability).Assembly, typeof(RatingCapability).Assembly }
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } &&
                           typeof(EntityCapability).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        var registeredCapabilities = EntityCapabilityProjectionRegistry.RegisteredCapabilityTypes
            .Distinct()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(contractCapabilities, registeredCapabilities);
    }
}
