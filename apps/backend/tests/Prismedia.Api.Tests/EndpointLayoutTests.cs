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
