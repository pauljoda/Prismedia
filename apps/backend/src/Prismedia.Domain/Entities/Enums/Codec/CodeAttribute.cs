namespace Prismedia.Domain.Entities;

/// <summary>
/// Declares the stable database/API code for an enum member. The code lives inline
/// on the member it describes; <see cref="EnumCodec{TValue}"/> derives the whole
/// mapping from these attributes, so there is no parallel dictionary to maintain.
/// </summary>
/// <param name="code">Stable text code used by database rows and HTTP contracts.</param>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class CodeAttribute(string code) : Attribute {
    /// <summary>Stable text code for the annotated enum member.</summary>
    public string Code { get; } = code;
}
