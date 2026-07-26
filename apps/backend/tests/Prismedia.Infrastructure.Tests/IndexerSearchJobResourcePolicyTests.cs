using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;

namespace Prismedia.Infrastructure.Tests;

public sealed class IndexerSearchJobResourcePolicyTests {
    [Fact]
    public async Task ProwlarrDeclaresItsExistingTwoSearchLimit() {
        var policy = Policy(
            [Config(IndexerKind.Prowlarr)],
            [new ProwlarrIndexerClient(new HttpClient())]);

        var requirement = await policy.ResolveAsync(CancellationToken.None);

        Assert.NotNull(requirement);
        Assert.Equal(JobResourceKeys.AcquisitionIndexerSearch, requirement.Key);
        Assert.Equal(2, requirement.Policy.MaxConcurrency);
        Assert.Equal(TimeSpan.Zero, requirement.Policy.MinimumStartInterval);
    }

    [Fact]
    public async Task SlskdMakesAMixedSearchUseTheStrictestDeclaredLimit() {
        var policy = Policy(
            [Config(IndexerKind.Prowlarr), Config(IndexerKind.Slskd)],
            [
                new ProwlarrIndexerClient(new HttpClient()),
                new SlskdIndexerClient(new HttpClient())
            ]);

        var requirement = await policy.ResolveAsync(CancellationToken.None);

        Assert.NotNull(requirement);
        Assert.Equal(1, requirement.Policy.MaxConcurrency);
        Assert.Equal(TimeSpan.Zero, requirement.Policy.MinimumStartInterval);
    }

    [Fact]
    public async Task UndeclaredAdaptersDoNotReceiveAnInventedHostLimit() {
        var policy = Policy(
            [Config(IndexerKind.Torznab)],
            [new TorznabIndexerClient(new HttpClient())]);

        Assert.Null(await policy.ResolveAsync(CancellationToken.None));
    }

    private static IndexerSearchJobResourcePolicy Policy(
        IReadOnlyList<IndexerConfigDetail> configs,
        IReadOnlyList<IIndexerSearchClient> clients) =>
        new(new FixedIndexerConfigStore(configs), new IndexerSearchClientFactory(clients));

    private static IndexerConfigDetail Config(IndexerKind kind) => new(
        Guid.NewGuid(),
        kind,
        kind.ToCode(),
        "http://indexer.invalid",
        Enabled: true,
        Priority: 0,
        Categories: [],
        HasApiKey: false,
        ApiKey: null);

    private sealed class FixedIndexerConfigStore(IReadOnlyList<IndexerConfigDetail> configs) : IIndexerConfigStore {
        public Task<IReadOnlyList<IndexerConfigDetail>> ListDetailsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(configs);

        public Task<IReadOnlyList<IndexerConfigSummary>> ListAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IndexerConfigDetail?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IndexerConfigSummary> SaveAsync(
            IndexerConfigSaveCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
