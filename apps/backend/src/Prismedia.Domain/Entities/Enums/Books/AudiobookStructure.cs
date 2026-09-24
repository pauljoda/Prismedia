namespace Prismedia.Domain.Entities;

/// <summary>
/// How a Book's audio rendition divides into chapters, derived from its ordered playable tracks. The
/// enum is the generated identity; <c>AudiobookStructureDefinition</c> carries each structure's rules.
/// </summary>
public enum AudiobookStructure {
    /// <summary>Every track carries embedded chapters read by ffprobe with exact start and end times.</summary>
    [Code("chaptered")]
    Chaptered,

    /// <summary>Several files without embedded chapters that are not part splits: each file may be one chapter.</summary>
    [Code("file_per_chapter")]
    FilePerChapter,

    /// <summary>Several files without embedded chapters that are part, disc, or length splits rather than chapters.</summary>
    [Code("parts")]
    Parts,

    /// <summary>A single file without embedded chapters.</summary>
    [Code("unstructured")]
    Unstructured
}
