using Prismedia.Contracts.Entities;

namespace Prismedia.Contracts.Media;

/// <summary>
/// API-facing detail shape for a single image entity.
/// Carries no kind-specific extras; the shared envelope is sufficient.
/// </summary>
public sealed record ImageDetail : EntityDetail;
