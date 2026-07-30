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

    /// <summary>The descriptor for a kind, or null when the kind isn't part of the request flow (e.g. the plugin passthrough).</summary>
    public static RequestKindDescriptor? Find(RequestMediaKind kind) =>
        ByKind.GetValueOrDefault(kind);

    /// <summary>
    /// The child descriptor a container fans out into (an author's books, an artist's albums), or null
    /// when the kind has no child options.
    /// </summary>
    public static RequestKindDescriptor? ChildOf(RequestKindDescriptor descriptor) =>
        descriptor.ChildKind is { } childKind ? Find(childKind) : null;

    /// <summary>
    /// Whether the descriptor exposes a direct child kind the request flow can actually commit. This is
    /// deliberately independent of <see cref="RequestKindDescriptor.IsContainer"/>: books, seasons, and
    /// albums can search missing children without running provider-container discovery.
    /// </summary>
    public static bool CanSearchMissingChildren(RequestKindDescriptor descriptor) =>
        ChildOf(descriptor) is { Committable: true };

    /// <summary>
    /// Resolves a structural acquisition unit whose import must be checked for still-wanted direct
    /// children. This is descriptor-driven so the monitored fallback used by seasons also applies when a
    /// future album, volume, or other Entity kind opts into child phantom materialization.
    /// </summary>
    public static RequestKindDescriptor? FindChildMaterializingUnit(EntityKind entityKind) =>
        All.FirstOrDefault(descriptor =>
            descriptor.MaterializeChildPhantoms
            && descriptor.WantedEntityKind == entityKind
            && ChildOf(descriptor) is { Committable: true });

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

        var expected = Enum.GetValues<RequestMediaKind>().Except([RequestMediaKind.Plugin]);
        var missing = expected.Except(descriptors.Select(descriptor => descriptor.Kind)).ToArray();
        if (missing.Length > 0) {
            throw new InvalidOperationException(
                $"Request kinds are missing Entity definitions: {string.Join(", ", missing)}.");
        }

        return descriptors;
    }
}
