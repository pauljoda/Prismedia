using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Entities;

/// <summary>Book-only classification needed to choose its reader and presentation.</summary>
/// <param name="BookType">Semantic book type.</param>
/// <param name="Format">Physical or digital book format.</param>
[CapabilityKind("book-metadata")]
public sealed record BookMetadataCapability(BookType BookType, BookFormat Format) : EntityCapability;
