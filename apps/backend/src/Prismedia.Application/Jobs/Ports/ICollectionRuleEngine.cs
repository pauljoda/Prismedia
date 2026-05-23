using System.Text.Json;
using System.Text.Json.Serialization;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Ports;

/// <summary>
/// Port for evaluating collection rule trees against the entity model.
/// </summary>
public interface ICollectionRuleEngine {
    /// <summary>
    /// Evaluates a rule tree JSON string and returns all matching entity references.
    /// </summary>
    Task<IReadOnlyList<CollectionRuleMatch>> EvaluateAsync(string ruleTreeJson, CancellationToken cancellationToken);
}

/// <summary>
/// A single entity matched by collection rule evaluation.
/// </summary>
/// <param name="EntityKind">The matched entity's kind.</param>
/// <param name="EntityId">The matched entity's ID.</param>
public sealed record CollectionRuleMatch(EntityKind EntityKind, Guid EntityId);

// ── Rule tree types matching the TypeScript contracts ──

/// <summary>
/// Discriminated base for rule tree nodes.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CollectionRuleCondition), "condition")]
[JsonDerivedType(typeof(CollectionRuleGroup), "group")]
public abstract record CollectionRuleNode;

/// <summary>
/// A leaf condition in the rule tree that filters entities by a field/operator/value triple.
/// </summary>
public sealed record CollectionRuleCondition : CollectionRuleNode {
    [JsonPropertyName("entityTypes")]
    public IReadOnlyList<string> EntityTypes { get; init; } = [];

    [JsonPropertyName("field")]
    public string Field { get; init; } = string.Empty;

    [JsonPropertyName("operator")]
    public string Operator { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public JsonElement? Value { get; init; }
}

/// <summary>
/// A group node combining child rules with a logical operator (and, or, not).
/// </summary>
public sealed record CollectionRuleGroup : CollectionRuleNode {
    [JsonPropertyName("operator")]
    public string Operator { get; init; } = "and";

    [JsonPropertyName("children")]
    public IReadOnlyList<CollectionRuleNode> Children { get; init; } = [];
}
