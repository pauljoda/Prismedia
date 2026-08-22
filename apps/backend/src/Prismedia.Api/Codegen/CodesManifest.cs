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

/// <summary>Generated-client symbol names owned by one backend code-bearing type.</summary>
/// <param name="ConstantName">Generated constant object name.</param>
/// <param name="TypeName">Generated code union type name.</param>
public sealed record CodeFamilyManifestEntry(string ConstantName, string TypeName);

/// <summary>Cross-client navigation metadata owned by an Entity-kind definition.</summary>
/// <param name="CanonicalBrowseKind">Entity kind represented by the canonical list destination.</param>
/// <param name="DestinationId">Stable native/app-shell destination identifier.</param>
/// <param name="BrowsePath">Canonical web browse path.</param>
/// <param name="DetailPathTemplate">Optional web detail path template.</param>
/// <param name="RequiredAncestorKind">Ancestor kind required to resolve a nested detail route.</param>
/// <param name="IsTopLevel">Whether detail navigation requires no ancestor context.</param>
public sealed record EntityKindNavigationManifestEntry(
    string CanonicalBrowseKind,
    string DestinationId,
    string BrowsePath,
    string? DetailPathTemplate,
    string? RequiredAncestorKind,
    bool IsTopLevel);

/// <summary>Global-search behavior owned by an Entity-kind definition.</summary>
/// <param name="Order">Stable filter and result-section order.</param>
/// <param name="ExpandsRelationshipResults">Whether direct matches hydrate related entities.</param>
public sealed record EntityKindSearchManifestEntry(int Order, bool ExpandsRelationshipResults);

/// <summary>Manual acquisition behavior owned by an Entity-kind definition.</summary>
/// <param name="SupportsUpload">Whether this kind is a concrete browser upload/import unit.</param>
/// <param name="SupportsReplacement">Whether existing owned content may be replaced after review.</param>
public sealed record EntityManualAcquisitionManifestEntry(
    bool SupportsUpload,
    bool SupportsReplacement);

/// <summary>Acquisition-profile policy projected from an owning Entity-kind definition.</summary>
/// <param name="Label">User-facing profile label.</param>
/// <param name="DisplayOrder">Stable settings-display order among acquisition profiles.</param>
/// <param name="LibraryRootMediaCapability">Required library-root capability code.</param>
/// <param name="SupportedReleaseDateTypes">Ordered automatic-search release milestones.</param>
/// <param name="DefaultNamingTemplate">Default profile path template.</param>
/// <param name="NamingHint">User-facing template guidance.</param>
/// <param name="NamingFamily">Template renderer and validation family code.</param>
public sealed record AcquisitionProfileManifestEntry(
    string Label,
    int DisplayOrder,
    string LibraryRootMediaCapability,
    IReadOnlyList<string> SupportedReleaseDateTypes,
    string DefaultNamingTemplate,
    string NamingHint,
    string NamingFamily);

