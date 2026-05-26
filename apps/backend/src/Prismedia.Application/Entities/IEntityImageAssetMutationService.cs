namespace Prismedia.Application.Entities;

/// <summary>
/// Outcome of mutating a user-controlled entity image asset.
/// </summary>
public enum EntityImageAssetMutationResult {
    /// <summary>The entity image asset was written or cleared.</summary>
    Updated,

    /// <summary>No active entity with the requested identifier exists.</summary>
    NotFound,

    /// <summary>The requested image role is not supported for manual artwork.</summary>
    UnsupportedRole,

    /// <summary>The uploaded file was not an image payload Prismedia can store.</summary>
    InvalidFile
}

/// <summary>
/// Application port for user-managed entity artwork such as posters, headers, and thumbnails.
/// </summary>
public interface IEntityImageAssetMutationService {
    /// <summary>
    /// Stores an uploaded image as the current asset for one semantic role.
    /// </summary>
    /// <param name="entityId">Entity receiving the uploaded artwork.</param>
    /// <param name="role">Semantic image role code, such as <c>poster</c> or <c>backdrop</c>.</param>
    /// <param name="fileName">Original upload filename used only to infer the image extension.</param>
    /// <param name="contentType">Upload MIME type.</param>
    /// <param name="content">Image stream to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Mutation outcome for HTTP status mapping.</returns>
    Task<EntityImageAssetMutationResult> UploadAsync(
        Guid entityId,
        string role,
        string fileName,
        string? contentType,
        Stream content,
        CancellationToken cancellationToken);

    /// <summary>
    /// Clears the current asset for one semantic role.
    /// </summary>
    /// <param name="entityId">Entity whose artwork should be cleared.</param>
    /// <param name="role">Semantic image role code, such as <c>poster</c> or <c>backdrop</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Mutation outcome for HTTP status mapping.</returns>
    Task<EntityImageAssetMutationResult> ClearAsync(
        Guid entityId,
        string role,
        CancellationToken cancellationToken);
}
