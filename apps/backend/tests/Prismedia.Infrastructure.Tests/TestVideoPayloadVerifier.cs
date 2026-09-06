using Prismedia.Application.Acquisition;

namespace Prismedia.Infrastructure.Tests;

internal sealed class TestVideoPayloadVerifier(string? failure = null) : IVideoPayloadVerifier {
    public List<string> Paths { get; } = [];
    public Task<string?> FindFailureAsync(string filePath, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        Paths.Add(filePath);
        return Task.FromResult(failure);
    }
}
