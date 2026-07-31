namespace Prismedia.Application.Files;

/// <summary>One configured library root considered when assigning filesystem source ownership.</summary>
/// <param name="Id">Stable library-root identifier.</param>
/// <param name="Path">Configured absolute filesystem path.</param>
public sealed record LibraryRootPathCandidate(Guid Id, string Path);

/// <summary>
/// Resolves deterministic library-root ownership for one filesystem source. Nested roots take
/// precedence over containing roots regardless of scan order, enabled state, or media-kind flags.
/// </summary>
public static class LibraryRootOwnershipResolver {
    /// <summary>
    /// Resolves the longest configured root containing <paramref name="sourcePath"/>. When
    /// <paramref name="callerRootId"/> is supplied, that root must also contain the source so a
    /// malformed scan request cannot assign media outside its declared boundary.
    /// </summary>
    /// <param name="sourcePath">Absolute filesystem source path being assigned.</param>
    /// <param name="roots">Every configured library root, including disabled and kind-ineligible roots.</param>
    /// <param name="callerRootId">Optional root that discovered the source.</param>
    /// <returns>The deterministic owning library-root identifier.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when paths are invalid, the caller root is absent or does not contain the source, or
    /// no configured root contains the source.
    /// </exception>
    public static Guid Resolve(
        string sourcePath,
        IReadOnlyCollection<LibraryRootPathCandidate> roots,
        Guid? callerRootId = null) {
        ArgumentNullException.ThrowIfNull(roots);
        if (string.IsNullOrWhiteSpace(sourcePath) || !Path.IsPathFullyQualified(sourcePath)) {
            throw new InvalidOperationException($"Media source path must be absolute: '{sourcePath}'.");
        }

        string normalizedSource;
        try {
            normalizedSource = Normalize(sourcePath);
        } catch (Exception exception) when (IsInvalidPathException(exception)) {
            throw new InvalidOperationException($"Media source path is invalid: '{sourcePath}'.", exception);
        }

        var normalizedRoots = new List<NormalizedRoot>(roots.Count);
        foreach (var root in roots) {
            if (string.IsNullOrWhiteSpace(root.Path) || !Path.IsPathFullyQualified(root.Path)) {
                throw new InvalidOperationException(
                    $"Library root {root.Id} must have an absolute path: '{root.Path}'.");
            }

            try {
                normalizedRoots.Add(new NormalizedRoot(root.Id, Normalize(root.Path)));
            } catch (Exception exception) when (IsInvalidPathException(exception)) {
                throw new InvalidOperationException(
                    $"Library root {root.Id} has an invalid path: '{root.Path}'.",
                    exception);
            }
        }

        if (callerRootId is { } requiredRootId) {
            var callerRoot = normalizedRoots.SingleOrDefault(root => root.Id == requiredRootId)
                ?? throw new InvalidOperationException(
                    $"Library root {requiredRootId} was not found while assigning '{sourcePath}'.");
            if (!FileSystemPathComparison.IsSameOrDescendant(callerRoot.Path, normalizedSource)) {
                throw new InvalidOperationException(
                    $"Media source '{sourcePath}' is outside its declared library root {requiredRootId}.");
            }
        }

        var owner = normalizedRoots
            .Where(root => FileSystemPathComparison.IsSameOrDescendant(root.Path, normalizedSource))
            .OrderByDescending(root => root.Path.Length)
            .ThenBy(root => root.Id)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"Media source '{sourcePath}' is outside every configured library root.");

        return owner.Id;
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsInvalidPathException(Exception exception) =>
        exception is ArgumentException or NotSupportedException or PathTooLongException;

    private sealed record NormalizedRoot(Guid Id, string Path);
}
