using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Prismedia.Contracts.Entities;

/// <summary>
/// Configures System.Text.Json polymorphism for <see cref="EntityCapability" /> by reflecting
/// over every subtype that carries a <see cref="CapabilityKindAttribute" />. Each capability
/// declares its discriminator next to itself; this resolver wires them onto the base type.
/// </summary>
public static class CapabilityPolymorphism {
    private static readonly Lazy<IReadOnlyList<(Type Type, string Kind)>> DiscoveredCapabilities = new(Discover);

    /// <summary>
    /// Type-info modifier that adds polymorphism options to <see cref="EntityCapability" />.
    /// Wire it into <c>JsonSerializerOptions.TypeInfoResolver</c> on startup.
    /// </summary>
    /// <param name="typeInfo">Type info being configured.</param>
    public static void ConfigureEntityCapabilityPolymorphism(JsonTypeInfo typeInfo) {
        if (typeInfo.Type != typeof(EntityCapability)) {
            return;
        }

        var options = new JsonPolymorphismOptions {
            TypeDiscriminatorPropertyName = "kind",
            IgnoreUnrecognizedTypeDiscriminators = false,
            UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,
        };

        foreach (var (type, kind) in DiscoveredCapabilities.Value) {
            options.DerivedTypes.Add(new JsonDerivedType(type, kind));
        }

        typeInfo.PolymorphismOptions = options;
    }

    private static IReadOnlyList<(Type, string)> Discover() {
        var baseType = typeof(EntityCapability);
        var discovered = baseType.Assembly
            .GetTypes()
            .Where(type => type != baseType && baseType.IsAssignableFrom(type) && !type.IsAbstract)
            .Select(type => (Type: type, Attribute: type.GetCustomAttribute<CapabilityKindAttribute>()))
            .ToArray();

        var missing = discovered.Where(pair => pair.Attribute is null).Select(pair => pair.Type).ToArray();
        if (missing.Length > 0) {
            var names = string.Join(", ", missing.Select(t => t.Name));
            throw new InvalidOperationException(
                $"EntityCapability subtypes are missing [CapabilityKind]: {names}.");
        }

        var byKind = discovered
            .GroupBy(pair => pair.Attribute!.Kind, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToArray();
        if (byKind.Length > 0) {
            var duplicates = string.Join("; ",
                byKind.Select(group => $"{group.Key}: {string.Join(", ", group.Select(pair => pair.Type.Name))}"));
            throw new InvalidOperationException(
                $"Duplicate capability discriminators detected: {duplicates}.");
        }

        return discovered
            .OrderBy(pair => pair.Attribute!.Kind, StringComparer.Ordinal)
            .Select(pair => (pair.Type, pair.Attribute!.Kind))
            .ToArray();
    }
}
