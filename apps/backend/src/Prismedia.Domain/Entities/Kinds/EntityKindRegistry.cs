using System.Reflection;

namespace Prismedia.Domain.Entities;

/// <summary>
/// Discovers the canonical <see cref="EntityKindDefinition"/> implementations in the Domain
/// assembly and indexes them by typed kind, stable code, CLR type, and definition type. Startup
/// fails unless every <see cref="EntityKind"/> has exactly one complete definition.
/// </summary>
public static class EntityKindRegistry {
    private static readonly IReadOnlyList<EntityKindDefinition> Definitions = Discover();

    private static readonly IReadOnlyDictionary<EntityKind, EntityKindDefinition> ByKind =
        Definitions.ToDictionary(definition => definition.Kind);

    private static readonly IReadOnlyDictionary<string, EntityKindDefinition> ByCode =
        Definitions.ToDictionary(definition => definition.Code, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<Type, EntityKindDefinition> ByEntityType =
        Definitions.Where(definition => definition.ClrType is not null)
            .ToDictionary(definition => definition.ClrType!);

    private static readonly IReadOnlyDictionary<Type, EntityKindDefinition> ByDefinitionType =
        Definitions.ToDictionary(definition => definition.GetType());

    /// <summary>All discovered entity-kind definitions in enum order.</summary>
    public static IReadOnlyList<EntityKindDefinition> All => Definitions;

    /// <summary>Gets the canonical definition for a domain entity kind.</summary>
    public static EntityKindDefinition Describe(EntityKind kind) =>
        ByKind.TryGetValue(kind, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported entity kind.");

    /// <summary>Gets one discovered definition by its concrete definition type.</summary>
    /// <typeparam name="TDefinition">Concrete definition type.</typeparam>
    public static TDefinition Get<TDefinition>()
        where TDefinition : EntityKindDefinition =>
        ByDefinitionType.TryGetValue(typeof(TDefinition), out var definition)
            ? (TDefinition)definition
            : throw new InvalidOperationException(
                $"Entity kind definition '{typeof(TDefinition).Name}' was not discovered.");

    /// <summary>
    /// Whether a stable kind code represents an identify container whose structural children
    /// should be identified independently. Unknown codes are treated as leaves.
    /// </summary>
    public static bool EnumeratesIdentifyChildren(string code) =>
        !string.IsNullOrWhiteSpace(code) &&
        ByCode.TryGetValue(code.Trim(), out var definition) &&
        definition.Identification.EnumeratesChildren;

    /// <summary>Encodes a domain entity kind to its stable storage code.</summary>
    public static string ToCode(EntityKind kind) => Describe(kind).Code;

    /// <summary>Decodes a storage code to a domain entity kind.</summary>
    public static EntityKind Require(string code) =>
        TryGet(code, out var kind)
            ? kind
            : throw new InvalidOperationException($"Unknown entity kind code '{code}'.");

    /// <summary>Gets the entity kind represented by a concrete domain entity CLR type.</summary>
    public static EntityKind RequireType(Type entityType) {
        ArgumentNullException.ThrowIfNull(entityType);
        return ByEntityType.TryGetValue(entityType, out var definition)
            ? definition.Kind
            : throw new InvalidOperationException($"Entity type '{entityType.Name}' is not registered.");
    }

    /// <summary>Attempts to decode a storage code to a domain entity kind.</summary>
    public static bool TryGet(string code, out EntityKind kind) {
        if (TryDescribe(code, out var definition)) {
            kind = definition.Kind;
            return true;
        }

        kind = default;
        return false;
    }

    /// <summary>Attempts to resolve the complete definition represented by a stable kind code.</summary>
    public static bool TryDescribe(string? code, out EntityKindDefinition definition) {
        if (!string.IsNullOrWhiteSpace(code) && ByCode.TryGetValue(code.Trim(), out var resolved)) {
            definition = resolved;
            return true;
        }

        definition = null!;
        return false;
    }

    private static IReadOnlyList<EntityKindDefinition> Discover() {
        var definitionType = typeof(EntityKindDefinition);
        var definitions = definitionType.Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } &&
                           definitionType.IsAssignableFrom(type))
            .Select(Create)
            .OrderBy(definition => definition.Kind)
            .ToArray();

        RejectDuplicates(definitions, definition => definition.Kind, "kind");
        RejectDuplicates(definitions, definition => definition.Code, "code", StringComparer.OrdinalIgnoreCase);
        RejectDuplicates(
            definitions.Where(definition => definition.ClrType is not null).ToArray(),
            definition => definition.ClrType!,
            "CLR type");
        foreach (var definition in definitions) {
            RejectDuplicateStructuralCounts(definition);
        }

        var expected = Enum.GetValues<EntityKind>();
        var missing = expected.Except(definitions.Select(definition => definition.Kind)).ToArray();
        if (missing.Length > 0) {
            throw new InvalidOperationException(
                $"Entity kinds are missing definitions: {string.Join(", ", missing)}.");
        }

        ValidateClientContracts(definitions);
        ValidateIdentificationPolicies(definitions);
        ValidateAcquisitionProfiles(definitions);

        return definitions;
    }

    private static void ValidateIdentificationPolicies(IReadOnlyList<EntityKindDefinition> definitions) {
        foreach (var definition in definitions) {
            if (definition.Identification.PluginFallbackKind == definition.Kind) {
                throw new InvalidOperationException(
                    $"Entity kind '{definition.Code}' cannot identify through itself as a plugin fallback.");
            }
        }
    }

