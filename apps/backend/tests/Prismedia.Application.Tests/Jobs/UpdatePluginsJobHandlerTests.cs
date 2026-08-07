using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers.Maintenance;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Jobs;

public sealed class UpdatePluginsJobHandlerTests {
    [Fact]
    public async Task UpdatesOnlyInstalledProvidersThatAdvertiseANewerVersion() {
        var plugins = new RecordingPluginCatalog([
            Provider("tmdb", installed: true, updateAvailable: true),
            Provider("musicbrainz", installed: true, updateAvailable: false),
            Provider("openlibrary", installed: false, updateAvailable: true),
        ]);
        var queue = new ProgressJobQueue();
        var handler = new UpdatePluginsJobHandler(plugins, NullLogger<UpdatePluginsJobHandler>.Instance);

        await handler.HandleAsync(Context(queue), CancellationToken.None);

        Assert.Equal(["tmdb"], plugins.UpdateAttempts);
        var progress = Assert.Single(queue.Progress);
        Assert.Equal(100, progress.Progress);
        Assert.Equal("Updated tmdb to 2.0.0", progress.Message);
    }

    [Fact]
    public async Task ContinuesUpdatingOtherProvidersBeforeReportingAnIndividualFailure() {
        var plugins = new RecordingPluginCatalog([
            Provider("broken", installed: true, updateAvailable: true),
            Provider("healthy", installed: true, updateAvailable: true),
        ], failingProviderId: "broken");
        var queue = new ProgressJobQueue();
        var handler = new UpdatePluginsJobHandler(plugins, NullLogger<UpdatePluginsJobHandler>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(Context(queue), CancellationToken.None));

        Assert.Equal(["broken", "healthy"], plugins.UpdateAttempts);
        Assert.Contains("broken", exception.Message, StringComparison.Ordinal);
        Assert.Contains(queue.Progress, progress => progress.Message == "Updated healthy to 2.0.0");
    }

    private static PluginProvider Provider(string id, bool installed, bool updateAvailable) =>
        new(
            id,
            id,
            "1.0.0",
            installed,
            Enabled: installed,
            IsNsfw: false,
            Supports: [],
            Auth: [],
            MissingAuthKeys: [],
            UpdateAvailable: updateAvailable,
            AvailableVersion: updateAvailable ? "2.0.0" : null);

    private static JobContext Context(IJobQueueService queue) {
        var now = DateTimeOffset.UtcNow;
        return new JobContext(
            new JobRunSnapshot(
                Guid.NewGuid(),
                JobType.UpdatePlugins,
                JobRunStatus.Running,
                Progress: 0,
                Message: null,
                PayloadJson: "{}",
                TargetEntityKind: null,
                TargetEntityId: null,
                TargetLabel: "Automatic plugin updates",
                CreatedAt: now,
                StartedAt: now,
                FinishedAt: null),
            queue);
    }

    private sealed class RecordingPluginCatalog(
        IReadOnlyList<PluginProvider> providers,
        string? failingProviderId = null) : IPluginCatalogService {
        public List<string> UpdateAttempts { get; } = [];

        public Task<IReadOnlyList<PluginProvider>> ListProvidersAsync(CancellationToken cancellationToken) =>
            Task.FromResult(providers);

        public Task<IReadOnlyList<PluginProvider>> ListInstalledProvidersAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PluginProvider>>(providers.Where(provider => provider.Installed).ToArray());

        public Task<PluginProvider?> InstallAsync(string providerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PluginProvider?> UpdateAsync(string providerId, CancellationToken cancellationToken) {
            UpdateAttempts.Add(providerId);
            if (providerId == failingProviderId) {
                throw new IOException("download failed");
            }

            var provider = providers.Single(candidate => candidate.Id == providerId);
            return Task.FromResult<PluginProvider?>(provider with {
                Version = provider.AvailableVersion ?? provider.Version,
                UpdateAvailable = false,
                AvailableVersion = null,
            });
        }

        public Task<bool> RemoveAsync(string providerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> SaveAuthAsync(
            string providerId,
            IReadOnlyDictionary<string, string?> values,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<StashScraperListing>> ListStashScrapersAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ProgressJobQueue : IJobQueueService {
        public List<(int Progress, string? Message)> Progress { get; } = [];

        public Task UpdateProgressAsync(
            Guid id,
            int progress,
            string? message,
            CancellationToken cancellationToken) {
            Progress.Add((progress, message));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<int> EnqueueBatchAsync(IReadOnlyList<EnqueueJobRequest> requests, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<int> CancelAsync(JobType? type, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> CancelRunAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<int> ClearFailuresAsync(JobType? type, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<int> RecoverStaleRunningAsync(string currentWorkerId, TimeSpan staleAfter, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task CompleteAsync(Guid id, string? message, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task FailAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
