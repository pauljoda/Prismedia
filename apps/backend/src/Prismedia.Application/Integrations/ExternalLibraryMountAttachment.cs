using Prismedia.Contracts.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Internal attach outcome used to keep exact request replays free of duplicate scan kickoffs.</summary>
public sealed record ExternalLibraryMountAttachment(ExternalLibraryMount Mount, bool Created);
