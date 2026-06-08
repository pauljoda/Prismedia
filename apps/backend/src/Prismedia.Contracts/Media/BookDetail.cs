using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Media;

/// <summary>
/// API-facing detail shape for a page-based book, comic, or manga entity.
/// </summary>
public sealed record BookDetail : EntityDetail {
    /// <summary>Book category.</summary>
    public required BookType BookType { get; init; }

    /// <summary>Physical format that selects reader and detail behavior.</summary>
    public required BookFormat Format { get; init; }

    /// <summary>Selected cover page entity, when one is set.</summary>
    public required Guid? CoverPageId { get; init; }
}
