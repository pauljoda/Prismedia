using System.Security.Cryptography;
using Prismedia.Application.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Integrations;

namespace Prismedia.Infrastructure.Tests;

public sealed class ImportedPublicationTitleResolverTests {
    [Fact] public async Task RecoversTheFullAcceptedTitleFromExactVerifiedPlacementOnEveryScan() {
        using var fixture = await ImportedTitleFixture.CreateAsync();
        var resolver = new ImportedPublicationTitleResolver(fixture);
        Assert.DoesNotContain(':', Path.GetFileName(fixture.Path));
        Assert.Equal(fixture.Work.Plan.Title, await resolver.ResolveAsync(fixture.Work.Plan.LibraryRootId, EntityKind.Book, fixture.Path, default));
        Assert.Equal(fixture.Work.Plan.Title, await resolver.ResolveAsync(fixture.Work.Plan.LibraryRootId, EntityKind.Book, fixture.Path, default));
        Assert.Equal("original", await File.ReadAllTextAsync(fixture.Path));
    }
    [Fact] public async Task SimilarFilenameWithoutItsAcceptedJournalDoesNotGainAnImportedTitle() {
        using var fixture = await ImportedTitleFixture.CreateAsync();
        fixture.Found = false;
        Assert.Null(await new ImportedPublicationTitleResolver(fixture).ResolveAsync(fixture.Work.Plan.LibraryRootId, EntityKind.Book, fixture.Path, default));
    }
    [Fact] public async Task WrongRootKindOrCopiedPathCannotReuseAnotherPublicationsTitle() {
        using var fixture = await ImportedTitleFixture.CreateAsync(); var resolver = new ImportedPublicationTitleResolver(fixture);
        Assert.Null(await resolver.ResolveAsync(Guid.NewGuid(), EntityKind.Book, fixture.Path, default));
        Assert.Null(await resolver.ResolveAsync(fixture.Work.Plan.LibraryRootId, EntityKind.ComicInstallment, fixture.Path, default));
        var copied = System.IO.Path.Combine(Directory.CreateDirectory(System.IO.Path.Combine(fixture.Directory, "copied")).FullName, System.IO.Path.GetFileName(fixture.Path));
        File.Copy(fixture.Path, copied);
        Assert.Null(await resolver.ResolveAsync(fixture.Work.Plan.LibraryRootId, EntityKind.Book, copied, default));
    }
    [Fact] public async Task SameSizeReplacementAndUnverifiedArtifactsDoNotInheritTheAcceptedTitle() {
        using var fixture = await ImportedTitleFixture.CreateAsync(); var resolver = new ImportedPublicationTitleResolver(fixture);
        await File.WriteAllTextAsync(fixture.Path, "changed!");
        Assert.Null(await resolver.ResolveAsync(fixture.Work.Plan.LibraryRootId, EntityKind.Book, fixture.Path, default));
        await File.WriteAllTextAsync(fixture.Path, "original");
        fixture.Work = fixture.Work with { Transfer = new(fixture.Work.Transfer.State with { VerifiedArtifactIds = [] }) };
        Assert.Null(await resolver.ResolveAsync(fixture.Work.Plan.LibraryRootId, EntityKind.Book, fixture.Path, default));
    }
    [Fact] public async Task OrdinaryFilenamesDoNotReadTheTransferJournal() {
        using var fixture = await ImportedTitleFixture.CreateAsync();
        Assert.Null(await new ImportedPublicationTitleResolver(fixture).ResolveAsync(fixture.Work.Plan.LibraryRootId, EntityKind.Book,
            System.IO.Path.Combine(fixture.Directory, "A normal book.epub"), default));
        Assert.Equal(0, fixture.Reads);
    }
}

/// <summary>Real-byte fixture with an accepted immutable journal; writes and scheduling are intentionally unavailable.</summary>
internal sealed class ImportedTitleFixture : IIntegrationTransferStore, IDisposable {
    internal string Directory { get; } = System.IO.Directory.CreateTempSubdirectory("prismedia-title-").FullName;
    internal string Path { get; private set; } = "";
    internal StoredIntegrationTransfer Work { get; set; } = null!;
    internal bool Found { get; set; } = true;
    internal int Reads { get; private set; }
    internal static async Task<ImportedTitleFixture> CreateAsync(EntityKind kind = EntityKind.Book, byte[]? bytes = null) {
        var fixture = new ImportedTitleFixture();
        var transfer = IntegrationTransfer.CreateSourceDownload(Guid.NewGuid(), Guid.NewGuid());
        bytes ??= "original"u8.ToArray();
        var name = kind == EntityKind.Book ? "book.epub" : "comic.cbz";
        var artifact = new IntegrationArtifact("publication", "selected-item", name, "application/octet-stream", bytes.Length,
            Convert.ToHexStringLower(SHA256.HashData(bytes)), IntegrationArtifactRole.Content);
        transfer.AcceptSourceArtifact(artifact);
        var plan = new IntegrationTransferPlan("A: publication / Part One", kind, Guid.NewGuid(), fixture.Directory, "owner", new string('a', 64));
        fixture.Work = new(transfer, plan, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        fixture.Path = System.IO.Path.Combine(fixture.Directory, IntegrationPublicationNames.FileName(transfer.State.OperationId, plan.Title, artifact.Id, name));
        await File.WriteAllBytesAsync(fixture.Path, bytes);
        return fixture;
    }
    public Task<StoredIntegrationTransfer?> FindAsync(Guid id, CancellationToken token) { Reads++; return Task.FromResult(Found && id == Work.Transfer.State.OperationId ? Work : null); }
    public Task<IReadOnlyList<StoredIntegrationTransfer>> ListAsync(int limit, CancellationToken token) => throw new NotSupportedException();
    public Task<StoredIntegrationTransfer> CreateAsync(IntegrationTransfer transfer, IntegrationTransferPlan plan, CancellationToken token) => throw new NotSupportedException();
    public Task SaveAsync(IntegrationTransfer transfer, long revision, string? error, CancellationToken token) => throw new NotSupportedException();
    public Task RecordErrorAsync(Guid id, long revision, string error, CancellationToken token) => throw new NotSupportedException();
    public Task EnqueueRetryAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    public void Dispose() => System.IO.Directory.Delete(Directory, true);
}
