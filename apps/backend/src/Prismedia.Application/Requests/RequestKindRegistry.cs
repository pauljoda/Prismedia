using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

/// <summary>
/// Discovers request entries from Entity kind definitions and indexes them for the application flow.
/// </summary>
public static class RequestKindRegistry {
    /// <summary>Every requestable kind, in Discover display order.</summary>
    public static readonly IReadOnlyList<RequestKindDescriptor> All = Discover();

    private static readonly IReadOnlyDictionary<RequestMediaKind, RequestKindDescriptor> ByKind =
        All.ToDictionary(descriptor => descriptor.Kind);

    private static readonly IReadOnlyDictionary<EntityKind, IReadOnlyList<RequestKindDescriptor>>
        CommittableLeavesByEntityKind = All
            .Where(descriptor => descriptor is { IsContainer: false, Committable: true })
            .GroupBy(descriptor => descriptor.WantedEntityKind)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<RequestKindDescriptor>)group.ToArray());

    /// <summary>
    /// Wanted Entity kinds governed by each acquisition-profile kind. Visibility and profile-aware
    /// projections consume this index instead of rebuilding request-family membership.
    /// </summary>
    public static IReadOnlyDictionary<EntityKind, IReadOnlyList<EntityKind>> WantedEntityKindsByProfile { get; } =
        All.Where(descriptor => descriptor is { Committable: true, ProfileEntityKind: not null })
            .GroupBy(descriptor => descriptor.ProfileEntityKind!.Value)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<EntityKind>)group
                    .Select(descriptor => descriptor.WantedEntityKind)
                    .Distinct()
                    .Order()
                    .ToArray());

    /// <summary>The descriptor for a kind, or null when the kind isn't part of the request flow (e.g. the plugin passthrough).</summary>
    public static RequestKindDescriptor? Find(RequestMediaKind kind) =>
        ByKind.GetValueOrDefault(kind);

    /// <summary>
    /// Resolves the committable leaf request for an existing Entity. A supplied rendition selects its
    /// matching rendition-aware descriptor; otherwise the Entity definition's default request entry is
    /// used. Non-rendition kinds deliberately ignore a rendition value inherited from a broader caller.
    /// </summary>
    public static RequestKindDescriptor? FindCommittableEntityRequest(
        EntityKind entityKind,
        BookRendition? bookRendition = null) {
        if (!CommittableLeavesByEntityKind.TryGetValue(entityKind, out var candidates)) {
            return null;
        }

        if (bookRendition is { } requestedRendition) {
            var rendition = candidates.FirstOrDefault(candidate =>
                candidate.BookRendition == requestedRendition);
            if (rendition is not null) {
                return rendition;
            }

            if (candidates.Any(candidate => candidate.BookRendition is not null)) {
                return null;
            }
        }

        return candidates.Single(candidate => candidate.IsDefaultEntityRequest);
    }

    /// <summary>
    /// The child descriptor a container fans out into (an author's books, an artist's albums), or null
    /// when the kind has no child options.
    /// </summary>
    public static RequestKindDescriptor? ChildOf(RequestKindDescriptor descriptor) =>
        descriptor.ChildKind is { } childKind ? Find(childKind) : null;

    /// <summary>Every direct child request descriptor declared by a structural container.</summary>
    public static IReadOnlyList<RequestKindDescriptor> ChildrenOf(RequestKindDescriptor descriptor) =>
        descriptor.ChildKinds
            .Select(Find)
            .Where(child => child is not null)
            .Select(child => child!)
            .ToArray();

    /// <summary>Resolves the direct child descriptor compatible with one proposal Entity kind.</summary>
    public static RequestKindDescriptor? ChildOf(
        RequestKindDescriptor descriptor,
        EntityKind targetKind) =>
        ChildrenOf(descriptor).SingleOrDefault(child =>
            EntityKindRegistry.Describe(child.WantedEntityKind).AcceptsPluginKind(targetKind));

    /// <summary>
    /// Whether the descriptor exposes a direct child kind the request flow can actually commit. This is
    /// deliberately independent of <see cref="RequestKindDescriptor.IsContainer"/>: books, seasons, and
    /// albums can search missing children without running provider-container discovery.
    /// </summary>
    public static bool CanSearchMissingChildren(RequestKindDescriptor descriptor) =>
        ChildrenOf(descriptor).Any(child => child.Committable);

    /// <summary>
    /// Every direct Entity kind eligible for missing-child search, preserving descriptor preference
    /// order while collapsing rendition aliases that resolve to the same Entity kind.
    /// </summary>
    public static IReadOnlyList<EntityKind> MissingChildEntityKinds(RequestKindDescriptor descriptor) =>
        ChildrenOf(descriptor)
            .Where(child => child.Committable)
            .Select(child => child.WantedEntityKind)
            .Distinct()
            .ToArray();

    /// <summary>
    /// Resolves a structural acquisition unit whose import must be checked for still-wanted direct
    /// children. This is descriptor-driven so the monitored fallback used by seasons also applies when a
    /// future album, volume, or other Entity kind opts into child phantom materialization.
    /// </summary>
    public static RequestKindDescriptor? FindChildMaterializingUnit(EntityKind entityKind) =>
        All.FirstOrDefault(descriptor =>
            descriptor.MaterializeChildPhantoms
            && descriptor.WantedEntityKind == entityKind
            && ChildrenOf(descriptor).Any(child => child.Committable));

    private static IReadOnlyList<RequestKindDescriptor> Discover() {
        var entries = EntityKindRegistry.All
            .SelectMany(definition => definition.RequestKinds.Select(descriptor => (definition, descriptor)))
            .ToArray();

        var misplaced = entries.FirstOrDefault(entry =>
            entry.descriptor.WantedEntityKind != entry.definition.Kind);
        if (misplaced.descriptor is not null) {
            throw new InvalidOperationException(
                $"Request kind '{misplaced.descriptor.Kind}' targets '{misplaced.descriptor.WantedEntityKind}' " +
                $"but is declared by Entity kind '{misplaced.definition.Kind}'.");
        }

        var descriptors = entries
            .Select(entry => entry.descriptor)
            .OrderBy(descriptor => descriptor.Kind)
            .ToArray();
        var duplicates = descriptors
            .GroupBy(descriptor => descriptor.Kind)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0) {
            throw new InvalidOperationException(
                $"Request kinds have multiple Entity definitions: {string.Join(", ", duplicates)}.");
        }

        var invalidChildDeclarations = descriptors
            .Where(descriptor => descriptor.ChildKinds.Count != descriptor.ChildKinds.Distinct().Count()
                || descriptor.AdditionalChildKinds is { Count: > 0 } && descriptor.ChildKind is null)
            .Select(descriptor => descriptor.Kind)
            .ToArray();
        if (invalidChildDeclarations.Length > 0) {
            throw new InvalidOperationException(
                $"Request kinds have invalid direct-child declarations: {string.Join(", ", invalidChildDeclarations)}.");
        }

        var expected = Enum.GetValues<RequestMediaKind>().Except([RequestMediaKind.Plugin]);
        var missing = expected.Except(descriptors.Select(descriptor => descriptor.Kind)).ToArray();
        if (missing.Length > 0) {
            throw new InvalidOperationException(
                $"Request kinds are missing Entity definitions: {string.Join(", ", missing)}.");
        }

        var invalidDefaults = descriptors
            .Where(descriptor => descriptor is { IsContainer: false, Committable: true })
            .GroupBy(descriptor => descriptor.WantedEntityKind)
            .Where(group => group.Count(descriptor => descriptor.IsDefaultEntityRequest) != 1)
            .Select(group => group.Key)
            .ToArray();
        if (invalidDefaults.Length > 0) {
            throw new InvalidOperationException(
                $"Committable Entity request leaves must declare exactly one default: {string.Join(", ", invalidDefaults)}.");
        }

        return descriptors;
    }
}
