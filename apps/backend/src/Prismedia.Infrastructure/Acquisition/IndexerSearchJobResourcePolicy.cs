using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// Projects execution policies declared by enabled indexer adapters into the durable graph scheduler.
/// One acquisition-search node may fan out to several adapters, so it uses the strictest concurrency
/// and pacing limits among those that opt into host enforcement.
/// </summary>
public sealed class IndexerSearchJobResourcePolicy(
    IIndexerConfigStore configs,
    IIndexerSearchClientFactory clients) : IAcquisitionSearchResourcePolicy {
    /// <inheritdoc />
    public async Task<JobResourceRequirement?> ResolveAsync(CancellationToken cancellationToken) {
        var policies = (await configs.ListDetailsAsync(cancellationToken))
            .Where(config => config.Enabled)
            .Select(config => config.Kind)
            .Distinct()
            .Select(kind => clients.Get(kind).ExecutionPolicy)
            .OfType<JobExecutionPolicy>()
            .ToArray();
        if (policies.Length == 0) {
            return null;
        }

        return new JobResourceRequirement(
            JobResourceKeys.AcquisitionIndexerSearch,
            new JobExecutionPolicy(
                policies.Min(policy => policy.MaxConcurrency),
                policies.Max(policy => policy.MinimumStartInterval)));
    }
}
