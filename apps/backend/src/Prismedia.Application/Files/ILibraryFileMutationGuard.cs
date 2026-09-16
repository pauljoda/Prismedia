namespace Prismedia.Application.Files;

/// <summary>Serializes library protection changes against filesystem mutations shared by API and worker processes.</summary>
public interface ILibraryFileMutationGuard {
    /// <summary>
    /// Rejects writes, moves, deletes, or hardlink ownership involving externally managed paths and their containing
    /// directories. Hold the returned lease through the complete physical operation. Reads and ordinary copies from
    /// an external source do not need source-path write permission; their destinations still do.
    /// </summary>
    ValueTask<IAsyncDisposable> EnterAsync(IReadOnlyCollection<string> paths, CancellationToken cancellationToken);
}
