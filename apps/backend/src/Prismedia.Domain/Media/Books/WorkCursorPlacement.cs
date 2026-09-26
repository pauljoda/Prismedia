using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media.Books;

/// <summary>Where a listening position places the work's shared progress cursor.</summary>
/// <param name="CurrentEntityId">Entity addressed by the cursor (work or chapter).</param>
/// <param name="Unit">Unit counted by the cursor.</param>
/// <param name="Index">Zero-based cursor position.</param>
/// <param name="Total">Unit total addressed by the cursor.</param>
public sealed record WorkCursorPlacement(Guid CurrentEntityId, ProgressUnit Unit, int Index, int Total);
