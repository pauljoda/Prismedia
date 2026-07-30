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
        definition.EnumeratesIdentifyChildren;

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
        if (!string.IsNullOrWhiteSpace(code) && ByCode.TryGetValue(code.Trim(), out var definition)) {
            kind = definition.Kind;
            return true;
        }

        kind = default;
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

        var expected = Enum.GetValues<EntityKind>();
        var missing = expected.Except(definitions.Select(definition => definition.Kind)).ToArray();
        if (missing.Length > 0) {
            throw new InvalidOperationException(
                $"Entity kinds are missing definitions: {string.Join(", ", missing)}.");
        }

        return definitions;
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
