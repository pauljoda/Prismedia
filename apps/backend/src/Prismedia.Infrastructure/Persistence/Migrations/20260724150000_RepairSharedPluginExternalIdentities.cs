using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(PrismediaDbContext))]
    [Migration("20260724150000_RepairSharedPluginExternalIdentities")]
    public partial class RepairSharedPluginExternalIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                WITH polluted AS (
                    SELECT identity.entity_id, identity.provider, identity.value
                    FROM entity_external_ids AS identity
                    INNER JOIN entities AS entity ON entity.id = identity.entity_id
                    WHERE
                      (entity.kind_code IN ('book-volume', 'book-chapter')
                        AND identity.provider IN ('mangadex', 'volume', 'chapternumber'))
                      OR
                      (entity.parent_entity_id IS NOT NULL
                        AND identity.provider = 'openlibraryseries')
                      OR
                      (entity.parent_entity_id IS NOT NULL
                        AND entity.kind_code = 'video'
                        AND identity.provider = 'anilist')
                      OR
                      (entity.parent_entity_id IS NOT NULL
                        AND entity.kind_code = 'audio-track'
                        AND identity.provider IN ('musicbrainz', 'musicbrainzrecording'))
                )
                DELETE FROM entity_provider_identities AS binding
                USING polluted
                WHERE binding.entity_id = polluted.entity_id
                  AND binding.identity_namespace = polluted.provider
                  AND binding.identity_value = polluted.value;

                WITH polluted AS (
                    SELECT identity.id
                    FROM entity_external_ids AS identity
                    INNER JOIN entities AS entity ON entity.id = identity.entity_id
                    WHERE
                      (entity.kind_code IN ('book-volume', 'book-chapter')
                        AND identity.provider IN ('mangadex', 'volume', 'chapternumber'))
                      OR
                      (entity.parent_entity_id IS NOT NULL
                        AND identity.provider = 'openlibraryseries')
                      OR
                      (entity.parent_entity_id IS NOT NULL
                        AND entity.kind_code = 'video'
                        AND identity.provider = 'anilist')
                      OR
                      (entity.parent_entity_id IS NOT NULL
                        AND entity.kind_code = 'audio-track'
                        AND identity.provider IN ('musicbrainz', 'musicbrainzrecording'))
                )
                DELETE FROM entity_external_ids AS identity
                USING polluted
                WHERE identity.id = polluted.id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Invalid shared identities cannot be reconstructed safely.
        }
    }
}
