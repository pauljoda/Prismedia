using Prismedia.Contracts.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Server-derived owned targets. Remote file paths and IDs are deliberately excluded from its stable fingerprint.</summary>
public sealed record OwnedManagedControlScope(ManagedControlScope Scope, string Fingerprint);
