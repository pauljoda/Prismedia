namespace Prismedia.Domain.Entities;

/// <summary>How a source offers a publication. Only Download represents an executable full-content acquisition.</summary>
public enum AcquisitionAccessKind {
    /// <summary>Download full content using the configured access credentials.</summary>
    [Code("download")] Download,
    /// <summary>Borrow through the source's lending workflow.</summary>
    [Code("borrow")] Borrow,
    /// <summary>Purchase through the source's checkout workflow.</summary>
    [Code("purchase")] Purchase,
    /// <summary>A sample rather than the complete requested work.</summary>
    [Code("sample")] Sample,
    /// <summary>The source requires a workflow this adapter cannot execute.</summary>
    [Code("external")] External
}
