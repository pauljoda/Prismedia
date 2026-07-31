namespace Prismedia.Domain.Entities;

/// <summary>
/// Overrides the mechanically derived generated-client names for a code family. Most code-bearing
/// types need no annotation; exceptional legacy public names declare themselves at the type.
/// </summary>
/// <param name="constantName">Generated constant name, for example <c>RELATIONSHIP_CODE</c>.</param>
/// <param name="typeName">Generated value type name, for example <c>RelationshipCode</c>.</param>
[AttributeUsage(AttributeTargets.Enum | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class CodeFamilyAttribute(string constantName, string typeName) : Attribute {
    /// <summary>Generated constant name.</summary>
    public string ConstantName { get; } = string.IsNullOrWhiteSpace(constantName)
        ? throw new ArgumentException("Code-family constant name cannot be empty.", nameof(constantName))
        : constantName;

    /// <summary>Generated value type name.</summary>
    public string TypeName { get; } = string.IsNullOrWhiteSpace(typeName)
        ? throw new ArgumentException("Code-family type name cannot be empty.", nameof(typeName))
        : typeName;
}
