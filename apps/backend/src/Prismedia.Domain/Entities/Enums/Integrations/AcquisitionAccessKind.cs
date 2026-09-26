namespace Prismedia.Domain.Entities;

/// <summary>How a source offers a publication. Direct download and explicitly requested preparation are separate acquisition workflows.</summary>
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
    [Code("external")] External,
    /// <summary>Request one exact publication from its source before retrieving the completed file.</summary>
    [Code("request")] Request,
}
