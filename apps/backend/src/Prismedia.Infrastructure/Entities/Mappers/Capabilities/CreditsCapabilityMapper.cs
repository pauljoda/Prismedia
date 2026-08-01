using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Taxonomy;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers.Capabilities;

/// <summary>
/// Persistence mapper for the <see cref="CapabilityCredits"/> capability. Credits are
/// stored as <see cref="EntityRelationshipLinkRow"/> entries with relationship code
/// <c>credits</c>; each row references a <see cref="Person"/> entity target with the
/// <see cref="CreditRole"/> encoded in <see cref="EntityRelationshipLinkRow.MetadataJson"/>.
///
/// Hydration constructs <see cref="Person"/> entities from the target entity row and
/// person detail row so the credit list is self-contained without requiring the full
/// recursive entity hydration pipeline.
/// </summary>
internal sealed class CreditsCapabilityMapper(PrismediaDbContext db) : IEntityCapabilityMapper {
    private static readonly string CreditsRelationshipCode = RelationshipKind.Credits.ToCode();

    public async Task HydrateAsync(Entity entity, CancellationToken cancellationToken) {
        var links = await db.EntityRelationshipLinks.AsNoTracking()
            .Where(link => link.EntityId == entity.Id &&
                           link.RelationshipCode == CreditsRelationshipCode)
            .OrderBy(link => link.SortOrder)
            .ToArrayAsync(cancellationToken);
        if (links.Length == 0) {
            return;
        }

        var targetIds = links.Select(link => link.TargetEntityId).Distinct().ToArray();
        var personRows = await db.Entities.AsNoTracking()
            .Where(row => targetIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var personDetails = await db.PersonDetails.AsNoTracking()
            .Where(detail => targetIds.Contains(detail.EntityId))
            .ToDictionaryAsync(detail => detail.EntityId, cancellationToken);

        entity.RemoveCapability<CapabilityCredits>();
        var credits = new CapabilityCredits();
        foreach (var link in links) {
            if (!personRows.TryGetValue(link.TargetEntityId, out var row)) {
                continue;
            }

            personDetails.TryGetValue(link.TargetEntityId, out var detail);
            var person = new Person(
                row.Id,
                row.Title,
                detail?.Disambiguation,
                detail?.Gender,
                detail?.Country,
                detail?.Ethnicity,
                detail?.EyeColor,
                detail?.HairColor,
                detail?.Height,
                detail?.Weight,
                detail?.Measurements,
                detail?.Tattoos,
                detail?.Piercings);
            credits.Add(
                person,
                DecodeCreditRole(link.MetadataJson),
                string.IsNullOrEmpty(link.Label) ? null : link.Label);
        }

        entity.AddCapability(credits);
    }

    /// <summary>
    /// Decodes the credit role from the JSON metadata on a relationship link row.
    /// Falls back to <see cref="CreditRole.Person"/> when the metadata is missing or malformed.
    /// </summary>
    private static CreditRole DecodeCreditRole(string? metadataJson) {
        if (string.IsNullOrWhiteSpace(metadataJson)) {
            return CreditRole.Person;
        }

        try {
            var metadata = JsonSerializer.Deserialize<CreditMetadata>(metadataJson);
            return metadata?.Role is { } role && role.TryDecodeAs<CreditRole>(out var decoded)
                ? decoded
                : CreditRole.Person;
        } catch (JsonException) {
            return CreditRole.Person;
        }
    }

    private sealed record CreditMetadata([property: JsonPropertyName("role")] string Role);
}
