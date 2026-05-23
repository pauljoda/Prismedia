using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Persistence;

internal static partial class PrismediaModelConfiguration {
    private static void ConfigureTaxonomyDetails(ModelBuilder modelBuilder) {
        modelBuilder.Entity<PersonDetailRow>(entity => {
            entity.ToTable("person_details");
            entity.HasKey(row => row.EntityId);
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.Disambiguation).HasColumnName("disambiguation");
            entity.Property(row => row.Gender).HasColumnName("gender");
            entity.Property(row => row.Country).HasColumnName("country");
            entity.Property(row => row.Ethnicity).HasColumnName("ethnicity");
            entity.Property(row => row.EyeColor).HasColumnName("eye_color");
            entity.Property(row => row.HairColor).HasColumnName("hair_color");
            entity.Property(row => row.Height).HasColumnName("height");
            entity.Property(row => row.Weight).HasColumnName("weight");
            entity.Property(row => row.Measurements).HasColumnName("measurements");
            entity.Property(row => row.Tattoos).HasColumnName("tattoos");
            entity.Property(row => row.Piercings).HasColumnName("piercings");
            entity.HasOne<EntityRow>().WithOne().HasForeignKey<PersonDetailRow>(row => row.EntityId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TagDetailRow>(entity => {
            entity.ToTable("tag_details");
            entity.HasKey(row => row.EntityId);
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.IgnoreAutoTag).HasColumnName("ignore_auto_tag");
            entity.HasOne<EntityRow>().WithOne().HasForeignKey<TagDetailRow>(row => row.EntityId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
