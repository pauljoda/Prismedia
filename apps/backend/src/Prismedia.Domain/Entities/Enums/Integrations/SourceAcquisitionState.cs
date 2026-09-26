namespace Prismedia.Domain.Entities;

/// <summary>Current observations of one requested source item, without inventing a durable executor job or output-retention guarantee.</summary>
public enum SourceAcquisitionState {
    /// <summary>The exact item has neither visible download activity nor a complete retrievable file.</summary>
    [Code("not-observed")] NotObserved,
    /// <summary>The source has queued the exact item.</summary>
    [Code("queued")] Queued,
    /// <summary>The source is downloading the exact item.</summary>
    [Code("downloading")] Downloading,
    /// <summary>The source reports a complete file and confirms its retrieval endpoint is available.</summary>
    [Code("ready")] Ready,
    /// <summary>The source reports an unsuccessful attempt; the accepted local intent remains available for review.</summary>
    [Code("failed")] Failed,
}
