using System.Reflection;
using Prismedia.Application.Requests;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Codegen;

/// <summary>One enum member's stable code.</summary>
/// <param name="Name">PascalCase domain member name.</param>
/// <param name="Code">Stable wire/storage code.</param>
public sealed record CodeEntry(string Name, string Code);

/// <summary>One named constant string.</summary>
/// <param name="Name">PascalCase constant name.</param>
/// <param name="Value">Constant value.</param>
public sealed record ConstantEntry(string Name, string Value);

/// <summary>Rich metadata for an entity kind, used to generate display labels on the frontend.</summary>
/// <param name="Code">Stable kind code.</param>
/// <param name="DisplayName">Singular display name.</param>
/// <param name="GroupLabel">Plural grouping label.</param>
/// <param name="Category">Broad category name.</param>
/// <param name="StorageShape">Filesystem storage shape name.</param>
/// <param name="SupportsFileDeletion">Whether this kind may root the managed delete-files workflow.</param>
/// <param name="SupportsRequests">Whether a committable request descriptor materializes this Entity kind.</param>
public sealed record EntityKindManifestEntry(
    string Code,
    string DisplayName,
    string GroupLabel,
    string Category,
    string StorageShape,
    bool SupportsFileDeletion,
    bool SupportsRequests);

/// <summary>Frontend-facing request-flow metadata projected from one canonical request descriptor.</summary>
/// <param name="Kind">Stable request-media kind code.</param>
/// <param name="Label">Singular display label.</param>
/// <param name="Plural">Plural display label.</param>
/// <param name="Committable">Whether the request flow may commit this kind.</param>
/// <param name="ChildNoun">Display noun for selectable direct children.</param>
/// <param name="EntityKind">Library Entity kind materialized by the request.</param>
/// <param name="PluginEntityKind">Entity kind used at the plugin protocol boundary.</param>
/// <param name="AcquisitionKind">Entity kind targeted by the concrete acquisition unit.</param>
/// <param name="ProfileKind">Acquisition-profile Entity kind governing the request.</param>
/// <param name="RootFlag">Library-root media flag required by the request.</param>
/// <param name="Discoverable">Whether Discover exposes the kind directly.</param>
/// <param name="ReviewSelection">Proposal-to-target selection strategy.</param>
public sealed record RequestKindManifestEntry(
    string Kind,
    string Label,
    string Plural,
    bool Committable,
    string? ChildNoun,
    string EntityKind,
    string PluginEntityKind,
    string AcquisitionKind,
    string? ProfileKind,
    string? RootFlag,
    bool Discoverable,
    string ReviewSelection);

/// <summary>
/// Serializable snapshot of every backend code registry. It is the single source the
/// frontend code generator reads from so that TypeScript code constants are derived from
/// the same <see cref="CodeAttribute"/> enums, capability discriminators, provider keys,
/// and setting keys the backend uses — never hand-maintained in parallel.
/// </summary>
/// <param name="Enums">Code-bearing domain enums keyed by enum type name.</param>
/// <param name="EntityKinds">Entity-kind metadata for display-label generation.</param>
/// <param name="RequestKinds">Request-flow metadata projected from <see cref="RequestKindRegistry"/>.</param>
/// <param name="CapabilityKinds">Capability discriminator codes.</param>
/// <param name="ExternalIdProviders">Well-known external-id provider keys.</param>
/// <param name="SettingKeys">App setting keys.</param>
/// <param name="ProblemCodes">Machine-readable API problem codes.</param>
public sealed record CodesManifest(
    IReadOnlyDictionary<string, IReadOnlyList<CodeEntry>> Enums,
    IReadOnlyList<EntityKindManifestEntry> EntityKinds,
    IReadOnlyList<RequestKindManifestEntry> RequestKinds,
    IReadOnlyList<string> CapabilityKinds,
    IReadOnlyList<ConstantEntry> ExternalIdProviders,
    IReadOnlyList<ConstantEntry> SettingKeys,
    IReadOnlyList<ConstantEntry> ProblemCodes) {
    /// <summary>Reflects the current backend registries into a fresh manifest.</summary>
    public static CodesManifest Build() => new(
        BuildEnums(),
        BuildEntityKinds(),
        BuildRequestKinds(),
        CapabilityPolymorphism.DiscriminatorKinds,
        ReflectConstants(typeof(Contracts.Entities.ExternalIdProviders)),
        ReflectConstants(typeof(AppSettingKeys)),
        ReflectConstants(typeof(Contracts.System.ApiProblemCodes)));

    private static IReadOnlyDictionary<string, IReadOnlyList<CodeEntry>> BuildEnums() {
        var result = new SortedDictionary<string, IReadOnlyList<CodeEntry>>(StringComparer.Ordinal);
        foreach (var enumType in CodeBearingEnums()) {
            var entries = new List<CodeEntry>();
            foreach (var value in Enum.GetValues(enumType)) {
                var name = Enum.GetName(enumType, value)!;
                var code = enumType.GetField(name)!.GetCustomAttribute<CodeAttribute>()!.Code;
                entries.Add(new CodeEntry(name, code));
            }

            result[enumType.Name] = entries;
        }

        return result;
    }

    private static IEnumerable<Type> CodeBearingEnums() =>
        typeof(EntityKind).Assembly.GetTypes()
            .Where(type => type.IsEnum)
            .Where(type => type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Any(field => field.GetCustomAttribute<CodeAttribute>() is not null));

    private static IReadOnlyList<EntityKindManifestEntry> BuildEntityKinds() {
        var requestableKinds = RequestKindRegistry.All
            .Where(descriptor => descriptor.Committable)
            .Select(descriptor => descriptor.WantedEntityKind)
            .ToHashSet();
        return EntityKindRegistry.All
            .Select(descriptor => new EntityKindManifestEntry(
                descriptor.Code,
                descriptor.DisplayName,
                descriptor.GroupLabel,
                descriptor.Category.ToString(),
                descriptor.StorageShape.ToString(),
                descriptor.SupportsFileDeletion,
                requestableKinds.Contains(descriptor.Value)))
            .ToArray();
    }

    private static IReadOnlyList<RequestKindManifestEntry> BuildRequestKinds() =>
        RequestKindRegistry.All
            .Select(descriptor => new RequestKindManifestEntry(
                descriptor.Kind.ToCode(),
                descriptor.Label,
                descriptor.Plural,
                descriptor.Committable,
                descriptor.ChildNoun,
                descriptor.WantedEntityKind.ToCode(),
                descriptor.PluginEntityKind.ToCode(),
                descriptor.AcquisitionKind.ToCode(),
                descriptor.ProfileEntityKind?.ToCode(),
                descriptor.LibraryRootMediaCapability?.ToCode(),
                descriptor.Discoverable,
                descriptor.ReviewSelection.ToCode()))
            .ToArray();

    private static IReadOnlyList<ConstantEntry> ReflectConstants(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .Select(field => new ConstantEntry(field.Name, (string)field.GetRawConstantValue()!))
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ToArray();
}