/// <summary>Rich metadata for an entity kind, used to generate display labels on the frontend.</summary>
/// <param name="Code">Stable kind code.</param>
/// <param name="DisplayName">Singular display name.</param>
/// <param name="GroupLabel">Plural grouping label.</param>
/// <param name="Category">Broad category name.</param>
/// <param name="StorageShape">Filesystem storage shape name.</param>
/// <param name="Icon">Specific semantic presentation icon.</param>
/// <param name="ReferenceIcon">Broader icon used to aggregate reference counts.</param>
/// <param name="ThumbnailWidth">Canonical thumbnail aspect-ratio width component.</param>
/// <param name="ThumbnailHeight">Canonical thumbnail aspect-ratio height component.</param>
/// <param name="PrimaryAccent">Primary shared spectrum hue code.</param>
/// <param name="SecondaryAccent">Secondary shared spectrum hue code.</param>
/// <param name="ArtworkFit">Default artwork scaling behavior.</param>
/// <param name="ArtworkSurface">Client-rendered surface surrounding the original artwork.</param>
/// <param name="Navigation">Cross-client navigation contract, when reachable.</param>
/// <param name="Search">Global-search behavior, when included.</param>
/// <param name="AutoIdentifySelector">Automatic-identification selector family, when directly selectable.</param>
/// <param name="IdentifyPluginFallbackKind">Compatible kind offered to plugins that omit this concrete kind.</param>
/// <param name="ContainableKinds">Entity kinds accepted as direct members, when the kind is a container.</param>
/// <param name="MediaQualityFamily">Acquisition-quality ladder used by the kind.</param>
/// <param name="SupportsFileDeletion">Whether this kind may root the managed delete-files workflow.</param>
/// <param name="SupportsAtomicMediaUpgrade">Whether one owned file can be replaced atomically.</param>
/// <param name="SupportsManualManagement">Whether users may create and delete this kind directly.</param>
/// <param name="ManualAcquisition">Definition-owned browser upload and replacement behavior.</param>
/// <param name="EngagementMode">Completion/filter vocabulary exposed by the kind.</param>
/// <param name="SupportsRequests">Whether a committable request descriptor materializes this Entity kind.</param>
/// <param name="EnumeratesIdentifyChildren">Whether this kind is an identify container whose local children are enumerated for cascade identify.</param>
/// <param name="AcquisitionProfile">Definition-owned acquisition-profile policy, when the kind owns profiles.</param>
public sealed record EntityKindManifestEntry(
    string Code,
    string DisplayName,
    string GroupLabel,
    string Category,
    string StorageShape,
    string Icon,
    string ReferenceIcon,
    int ThumbnailWidth,
    int ThumbnailHeight,
    string PrimaryAccent,
    string SecondaryAccent,
    string ArtworkFit,
    string ArtworkSurface,
    EntityKindNavigationManifestEntry? Navigation,
    EntityKindSearchManifestEntry? Search,
    string? AutoIdentifySelector,
    string? IdentifyPluginFallbackKind,
    IReadOnlyList<string>? ContainableKinds,
    string MediaQualityFamily,
    bool SupportsFileDeletion,
    bool SupportsAtomicMediaUpgrade,
    bool SupportsManualManagement,
    EntityManualAcquisitionManifestEntry ManualAcquisition,
    string EngagementMode,
    bool SupportsRequests,
    bool EnumeratesIdentifyChildren,
    AcquisitionProfileManifestEntry? AcquisitionProfile);

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
/// the same discovered codecs, capability discriminators, provider keys, and setting keys the
/// backend uses — never hand-maintained in parallel.
/// </summary>
/// <param name="Enums">Code-bearing domain enums keyed by enum type name.</param>
/// <param name="CodeFamilies">Generated-client names keyed by code-bearing type name.</param>
/// <param name="EntityKinds">Entity-kind metadata for display-label generation.</param>
/// <param name="RequestKinds">Request-flow metadata projected from <see cref="RequestKindRegistry"/>.</param>
/// <param name="CapabilityKinds">Capability discriminator codes.</param>
/// <param name="ExternalIdProviders">Well-known external-id provider keys.</param>
/// <param name="SettingKeys">App setting keys.</param>
/// <param name="ProblemCodes">Machine-readable API problem codes.</param>
/// <param name="ThumbnailMetaIcons">Stable compact-thumbnail metadata icon codes.</param>
/// <param name="EntityStatCodes">Prismedia-owned persisted statistic codes.</param>
public sealed record CodesManifest(
    IReadOnlyDictionary<string, IReadOnlyList<CodeEntry>> Enums,
    IReadOnlyDictionary<string, CodeFamilyManifestEntry> CodeFamilies,
    IReadOnlyList<EntityKindManifestEntry> EntityKinds,
    IReadOnlyList<RequestKindManifestEntry> RequestKinds,
    IReadOnlyList<string> CapabilityKinds,
    IReadOnlyList<ConstantEntry> ExternalIdProviders,
    IReadOnlyList<ConstantEntry> SettingKeys,
    IReadOnlyList<ConstantEntry> ProblemCodes,
    IReadOnlyList<ConstantEntry> ThumbnailMetaIcons,
    IReadOnlyList<ConstantEntry> EntityStatCodes) {
    /// <summary>Reflects the current backend registries into a fresh manifest.</summary>
    public static CodesManifest Build() {
        var enums = BuildEnums();
        return new(
            enums,
            BuildCodeFamilies(enums.Keys),
            BuildEntityKinds(),
            BuildRequestKinds(),
            CapabilityPolymorphism.DiscriminatorKinds,
            ReflectConstants(typeof(Contracts.Entities.ExternalIdProviders)),
            AppSettingsRegistry.DefinitionsByClientName
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new ConstantEntry(entry.Key, entry.Value.Key))
                .ToArray(),
            ReflectConstants(typeof(Contracts.System.ApiProblemCodes)),
            ReflectConstants(typeof(EntityThumbnailMetaIcons)),
            ReflectConstants(typeof(EntityStatCodes)));
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<CodeEntry>> BuildEnums() {
        var result = new SortedDictionary<string, IReadOnlyList<CodeEntry>>(StringComparer.Ordinal);
        foreach (var enumType in CodeBearingEnums()) {
            if (!CodecRegistry.TryGet(enumType, out var codec)) {
                throw new InvalidOperationException($"Discovered code enum '{enumType.Name}' has no codec.");
            }

            var entries = new List<CodeEntry>();
            foreach (var value in Enum.GetValues(enumType)) {
                var name = Enum.GetName(enumType, value)!;
                entries.Add(new CodeEntry(name, codec!.EncodeObject(value!)));
            }

            result[enumType.Name] = entries;
        }

        return result;
    }

    private static IEnumerable<Type> CodeBearingEnums() =>
        typeof(EntityKind).Assembly.GetTypes()
            .Where(type => type.IsEnum)
            .Where(type => CodecRegistry.TryGet(type, out _));

    private static IReadOnlyDictionary<string, CodeFamilyManifestEntry> BuildCodeFamilies(
        IEnumerable<string> familyNames) {
        var knownTypes = CodeBearingEnums()
            .ToDictionary(type => type.Name, StringComparer.Ordinal);
        var result = familyNames.ToDictionary(
            name => name,
            name => DescribeCodeFamily(knownTypes[name]),
            StringComparer.Ordinal);

        var duplicateConstants = result
            .GroupBy(pair => pair.Value.ConstantName, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        var duplicateTypes = result
            .GroupBy(pair => pair.Value.TypeName, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateConstants is not null || duplicateTypes is not null) {
            throw new InvalidOperationException(
                $"Generated code-family names must be unique; constant '{duplicateConstants?.Key}' or type '{duplicateTypes?.Key}' is duplicated.");
        }

        return result;
    }

    private static CodeFamilyManifestEntry DescribeCodeFamily(Type valueType) {
        var declared = valueType.GetCustomAttribute<CodeFamilyAttribute>();
        return declared is null
            ? new CodeFamilyManifestEntry(ToScreamingSnake(valueType.Name), $"{valueType.Name}Code")
            : new CodeFamilyManifestEntry(declared.ConstantName, declared.TypeName);
    }

    private static string ToScreamingSnake(string name) {
        var result = new System.Text.StringBuilder(name.Length + 8);
        for (var index = 0; index < name.Length; index++) {
            var current = name[index];
            if (index > 0 && char.IsUpper(current) &&
                (char.IsLower(name[index - 1]) || char.IsDigit(name[index - 1]) ||
                 index + 1 < name.Length && char.IsLower(name[index + 1]))) {
                result.Append('_');
            }
            result.Append(char.ToUpperInvariant(current));
        }
        return result.ToString();
    }

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
                descriptor.Presentation.Icon.ToCode(),
                descriptor.Presentation.ReferenceIcon.ToCode(),
                descriptor.Presentation.ThumbnailWidth,
                descriptor.Presentation.ThumbnailHeight,
                descriptor.Presentation.PrimaryAccent.ToCode(),
                descriptor.Presentation.SecondaryAccent.ToCode(),
                descriptor.Presentation.ArtworkFit.ToCode(),
                descriptor.Presentation.ArtworkSurface.ToCode(),
                descriptor.Navigation is { } navigation
                    ? new EntityKindNavigationManifestEntry(
                        navigation.CanonicalBrowseKind.ToCode(),
                        navigation.DestinationId,
                        navigation.BrowsePath,
                        navigation.DetailPathTemplate,
                        navigation.RequiredAncestorKind?.ToCode(),
                        navigation.IsTopLevel)
                    : null,
                descriptor.Search is { } search
                    ? new EntityKindSearchManifestEntry(search.Order, search.ExpandsRelationshipResults)
                    : null,
                descriptor.Identification.AutoIdentifySelector?.ToCode(),
                descriptor.Identification.PluginFallbackKind?.ToCode(),
                descriptor is IEntityContainmentPolicy containment
                    ? containment.ContainableKinds.Select(kind => kind.ToCode()).ToArray()
                    : null,
                descriptor.MediaQualityFamily.ToCode(),
                descriptor.SupportsFileDeletion,
                descriptor.SupportsAtomicMediaUpgrade,
                descriptor.SupportsManualManagement,
                new EntityManualAcquisitionManifestEntry(
                    descriptor.ManualAcquisition.SupportsUpload,
                    descriptor.ManualAcquisition.SupportsReplacement),
                descriptor.Engagement.Mode.ToCode(),
                requestableKinds.Contains(descriptor.Kind),
                descriptor.Identification.EnumeratesChildren,
                descriptor.AcquisitionProfile is { } acquisitionProfile
                    ? new AcquisitionProfileManifestEntry(
                        acquisitionProfile.Label,
                        acquisitionProfile.DisplayOrder,
                        acquisitionProfile.LibraryRootMediaCapability.ToCode(),
                        acquisitionProfile.SupportedReleaseDateTypes.Select(type => type.ToCode()).ToArray(),
                        acquisitionProfile.DefaultNamingTemplate,
                        acquisitionProfile.NamingHint,
                        acquisitionProfile.NamingFamily.ToCode())
                    : null))
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
                descriptor.ProfileEntityKind is { } profileKind
                    ? EntityKindRegistry.Describe(profileKind).AcquisitionProfile?.LibraryRootMediaCapability.ToCode()
                    : null,
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
