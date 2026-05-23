namespace Prismedia.Infrastructure.Tests;

public sealed class InfrastructureBoundaryTests {
    [Fact]
    public void InfrastructureDoesNotKeepOneToOneServiceInterfaces() {
        var infrastructureAssembly = typeof(Prismedia.Infrastructure.DependencyInjection).Assembly;
        var applicationAssembly = typeof(Prismedia.Application.Jobs.JobService).Assembly;
        var removedInterfaceNames = new[]
        {
            "Prismedia.Infrastructure.Media.IMediaToolService",
            "Prismedia.Infrastructure.Processes.IProcessExecutor",
            "Prismedia.Application.Entities.EntityRepository",
            "Prismedia.Application.Entities.IEntityReadUseCases",
            "Prismedia.Application.Entities.IEntityWriteUseCases",
            "Prismedia.Application.Organization.IEntityOrganizer",
            "Prismedia.Application.Plugins.IBulkIdentifySessions",
            "Prismedia.Application.Plugins.IIdentifyUseCases",
            "Prismedia.Application.Plugins.IPluginCatalogUseCases",
            "Prismedia.Application.UserState.IUserStateService",
            "Prismedia.Infrastructure.Plugins.ApplicationIdentifySessionStore",
            "Prismedia.Infrastructure.Plugins.IdentifyUseCases",
            "Prismedia.Infrastructure.Plugins.PluginCatalogUseCases"
        };

        Assert.All(
            removedInterfaceNames,
            name => Assert.Null(infrastructureAssembly.GetType(name) ?? applicationAssembly.GetType(name)));
    }

    [Fact]
    public void ApplicationProjectDoesNotReferenceInfrastructureOrApiOrEfCore() {
        // Prismedia.Contracts is a pure data-only project (no HTTP/EF dependencies), and
        // Application consumes it directly to avoid duplicating one record per layer for
        // the same flat data. Infrastructure, API, and EF Core remain forbidden from
        // Application so use-case orchestration stays persistence- and transport-agnostic.
        var projectFile = ReadRepoFile("apps/backend/src/Prismedia.Application/Prismedia.Application.csproj");

        Assert.DoesNotContain("Prismedia.Infrastructure", projectFile, StringComparison.Ordinal);
        Assert.DoesNotContain("Prismedia.Api", projectFile, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", projectFile, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationSourceDoesNotUseOuterLayerNamespaces() {
        var sourceFiles = Directory.GetFiles(
            RepoPath("apps/backend/src/Prismedia.Application"),
            "*.cs",
            SearchOption.AllDirectories);

        Assert.All(sourceFiles, file => {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("using Prismedia.Infrastructure", source, StringComparison.Ordinal);
            Assert.DoesNotContain("using Prismedia.Api", source, StringComparison.Ordinal);
            Assert.DoesNotContain("using Microsoft.EntityFrameworkCore", source, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ApiEndpointsDoNotInjectInfrastructurePersistenceOrLowLevelServices() {
        var endpointFiles = Directory.GetFiles(
            RepoPath("apps/backend/src/Prismedia.Api/Endpoints"),
            "*.cs",
            SearchOption.AllDirectories);

        Assert.All(endpointFiles, file => {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("PrismediaDbContext", source, StringComparison.Ordinal);
        });
    }

    private static string ReadRepoFile(string relativePath) => File.ReadAllText(RepoPath(relativePath));

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
