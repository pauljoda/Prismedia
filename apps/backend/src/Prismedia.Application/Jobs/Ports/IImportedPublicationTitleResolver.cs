using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Ports;

/// <summary>Recovers a publication's accepted source title when it has no embedded title, independently of its storage filename.</summary>
public interface IImportedPublicationTitleResolver {
    /// <summary>Returns a title only when the retained import journal and exact source bytes establish its provenance.</summary>
    Task<string?> ResolveAsync(Guid libraryRootId, EntityKind kind, string sourcePath, CancellationToken cancellationToken);

    /// <summary>Returns the accepted comic title and issue label when the exact imported archive still matches its journal.</summary>
    Task<ImportedComicPublication?> ResolveComicAsync(Guid libraryRootId, string sourcePath, CancellationToken cancellationToken);
}

/// <summary>Source-supplied identity facts for one verified comic archive.</summary>
public sealed record ImportedComicPublication(string Title, string? IssueLabel);
