namespace Prismedia.Domain.Entities;

/// <summary>
/// One way of consuming a work that keeps its own exact resume position. A kind declares the
/// modalities it supports on its engagement policy; each modality's behavior lives on
/// <see cref="Prismedia.Domain.Capabilities.ConsumptionModalityDefinition"/>.
/// </summary>
public enum ConsumptionModality {
    /// <summary>Reading a readable rendition (EPUB, PDF, or paged chapters).</summary>
    [Code("reading")]
    Reading,

    /// <summary>Listening to the work's playable audio tracks.</summary>
    [Code("listening")]
    Listening
}
