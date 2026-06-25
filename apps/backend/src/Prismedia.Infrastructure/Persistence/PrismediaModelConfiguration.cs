using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Persistence;

internal static partial class PrismediaModelConfiguration {
    public static void ConfigurePrismediaModel(this ModelBuilder modelBuilder) {
        ConfigureEntityCapabilities(modelBuilder);
        ConfigureMediaDetails(modelBuilder);
        ConfigureMediaPlaybackModel(modelBuilder);
        ConfigureTaxonomyDetails(modelBuilder);
        ConfigureCollections(modelBuilder);
        ConfigureSystemTables(modelBuilder);
        ConfigureAcquisitionTables(modelBuilder);
    }
}
