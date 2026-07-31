using Prismedia.Application.Files;

namespace Prismedia.Application.Tests;

public sealed class LibraryRootOwnershipResolverTests {
    [Fact]
    public void ResolveSelectsLongestContainingRootRegardlessOfCaller() {
        var outerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var nestedId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var outerPath = Path.Combine(Path.GetTempPath(), "prismedia-root");
        var nestedPath = Path.Combine(outerPath, "private");

        var owner = LibraryRootOwnershipResolver.Resolve(
            Path.Combine(nestedPath, "image.jpg"),
            [
                new LibraryRootPathCandidate(outerId, outerPath),
                new LibraryRootPathCandidate(nestedId, nestedPath)
            ],
            outerId);

        Assert.Equal(nestedId, owner);
    }

    [Fact]
    public void ResolveBreaksEqualPathTiesByRootId() {
        var firstId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var rootPath = Path.Combine(Path.GetTempPath(), "prismedia-tied-root");

        var owner = LibraryRootOwnershipResolver.Resolve(
            Path.Combine(rootPath, "track.flac"),
            [
                new LibraryRootPathCandidate(secondId, rootPath),
                new LibraryRootPathCandidate(firstId, rootPath)
            ]);

        Assert.Equal(firstId, owner);
    }

    [Fact]
    public void ResolveRejectsSourceOutsideCallerRoot() {
        var callerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var otherId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var basePath = Path.Combine(Path.GetTempPath(), "prismedia-caller-root");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            LibraryRootOwnershipResolver.Resolve(
                Path.Combine(basePath, "other", "track.flac"),
                [
                    new LibraryRootPathCandidate(callerId, Path.Combine(basePath, "caller")),
                    new LibraryRootPathCandidate(otherId, Path.Combine(basePath, "other"))
                ],
                callerId));

        Assert.Contains("outside its declared library root", exception.Message, StringComparison.Ordinal);
    }
}
