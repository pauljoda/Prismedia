namespace Prismedia.Application.Entities;

/// <summary>
/// Describes one entity-attached file that can be streamed by the API.
/// </summary>
/// <param name="EntityId">Entity that owns the file attachment.</param>
/// <param name="Role">Semantic file role requested by the client.</param>
/// <param name="Path">Absolute file path on the server.</param>
/// <param name="ContentType">HTTP content type to use while streaming.</param>
public sealed record EntityFileContent(
    Guid EntityId,
    string Role,
    string Path,
    string ContentType);

/// <summary>
/// Resolves entity file attachments into streamable server-side files.
/// </summary>
public interface IEntityFileContentService {
    /// <summary>
    /// Finds an entity file by semantic role.
    /// </summary>
    /// <param name="entityId">Identifier of the entity that owns the file.</param>
    /// <param name="role">Entity file role code such as <c>source</c> or <c>thumbnail</c>.</param>
    /// <param name="cancellationToken">Cancellation token for the lookup.</param>
    /// <returns>The streamable file, or <c>null</c> when no matching attachment exists.</returns>
    Task<EntityFileContent?> GetContentAsync(
        Guid entityId,
        string role,
        CancellationToken cancellationToken);
}
