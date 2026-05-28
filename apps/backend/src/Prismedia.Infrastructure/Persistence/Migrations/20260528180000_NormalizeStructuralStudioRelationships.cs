using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(PrismediaDbContext))]
    [Migration("20260528180000_NormalizeStructuralStudioRelationships")]
    public partial class NormalizeStructuralStudioRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                WITH RECURSIVE relationship_ancestry AS (
                    SELECT
                        link.entity_id AS source_entity_id,
                        link.relationship_code,
                        link.label,
                        link.target_entity_id,
                        link.target_kind_code,
                        link.sort_order,
                        link.metadata_json,
                        link.created_at,
                        entity.id AS current_entity_id,
                        entity.kind_code AS current_kind_code,
                        entity.parent_entity_id,
                        0 AS depth
                    FROM entity_relationship_links AS link
                    INNER JOIN entities AS entity ON entity.id = link.entity_id
                    WHERE link.relationship_code = 'studio'
                        AND entity.kind_code IN ('book-volume', 'book-chapter', 'book-page', 'video-season')

                    UNION ALL

                    SELECT
                        ancestry.source_entity_id,
                        ancestry.relationship_code,
                        ancestry.label,
                        ancestry.target_entity_id,
                        ancestry.target_kind_code,
                        ancestry.sort_order,
                        ancestry.metadata_json,
                        ancestry.created_at,
                        parent.id AS current_entity_id,
                        parent.kind_code AS current_kind_code,
                        parent.parent_entity_id,
                        ancestry.depth + 1 AS depth
                    FROM relationship_ancestry AS ancestry
                    INNER JOIN entities AS parent ON parent.id = ancestry.parent_entity_id
                    WHERE ancestry.current_kind_code NOT IN ('audio', 'audio-library', 'audio-track', 'book', 'gallery', 'image', 'video', 'video-series')
                        AND ancestry.depth < 8
                ),
                promoted_relationships AS (
                    SELECT DISTINCT ON (source_entity_id, relationship_code, target_entity_id)
                        current_entity_id AS owner_entity_id,
                        relationship_code,
                        label,
                        target_entity_id,
                        target_kind_code,
                        sort_order,
                        metadata_json,
                        created_at
                    FROM relationship_ancestry
                    WHERE current_kind_code IN ('audio', 'audio-library', 'audio-track', 'book', 'gallery', 'image', 'video', 'video-series')
                    ORDER BY source_entity_id, relationship_code, target_entity_id, depth
                )
                INSERT INTO entity_relationship_links (
                    entity_id,
                    relationship_code,
                    label,
                    target_entity_id,
                    target_kind_code,
                    sort_order,
                    metadata_json,
                    created_at
                )
                SELECT
                    owner_entity_id,
                    relationship_code,
                    label,
                    target_entity_id,
                    target_kind_code,
                    sort_order,
                    metadata_json,
                    created_at
                FROM promoted_relationships
                ON CONFLICT (entity_id, relationship_code, target_entity_id) DO NOTHING;

                DELETE FROM entity_relationship_links AS link
                USING entities AS entity
                WHERE entity.id = link.entity_id
                    AND link.relationship_code = 'studio'
                    AND entity.kind_code IN ('book-volume', 'book-chapter', 'book-page', 'video-season');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
