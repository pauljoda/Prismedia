using Prismedia.Application.Jobs;
using Microsoft.Extensions.Logging;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Import;

/// <summary>
/// Coordinates provider-driven metadata imports for entities. Currently a placeholder
/// until the provider/identify system is migrated to the .NET backend.
/// </summary>
[JobDefinition(JobType.ImportMetadata, BlocksAutoIdentify = true)]
public sealed class ImportMetadataJobHandler(ILogger<ImportMetadataJobHandler> logger) : IJobHandler {

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        logger.LogInformation("ImportMetadata: provider imports not yet migrated for {Label}", context.Job.TargetLabel);
        await context.ReportProgressAsync(100, "Provider system pending migration", cancellationToken);
    }
}
