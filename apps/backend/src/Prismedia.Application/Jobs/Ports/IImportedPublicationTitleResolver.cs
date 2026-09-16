using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Ports;

/// <summary>Recovers a publication's accepted source title when it has no embedded title, independently of its storage filename.</summary>
public interface IImportedPublicationTitleResolver {
    /// <summary>Returns a title only when the retained import journal and exact source bytes establish its provenance.</summary>
    Task<string?> ResolveAsync(Guid libraryRootId, EntityKind kind, string sourcePath, CancellationToken cancellationToken);
}
