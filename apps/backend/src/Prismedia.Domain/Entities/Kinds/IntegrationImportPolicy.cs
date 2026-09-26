namespace Prismedia.Domain.Entities;

/// <summary>
/// Immutable import rules owned by one Entity kind: the library root it lands in, the exact file formats and
/// media types a source may deliver, the largest accepted file, and, for container kinds such as galleries,
/// the kind each delivered file is imported as.
/// </summary>
public sealed record IntegrationImportPolicy {
    #region Static Variables

    /// <summary>Largest single publication an executor or source may deliver.</summary>
    public const long PublicationByteLimit = 2L * 1024 * 1024 * 1024;

    #endregion

    #region Variables

    /// <summary>Library-root capability the destination must scan.</summary>
    public LibraryRootMediaCapability RootCapability { get; }

    /// <summary>Whether the destination must scan recursively, as ordered galleries need.</summary>
    public bool RequiresRecursiveRoot { get; }

    /// <summary>Kind each delivered file is imported as, or null when files import as this kind itself.</summary>
    public EntityKind? ContentKind { get; }

    /// <summary>File extensions, with their leading dot, accepted as this kind directly.</summary>
    public IReadOnlyList<string> Extensions { get; }

    /// <summary>Declared media types accepted as this kind directly.</summary>
    public IReadOnlyList<string> MediaTypes { get; }

    /// <summary>Largest accepted file of this kind.</summary>
    public long MaximumBytes { get; }

    /// <summary>Whether sources may deliver this kind as one direct file.</summary>
    public bool AcceptsDirectFiles => Extensions.Count > 0;

    /// <summary>Rules for each delivered file: the content kind's for containers, otherwise these.</summary>
    public IntegrationImportPolicy Content => ContentKind is { } kind ? For(kind) : this;

    #endregion

    #region Constructors

    /// <summary>Creates one validated import policy.</summary>
    /// <exception cref="ArgumentException">The formats or limits are inconsistent.</exception>
    public IntegrationImportPolicy(
        LibraryRootMediaCapability rootCapability,
        IReadOnlyList<string> extensions,
        IReadOnlyList<string> mediaTypes,
        long maximumBytes = PublicationByteLimit,
        bool requiresRecursiveRoot = false,
        EntityKind? contentKind = null) {
        if (extensions.Any(extension => !extension.StartsWith('.') || extension.Length < 2)) {
            throw new ArgumentException("Import extensions need their leading dot.", nameof(extensions));
        }

        if (extensions.Count == 0 != (mediaTypes.Count == 0) || extensions.Count == 0 && contentKind is null) {
            throw new ArgumentException("Declare direct formats and media types together, or a content kind for a container.", nameof(extensions));
        }

        if (maximumBytes <= 0 || maximumBytes > PublicationByteLimit) {
            throw new ArgumentException("The byte limit must be positive and within the publication limit.", nameof(maximumBytes));
        }

        RootCapability = rootCapability;
        Extensions = extensions;
        MediaTypes = mediaTypes;
        MaximumBytes = maximumBytes;
        RequiresRecursiveRoot = requiresRecursiveRoot;
        ContentKind = contentKind;
    }

    #endregion

    #region Actions - Lookup

    /// <summary>Whether a connected source or executor can import <paramref name="kind"/>.</summary>
    public static bool Supports(EntityKind kind) => EntityKindRegistry.Describe(kind) is IIntegrationImportKindDefinition;

    /// <summary>Returns the import rules declared by <paramref name="kind"/>'s definition.</summary>
    /// <exception cref="ArgumentException">The kind cannot be imported from a connected source.</exception>
    public static IntegrationImportPolicy For(EntityKind kind) {
        var definition = EntityKindRegistry.Describe(kind);
        return definition is IIntegrationImportKindDefinition importable
            ? importable.IntegrationImport
            : throw new ArgumentException($"{definition.GroupLabel} cannot be imported from a connected source.", nameof(kind));
    }

    #endregion

    #region Actions - Formats

    /// <summary>Whether a delivered file name has a format accepted as this kind directly.</summary>
    public bool AcceptsFileName(string fileName) =>
        Extensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a declared media type, ignoring parameters, is accepted as this kind directly.</summary>
    public bool AcceptsMediaType(string mediaType) =>
        MediaTypes.Contains(mediaType.Split(';', 2)[0].Trim(), StringComparer.OrdinalIgnoreCase);

    #endregion
}
