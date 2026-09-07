using Prismedia.Application.Acquisition;

namespace Prismedia.Infrastructure.Tests;

internal sealed class TestVideoPayloadVerifier(string? failure = null) : IVideoPayloadVerifier {
    public List<string> Paths { get; } = [];
    public string? Failure { get; set; } = failure;
    public Func<Task>? BeforeResult { get; set; }
    public async Task<string?> FindFailureAsync(string filePath, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        Paths.Add(filePath);
        if (BeforeResult is not null) await BeforeResult();
        return Failure;
    }
}
