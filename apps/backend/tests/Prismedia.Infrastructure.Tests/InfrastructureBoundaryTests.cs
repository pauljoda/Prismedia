namespace Prismedia.Infrastructure.Tests;

public sealed class InfrastructureBoundaryTests {
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
    public void ProductionQueueRequestsDoNotRetypeTargetKindLiterals() {
        var sourceFiles = Directory.GetFiles(
            RepoPath("apps/backend/src"),
            "*.cs",
            SearchOption.AllDirectories);
        var offenders = sourceFiles
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
            .Where(file => {
                var source = File.ReadAllText(file);
                return source.Contains("TargetEntityKind: \"", StringComparison.Ordinal) ||
                    source.Contains("TargetEntityKind = \"", StringComparison.Ordinal);
            })
            .Select(file => Path.GetRelativePath(Path.GetDirectoryName(RepoPath("package.json"))!, file).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ContractProjectStaysDataOnly() {
        // Contracts may reference the dependency-free Domain core so DTOs can carry [Code]
        // value objects (typed enums) instead of stringly-typed boundary fields — Domain has no
        // HTTP/EF/serialization dependencies, so this keeps Contracts a pure data layer. It must
        // stay free of Infrastructure, API, EF Core, and ASP.NET so it remains transport- and
        // persistence-agnostic.
        var projectFile = ReadRepoFile("apps/backend/src/Prismedia.Contracts/Prismedia.Contracts.csproj");
        Assert.DoesNotContain("Prismedia.Infrastructure", projectFile, StringComparison.Ordinal);
        Assert.DoesNotContain("Prismedia.Api", projectFile, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", projectFile, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.AspNetCore", projectFile, StringComparison.Ordinal);
    }

    [Fact]
    public void ContractSourceStaysFreeOfOuterLayers() {
        string[] forbidden = ["Prismedia.Infrastructure", "Prismedia.Api", "Microsoft.EntityFrameworkCore"];
        Assert.All(forbidden, namespacePrefix =>
            Assert.Empty(FilesContaining("apps/backend/src/Prismedia.Contracts", namespacePrefix)));
    }

    [Fact]
    public void DomainProjectReferencesNothing() {
        // The Domain core is the innermost layer: no project references (not even
        // Contracts, which references Domain) and no NuGet packages. Everything it
        // needs comes from the base class library.
        var projectFile = ReadRepoFile("apps/backend/src/Prismedia.Domain/Prismedia.Domain.csproj");

        Assert.DoesNotContain("<ProjectReference", projectFile, StringComparison.Ordinal);
        Assert.DoesNotContain("<PackageReference", projectFile, StringComparison.Ordinal);
    }

    [Fact]
    public void DomainSourceStaysDependencyFree() {
        // Belt-and-braces with DomainProjectReferencesNothing: even with no project
        // references, a stray global using or fully-qualified name would signal a
        // layering leak the compiler cannot express.
        string[] forbidden =
        [
            "using Prismedia.Application",
            "using Prismedia.Infrastructure",
            "using Prismedia.Api",
            "using Prismedia.Contracts",
            "using Microsoft.EntityFrameworkCore",
            "using Microsoft.AspNetCore"
        ];

        Assert.All(forbidden, namespacePrefix =>
            Assert.Empty(FilesContaining("apps/backend/src/Prismedia.Domain", namespacePrefix)));
    }

    [Fact]
    public void ApiAndWorkerNeverTouchThePersistenceContextDirectly() {
        // Broader than the endpoint-only gate above: nothing anywhere in the Api or
        // Worker hosts may reach for the EF context or EF Core itself. Persistence is
        // reached exclusively through Application ports implemented in Infrastructure.
        string[] forbidden = ["PrismediaDbContext", "using Microsoft.EntityFrameworkCore"];
        string[] hosts = ["apps/backend/src/Prismedia.Api", "apps/backend/src/Prismedia.Worker"];

        foreach (var host in hosts) {
            Assert.All(forbidden, text => Assert.Empty(FilesContaining(host, text)));
        }
    }

    /// <summary>
    /// Grandfathered oversized files mapped to a hard line ceiling (current size rounded
    /// up to the next 50). Entries may only shrink or disappear: splitting a file removes
    /// its entry, and growing one past its ceiling fails this guard. New files are capped
    /// at <see cref="MaxSourceFileLines"/>.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> OversizedFileCeilings = new Dictionary<string, int> {
        ["apps/backend/src/Prismedia.Application/Jobs/Handlers/Acquisition/AcquisitionImportEngines.cs"] = 2150,
        ["apps/backend/src/Prismedia.Infrastructure/Acquisition/EfAcquisitionStore.cs"] = 1800,
        ["apps/backend/src/Prismedia.Application/Requests/RequestCommitService.cs"] = 1600,
        ["apps/backend/src/Prismedia.Infrastructure/Entities/EfEntityReadService.cs"] = 1400,
        ["apps/backend/src/Prismedia.Infrastructure/Plugins/IdentifyQueueService.cs"] = 1350,
        ["apps/backend/src/Prismedia.Infrastructure/Acquisition/EfMonitorStore.cs"] = 1350,
        ["apps/backend/src/Prismedia.Application/Acquisition/AcquisitionService.cs"] = 1200,
        ["apps/backend/src/Prismedia.Infrastructure/Entities/MediaEntityDeletionService.cs"] = 1200,
    };

    private const int MaxSourceFileLines = 1000;

    [Fact]
    public void BackendSourceFilesStayUnderTheModularityCeiling() {
        // The 2026-07 audit found god files regrowing after earlier splits because no
        // guard capped them. Generated EF migrations are exempt; everything else stays
        // under the ceiling or carries an explicit, shrink-only grandfather entry.
        var root = Path.GetDirectoryName(RepoPath("package.json"))!;
        var offenders = Directory.GetFiles(RepoPath("apps/backend/src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}") &&
                !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(file => (
                Path: Path.GetRelativePath(root, file).Replace('\\', '/'),
                Lines: File.ReadLines(file).Count()))
            .Where(entry => entry.Lines > OversizedFileCeilings.GetValueOrDefault(entry.Path, MaxSourceFileLines))
            .OrderByDescending(entry => entry.Lines)
            .Select(entry => $"{entry.Path}: {entry.Lines} lines (ceiling {OversizedFileCeilings.GetValueOrDefault(entry.Path, MaxSourceFileLines)})")
            .ToArray();

        Assert.True(offenders.Length == 0,
            "Source files exceed the modularity ceiling — split them instead of growing them:\n" +
            string.Join("\n", offenders));
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
