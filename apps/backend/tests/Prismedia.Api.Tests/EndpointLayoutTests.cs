namespace Prismedia.Api.Tests;

public sealed class EndpointLayoutTests {
    [Fact]
    public void EndpointFilesLiveInRouteDirectories() {
        var endpointRoot = RepoPath("apps/backend/src/Prismedia.Api/Endpoints");
        var rootFiles = Directory.GetFiles(endpointRoot, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(file => Path.GetFileName(file) ?? string.Empty)
            .Order()
            .ToArray();

        Assert.Equal(["EndpointRouteBuilderExtensions.cs"], rootFiles);
        Assert.False(Directory.Exists(Path.Combine(endpointRoot, "Api")));
    }

    [Fact]
    public void MappedEndpointsDeclareOpenApiSummaries() {
        var endpointRoot = RepoPath("apps/backend/src/Prismedia.Api/Endpoints");
        var offenders = Directory.GetFiles(endpointRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !Path.GetFileName(file).EndsWith("Results.cs", StringComparison.Ordinal))
            .Where(file => !string.Equals(Path.GetFileName(file), "JobRouteValues.cs", StringComparison.Ordinal))
            .Select(file => new { File = file, Source = File.ReadAllText(file) })
            .Where(item => ContainsRouteMapping(item.Source) && !item.Source.Contains(".WithSummary(", StringComparison.Ordinal))
            .Select(item => Path.GetRelativePath(endpointRoot, item.File).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    private static bool ContainsRouteMapping(string source) =>
        source.Contains(".MapGet(", StringComparison.Ordinal) ||
        source.Contains(".MapPost(", StringComparison.Ordinal) ||
        source.Contains(".MapPut(", StringComparison.Ordinal) ||
        source.Contains(".MapPatch(", StringComparison.Ordinal) ||
        source.Contains(".MapDelete(", StringComparison.Ordinal);

    private static string RepoPath(string relativePath) {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate) || Directory.Exists(candidate)) {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not resolve repo path '{relativePath}'.");
    }
}
