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

    [Fact]
    public void ApiEndpointInfrastructureImportsDoNotGrow() {
        var actual = FilesContaining(
                "apps/backend/src/Prismedia.Api/Endpoints",
                "using Prismedia.Infrastructure")
            .ToArray();

        Assert.Empty(actual);
    }

    [Fact]
    public void ContractProjectDoesNotReferenceDomain() {
        var projectFile = ReadRepoFile("apps/backend/src/Prismedia.Contracts/Prismedia.Contracts.csproj");
        Assert.DoesNotContain("Prismedia.Domain", projectFile, StringComparison.Ordinal);
    }

    [Fact]
    public void ContractSourceDoesNotUseDomainNamespaces() {
        var actual = FilesContaining(
                "apps/backend/src/Prismedia.Contracts",
                "Prismedia.Domain")
            .ToArray();

        Assert.Empty(actual);
    }

    [Fact]
    public void LibraryScanAggregatePortDoesNotRegainDeclaredMembers() {
        var declaredMethods = typeof(Prismedia.Application.Jobs.Ports.ILibraryScanPersistence)
            .GetMethods(System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.DeclaredOnly);

        Assert.Empty(declaredMethods);
    }

    [Fact]
    public void RefreshEntityHandlerUsesNarrowScanPersistencePorts() {
        var constructorTypes = typeof(Prismedia.Application.Jobs.Handlers.Maintenance.RefreshEntityJobHandler)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.DoesNotContain(typeof(Prismedia.Application.Jobs.Ports.ILibraryScanPersistence), constructorTypes);
        Assert.Contains(typeof(Prismedia.Application.Jobs.Ports.IEntityRefreshTreePersistence), constructorTypes);
        Assert.Contains(typeof(Prismedia.Application.Jobs.Ports.ILibraryScanRootPersistence), constructorTypes);
        Assert.Contains(typeof(Prismedia.Application.Jobs.Ports.IDownstreamNeedsPersistence), constructorTypes);
    }

    [Fact]
    public void EntityFileJobHandlersUseNarrowMediaProcessingPersistencePort() {
        var handlerTypes = new[]
        {
            typeof(Prismedia.Application.Jobs.Handlers.Probe.ProbeVideoJobHandler),
            typeof(Prismedia.Application.Jobs.Handlers.Probe.ProbeAudioJobHandler),
            typeof(Prismedia.Application.Jobs.Handlers.Identity.FingerprintJobHandler),
            typeof(Prismedia.Application.Jobs.Handlers.Identity.ExtractSubtitlesJobHandler),
            typeof(Prismedia.Application.Jobs.Handlers.Generate.GeneratePreviewJobHandler),
            typeof(Prismedia.Application.Jobs.Handlers.Generate.GenerateImageThumbnailJobHandler),
            typeof(Prismedia.Application.Jobs.Handlers.Generate.GenerateBookPageThumbnailJobHandler),
            typeof(Prismedia.Application.Jobs.Handlers.Generate.GenerateAudioWaveformJobHandler)
        };

        foreach (var handlerType in handlerTypes) {
            var constructorTypes = handlerType.GetConstructors()
                .Single()
                .GetParameters()
                .Select(parameter => parameter.ParameterType)
                .ToArray();

            Assert.DoesNotContain(typeof(Prismedia.Application.Jobs.Ports.ILibraryScanPersistence), constructorTypes);
            Assert.Contains(typeof(Prismedia.Application.Jobs.Ports.IMediaProcessingStatePersistence), constructorTypes);
        }
    }

    [Fact]
    public void ScanJobHandlersUseNarrowScanPersistencePorts() {
        var handlerTypes = new[]
        {
            typeof(Prismedia.Application.Jobs.Handlers.Scan.ScanLibraryJobHandler),
            typeof(Prismedia.Application.Jobs.Handlers.Scan.ScanGalleryJobHandler),
            typeof(Prismedia.Application.Jobs.Handlers.Scan.ScanBookJobHandler),
            typeof(Prismedia.Application.Jobs.Handlers.Scan.ScanAudioJobHandler)
        };

        foreach (var handlerType in handlerTypes) {
            var constructorTypes = handlerType.GetConstructors()
                .Single()
                .GetParameters()
                .Select(parameter => parameter.ParameterType)
                .ToArray();

            Assert.DoesNotContain(typeof(Prismedia.Application.Jobs.Ports.ILibraryScanPersistence), constructorTypes);
            Assert.Contains(typeof(Prismedia.Application.Jobs.Ports.ILibraryScanRootPersistence), constructorTypes);
            Assert.Contains(typeof(Prismedia.Application.Jobs.Ports.IDownstreamNeedsPersistence), constructorTypes);
        }
    }

    [Fact]
    public void JobServiceUsesNarrowDownstreamNeedsPersistencePort() {
        var constructorTypes = typeof(Prismedia.Application.Jobs.JobService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.DoesNotContain(typeof(Prismedia.Application.Jobs.Ports.ILibraryScanPersistence), constructorTypes);
        Assert.Contains(typeof(Prismedia.Application.Jobs.Ports.IDownstreamNeedsPersistence), constructorTypes);
    }

    private static IEnumerable<string> FilesContaining(string relativeDirectory, string text) {
        var root = Path.GetDirectoryName(RepoPath("package.json")) ??
            throw new DirectoryNotFoundException("Could not resolve repository root.");
        var directory = Path.Combine(root, relativeDirectory);
        return Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains(text, StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal);
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
