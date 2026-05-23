namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of provider runtime shapes supported by the backend.
/// </summary>
public enum ProviderType {
    /// <summary>Provider implemented as first-party .NET code.</summary>
    [Code("native")]
    Native,

    /// <summary>Provider launched as a separate JSON stdin/stdout process.</summary>
    [Code("external-process")]
    ExternalProcess,

    /// <summary>Provider that adapts a Stash-compatible source during import or migration.</summary>
    [Code("stash-compat")]
    StashCompat
}
