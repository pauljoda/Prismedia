namespace Prismedia.Domain.Entities;

/// <summary>Released-work subtype for one serialized-comic installment.</summary>
public enum ComicInstallmentKind {
    /// <summary>Manga or webcomic chapter released within a continuing title.</summary>
    [Code("chapter")]
    Chapter,

    /// <summary>Numbered western-comic issue released within a run.</summary>
    [Code("issue")]
    Issue,

    /// <summary>Special release that belongs to the title but not its ordinary numbering.</summary>
    [Code("special")]
    Special,

    /// <summary>Self-contained comic released without a continuing sequence expectation.</summary>
    [Code("one-shot")]
    OneShot
}
