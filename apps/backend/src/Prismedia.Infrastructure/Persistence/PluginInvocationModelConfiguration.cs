using Microsoft.EntityFrameworkCore;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Persistence;

internal static partial class PrismediaModelConfiguration {
    private static void ConfigurePluginInvocationTables(ModelBuilder modelBuilder) {
        modelBuilder.Entity<PluginInvocationStateRow>(entity => {
            entity.ToTable("plugin_invocation_states");
            entity.HasKey(row => row.PluginId);
            entity.Property(row => row.PluginId).HasColumnName("plugin_id").HasMaxLength(128);
            entity.Property(row => row.NextStartAt).HasColumnName("next_start_at");
        });
        modelBuilder.Entity<PluginInvocationLeaseRow>(entity => {
            entity.ToTable("plugin_invocation_leases");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.PluginId).HasColumnName("plugin_id").HasMaxLength(128);
            entity.Property(row => row.ExpiresAt).HasColumnName("expires_at");
            entity.HasOne<PluginInvocationStateRow>().WithMany().HasForeignKey(row => row.PluginId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(row => new { row.PluginId, row.ExpiresAt });
        });
    }
}
