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

    private static readonly IReadOnlyDictionary<EntityKind, IReadOnlyList<EntityKind>> AllowedChildKindsByParent =
        Definitions.ToDictionary(
            definition => definition.Kind,
            definition => (IReadOnlyList<EntityKind>)Definitions
                .Where(candidate => candidate.StructurePolicy.AllowsParent(definition.Kind))
                .Select(candidate => candidate.Kind)
                .ToArray());

    private static readonly IReadOnlyDictionary<PlayableVideoScanPlacement, IPlayableVideoKindDefinition>
        PlayableVideoByScanPlacement = BuildPlayableVideoByScanPlacement(Definitions);

    /// <summary>All discovered entity-kind definitions in enum order.</summary>
    public static IReadOnlyList<EntityKindDefinition> All => Definitions;

    /// <summary>Gets the kinds that derive this kind as an allowed direct structural parent.</summary>
    public static IReadOnlyList<EntityKind> AllowedChildKinds(EntityKind parentKind) =>
        AllowedChildKindsByParent.TryGetValue(parentKind, out var childKinds)
            ? childKinds
            : throw new ArgumentOutOfRangeException(nameof(parentKind), parentKind, "Unsupported entity kind.");

    /// <summary>Whether the supplied parent/child kinds form a declared structural edge.</summary>
    public static bool AllowsStructuralChild(EntityKind parentKind, EntityKind childKind) =>
        Describe(childKind).StructurePolicy.AllowsParent(parentKind);

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
    /// Resolves the one directly playable Entity kind declared for a scan placement. Discovery
    /// validates this mapping during startup, so scanner ingress cannot silently fall back to a
    /// legacy generic video kind.
    /// </summary>
    public static EntityKind PlayableVideoKindFor(PlayableVideoScanPlacement placement) =>
        PlayableVideoByScanPlacement.TryGetValue(placement, out var definition)
            ? definition.Kind
            : throw new ArgumentOutOfRangeException(nameof(placement), placement, "Unsupported playable scan placement.");

    /// <summary>
    /// Whether a stable kind code represents an identify container whose structural children
    /// should be identified independently. Unknown codes are treated as leaves.
    /// </summary>
    public static bool EnumeratesIdentifyChildren(string code) =>
        !string.IsNullOrWhiteSpace(code) &&
        ByCode.TryGetValue(code.Trim(), out var definition) &&
        definition.Identification.EnumeratesChildren;

    /// <summary>
    /// Whether a kind represents a taxonomy relationship rather than a structural media entity.
    /// </summary>
    public static bool IsRelationship(this EntityKind kind) =>
        Describe(kind).Category == EntityKindCategory.Taxonomy;

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
        ValidateLibraryVisibilityPolicies(definitions);
        ValidateStructurePolicies(definitions);
        ValidateCatalogVisibilityPolicies(definitions);
        ValidateProgressTopologies(definitions);

        return definitions;
    }

    private static IReadOnlyDictionary<PlayableVideoScanPlacement, IPlayableVideoKindDefinition>
        BuildPlayableVideoByScanPlacement(IReadOnlyList<EntityKindDefinition> definitions) {
        var playableDefinitions = definitions.OfType<IPlayableVideoKindDefinition>().ToArray();
        var expectedPlacements = Enum.GetValues<PlayableVideoScanPlacement>();
        var grouped = playableDefinitions.GroupBy(definition => definition.ScanPlacement).ToArray();
        var duplicates = grouped.Where(group => group.Count() != 1).Select(group => group.Key).ToArray();
        var missing = expectedPlacements.Except(grouped.Select(group => group.Key)).ToArray();
        if (duplicates.Length > 0 || missing.Length > 0) {
            throw new InvalidOperationException(
                "Playable video scan placements must be declared exactly once. " +
                $"Duplicate: [{string.Join(", ", duplicates)}]; missing: [{string.Join(", ", missing)}].");
        }

        return grouped.ToDictionary(group => group.Key, group => group.Single());
    }

    private static void ValidateLibraryVisibilityPolicies(IReadOnlyList<EntityKindDefinition> definitions) {
        var byKind = definitions.ToDictionary(definition => definition.Kind);
        foreach (var definition in definitions.Where(candidate =>
                     candidate.LibraryVisibility.Mode == EntityLibraryVisibilityMode.DescendantRoot)) {
            var descendant = byKind[definition.LibraryVisibility.DescendantKind!.Value];
            if (descendant.LibraryVisibility.Mode != EntityLibraryVisibilityMode.DirectRoot) {
                throw new InvalidOperationException(
                    $"Entity kind '{definition.Code}' derives library visibility from '{descendant.Code}', " +
                    "which must own a direct library root.");
            }
        }
    }

    private static void ValidateIdentificationPolicies(IReadOnlyList<EntityKindDefinition> definitions) {
        foreach (var definition in definitions) {
            if (definition.Identification.PluginFallbackKind == definition.Kind) {
                throw new InvalidOperationException(
                    $"Entity kind '{definition.Code}' cannot identify through itself as a plugin fallback.");
            }
        }
    }

    private static void ValidateStructurePolicies(IReadOnlyList<EntityKindDefinition> definitions) {
        var byKind = definitions.ToDictionary(definition => definition.Kind);
        foreach (var definition in definitions) {
            var policy = definition.StructurePolicy;
            foreach (var parentKind in policy.AllowedParentKinds) {
                if (!byKind.TryGetValue(parentKind, out var parent)) {
                    throw new InvalidOperationException(
                        $"Entity kind '{definition.Code}' declares unknown structural parent '{parentKind}'.");
                }
            }
        }
    }

    private static void ValidateCatalogVisibilityPolicies(IReadOnlyList<EntityKindDefinition> definitions) {
        foreach (var definition in definitions) {
            try {
                definition.CatalogVisibility.ValidateFor(definition.StructurePolicy);
            } catch (ArgumentException exception) {
                throw new InvalidOperationException(
                    $"Entity kind '{definition.Code}' has an invalid catalog visibility policy.",
                    exception);
            }
        }
    }

    private static void ValidateProgressTopologies(IReadOnlyList<EntityKindDefinition> definitions) {
        var byKind = definitions.ToDictionary(definition => definition.Kind);
        foreach (var definition in definitions) {
            switch (definition.ProgressTopology) {
                case EntityProgressTopology.NoneTopology or EntityProgressTopology.DirectTopology:
                    break;
                case EntityProgressTopology.WorkTopology work:
                    RequireProgressKind(byKind, definition, work.WorkKind, "work owner");
                    if (byKind[work.WorkKind].ProgressTopology is not EntityProgressTopology.WorkTopology ownerWork ||
                        ownerWork.WorkKind != work.WorkKind ||
                        !CanReachStructuralAncestor(definition.Kind, work.WorkKind, byKind, new HashSet<EntityKind>())) {
                        throw new InvalidOperationException(
                            $"Entity kind '{definition.Code}' declares work owner '{work.WorkKind}', which must be a reachable matching work topology.");
                    }
                    break;
                case EntityProgressTopology.OrderedContainerTopology container:
                    RequireProgressKind(byKind, definition, container.ItemKind, "ordered item");
                    if (byKind[container.ItemKind].ProgressTopology is not EntityProgressTopology.OrderedRollupTopology itemRollup ||
                        itemRollup.ItemKind != container.ItemKind ||
                        !itemRollup.ContainerKinds.Contains(definition.Kind)) {
                        throw new InvalidOperationException(
                            $"Entity kind '{definition.Code}' must be named by its ordered item's matching roll-up topology.");
                    }
                    break;
                case EntityProgressTopology.OrderedRollupTopology rollup:
                    if (rollup.ItemKind != definition.Kind || rollup.ContainerKinds.Count == 0 ||
                        rollup.ContainerKinds.Distinct().Count() != rollup.ContainerKinds.Count) {
                        throw new InvalidOperationException(
                            $"Entity kind '{definition.Code}' must declare itself once as an ordered item with one or more distinct containers.");
                    }

                    foreach (var containerKind in rollup.ContainerKinds) {
                        RequireProgressKind(byKind, definition, containerKind, "ordered container");
                        if (byKind[containerKind].ProgressTopology is not EntityProgressTopology.OrderedContainerTopology containerTopology ||
                            containerTopology.ItemKind != rollup.ItemKind ||
                            !CanReachStructuralAncestor(definition.Kind, containerKind, byKind, new HashSet<EntityKind>())) {
                            throw new InvalidOperationException(
                                $"Entity kind '{definition.Code}' declares unreachable or incompatible ordered container '{containerKind}'.");
                        }
                    }
                    break;
                default:
                    throw new InvalidOperationException($"Entity kind '{definition.Code}' has an unsupported progress topology.");
            }
        }
    }

    private static void RequireProgressKind(
        IReadOnlyDictionary<EntityKind, EntityKindDefinition> byKind,
        EntityKindDefinition definition,
        EntityKind referencedKind,
        string role) {
        if (!byKind.ContainsKey(referencedKind)) {
            throw new InvalidOperationException(
                $"Entity kind '{definition.Code}' declares unknown {role} '{referencedKind}'.");
        }
    }

    private static bool CanReachStructuralAncestor(
        EntityKind childKind,
        EntityKind ancestorKind,
        IReadOnlyDictionary<EntityKind, EntityKindDefinition> byKind,
        ISet<EntityKind> seen) {
        if (childKind == ancestorKind) {
            return true;
        }
        if (!seen.Add(childKind)) {
            return false;
        }

        var reachable = byKind[childKind].StructurePolicy.AllowedParentKinds.Any(parentKind =>
            byKind.ContainsKey(parentKind) && CanReachStructuralAncestor(parentKind, ancestorKind, byKind, seen));
        seen.Remove(childKind);
        return reachable;
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
