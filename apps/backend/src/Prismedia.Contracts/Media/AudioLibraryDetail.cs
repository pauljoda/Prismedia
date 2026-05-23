using Prismedia.Contracts.Entities;

namespace Prismedia.Contracts.Media;

/// <summary>
/// API-facing detail shape for an album, audiobook, podcast, or other audio grouping.
/// Carries no kind-specific extras; the shared envelope is sufficient.
/// </summary>
public sealed record AudioLibraryDetail : EntityDetail;
