using Prismedia.Application.Files;

namespace Prismedia.Infrastructure.Tests;

/// <summary>Explicitly unrestricted fixture storage for tests unrelated to external-library protection.</summary>
internal sealed class TestFileMutationGuard : ILibraryFileMutationGuard, IAsyncDisposable {
    public ValueTask<IAsyncDisposable> EnterAsync(IReadOnlyCollection<string> paths, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IAsyncDisposable>(this);
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
