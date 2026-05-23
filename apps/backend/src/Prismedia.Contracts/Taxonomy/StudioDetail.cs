using Prismedia.Contracts.Entities;

namespace Prismedia.Contracts.Taxonomy;

/// <summary>
/// API-facing detail shape for a studio, publisher, label, or production-group taxonomy entity.
/// Carries no kind-specific extras; the shared <see cref="EntityDetail" /> envelope is sufficient.
/// </summary>
public sealed record StudioDetail : EntityDetail;