    private static void ValidateClientContracts(IReadOnlyList<EntityKindDefinition> definitions) {
        var byKind = definitions.ToDictionary(definition => definition.Kind);
        var searchable = definitions.Where(definition => definition.Search is not null).ToArray();
        RejectDuplicates(searchable, definition => definition.Search!.Order, "global-search order");

        var expectedOrders = Enumerable.Range(0, searchable.Length).ToArray();
        var actualOrders = searchable.Select(definition => definition.Search!.Order).Order().ToArray();
        if (!expectedOrders.SequenceEqual(actualOrders)) {
            throw new InvalidOperationException(
                $"Entity kind global-search orders must be contiguous from zero; found [{string.Join(", ", actualOrders)}].");
        }

        foreach (var definition in definitions) {
            var navigation = definition.Navigation;
            if (definition.Search is not null && (navigation is null || !navigation.IsTopLevel)) {
                throw new InvalidOperationException(
                    $"Searchable entity kind '{definition.Code}' must declare a top-level detail route.");
            }

            if (navigation is null) {
                continue;
            }

            var canonical = byKind[navigation.CanonicalBrowseKind];
            var canonicalNavigation = canonical.Navigation;
            if (canonicalNavigation is null || canonicalNavigation.CanonicalBrowseKind != canonical.Kind) {
                throw new InvalidOperationException(
                    $"Entity kind '{definition.Code}' targets '{canonical.Code}', which is not a canonical browse kind.");
            }

            if (!navigation.DestinationId.Equals(canonicalNavigation.DestinationId, StringComparison.Ordinal) ||
                !navigation.BrowsePath.Equals(canonicalNavigation.BrowsePath, StringComparison.Ordinal)) {
                throw new InvalidOperationException(
                    $"Entity kind '{definition.Code}' must share destination '{canonicalNavigation.DestinationId}' and " +
                    $"browse path '{canonicalNavigation.BrowsePath}' with canonical kind '{canonical.Code}'.");
            }

            if (navigation.RequiredAncestorKind is { } requiredAncestor &&
                requiredAncestor != navigation.CanonicalBrowseKind) {
                throw new InvalidOperationException(
                    $"Entity kind '{definition.Code}' route requires '{requiredAncestor}', but browses through " +
                    $"'{navigation.CanonicalBrowseKind}'.");
            }
        }
    }

    private static void ValidateAcquisitionProfiles(IReadOnlyList<EntityKindDefinition> definitions) {
        var profiles = definitions
            .Where(definition => definition.AcquisitionProfile is not null)
            .ToDictionary(definition => definition.Kind, definition => definition.AcquisitionProfile!);
        var requestDescriptors = definitions.SelectMany(definition => definition.RequestKinds).ToArray();
        var requestedProfileKinds = requestDescriptors
            .Select(descriptor => descriptor.ProfileEntityKind)
            .OfType<EntityKind>()
            .ToHashSet();

        if (!profiles.Keys.ToHashSet().SetEquals(requestedProfileKinds)) {
            var missing = requestedProfileKinds.Except(profiles.Keys).ToArray();
            var unreferenced = profiles.Keys.Except(requestedProfileKinds).ToArray();
            throw new InvalidOperationException(
                "Acquisition-profile definitions and request descriptors must name exactly the same kinds. " +
                $"Missing definitions: [{string.Join(", ", missing)}]; " +
                $"unreferenced definitions: [{string.Join(", ", unreferenced)}].");
        }

        var profileOrders = profiles.Values.Select(profile => profile.DisplayOrder).Order().ToArray();
        if (!profileOrders.SequenceEqual(Enumerable.Range(0, profiles.Count))) {
            throw new InvalidOperationException(
                "Acquisition-profile display orders must be contiguous from zero; found " +
                $"[{string.Join(", ", profileOrders)}].");
        }

        foreach (var descriptor in requestDescriptors.Where(descriptor => descriptor.ProfileEntityKind is not null)) {
            var profileKind = descriptor.ProfileEntityKind!.Value;
            var expectedCapability = profiles[profileKind].LibraryRootMediaCapability;
            if (descriptor.LibraryRootMediaCapability != expectedCapability) {
                throw new InvalidOperationException(
                    $"Request kind '{descriptor.Kind}' declares root capability " +
                    $"'{descriptor.LibraryRootMediaCapability}', but profile kind '{profileKind}' requires " +
                    $"'{expectedCapability}'.");
            }
        }
    }

    private static void RejectDuplicateStructuralCounts(EntityKindDefinition definition) {
        var duplicateKinds = definition.StructuralThumbnailCounts
            .GroupBy(metric => metric.DescendantKind)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        var duplicateIcons = definition.StructuralThumbnailCounts
            .GroupBy(metric => metric.Icon, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateKinds.Length > 0 || duplicateIcons.Length > 0) {
            throw new InvalidOperationException(
                $"Entity kind definition '{definition.Code}' has duplicate structural thumbnail " +
                $"count kinds [{string.Join(", ", duplicateKinds)}] or icons [{string.Join(", ", duplicateIcons)}].");
        }
    }

    private static EntityKindDefinition Create(Type definitionType) =>
        Activator.CreateInstance(definitionType, nonPublic: true) as EntityKindDefinition
        ?? throw new InvalidOperationException(
            $"Entity kind definition '{definitionType.FullName}' must have a parameterless constructor.");

    private static void RejectDuplicates<TKey>(
        IReadOnlyList<EntityKindDefinition> definitions,
        Func<EntityKindDefinition, TKey> keySelector,
        string label,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull {
        var duplicates = definitions
            .GroupBy(keySelector, comparer)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0) {
            throw new InvalidOperationException(
                $"Duplicate entity kind definition {label}s: {string.Join(", ", duplicates)}.");
        }
    }
}
