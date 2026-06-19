using System.Text.Json;
using NpgsqlTypes;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Collections;

namespace Prismedia.Infrastructure.Tests;

public sealed class CollectionRuleEngineSqlTests {
    [Theory]
    [MemberData(nameof(RepresentativeRuleCases))]
    public void RepresentativeRuleFieldsBuildSqlForSupportedKinds(
        string field,
        string op,
        object? value,
        string kindCode) {
        var group = new CollectionRuleGroup {
            Operator = "and",
            Children = [
                new CollectionRuleCondition {
                    EntityTypes = [kindCode],
                    Field = field,
                    Operator = op,
                    Value = value is null ? null : JsonSerializer.SerializeToElement(value, value.GetType())
                }
            ]
        };

        var query = new CollectionRuleEngine(null!).BuildQuery(group, kindCode);

        Assert.NotNull(query);
        Assert.Contains("WHERE e.kind_code =", query.Value.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void UniversalRulesUseCollapsedEntityColumns() {
        var group = new CollectionRuleGroup {
            Operator = "and",
            Children = [
                new CollectionRuleCondition {
                    EntityTypes = ["video"],
                    Field = "isNsfw",
                    Operator = "is_true"
                },
                new CollectionRuleCondition {
                    EntityTypes = ["video"],
                    Field = "rating",
                    Operator = "greater_equal",
                    Value = JsonSerializer.SerializeToElement(3)
                }
            ]
        };

        var query = new CollectionRuleEngine(null!).BuildQuery(group, "video");

        Assert.NotNull(query);
        var sql = query.Value.Sql;
        Assert.Contains("e.is_nsfw = true", sql, StringComparison.Ordinal);
        Assert.Contains("e.rating_value >=", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("entity_flags", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("entity_ratings", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("deleted_at", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void DateRulesUseTypedPostgresParameters() {
        var group = new CollectionRuleGroup {
            Operator = "and",
            Children = [
                new CollectionRuleCondition {
                    Field = "date",
                    Operator = "greater_than",
                    Value = JsonSerializer.SerializeToElement("2026-06-01")
                },
                new CollectionRuleCondition {
                    Field = "createdAt",
                    Operator = "less_than",
                    Value = JsonSerializer.SerializeToElement("2026-06-06T00:00:00Z")
                }
            ]
        };

        var query = new CollectionRuleEngine(null!).BuildQuery(group, "video");

        Assert.NotNull(query);
        Assert.Contains("ed.sortable_value > @p", query.Value.Sql, StringComparison.Ordinal);
        Assert.Contains("e.created_at < @p", query.Value.Sql, StringComparison.Ordinal);
        Assert.Contains(query.Value.Parameters, parameter => parameter.NpgsqlDbType == NpgsqlDbType.Date);
        Assert.Contains(query.Value.Parameters, parameter => parameter.NpgsqlDbType == NpgsqlDbType.TimestampTz);
    }

    [Fact]
    public void SeriesRuleWithTitleValueComparesAgainstSeriesTitle() {
        var group = new CollectionRuleGroup {
            Operator = "and",
            Children = [
                new CollectionRuleCondition {
                    Field = "videoSeriesId",
                    Operator = "equals",
                    Value = JsonSerializer.SerializeToElement("The Chair Company")
                }
            ]
        };

        var query = new CollectionRuleEngine(null!).BuildQuery(group, "video");

        Assert.NotNull(query);
        Assert.Contains("series_entity.title", query.Value.Sql, StringComparison.Ordinal);
        Assert.Contains(query.Value.Parameters, parameter =>
            parameter.NpgsqlDbType == NpgsqlDbType.Text &&
            parameter.Value is string value &&
            value == "The Chair Company");
    }

    [Fact]
    public void SeriesRuleWithGuidValueUsesTypedUuidParameter() {
        var seriesId = Guid.NewGuid();
        var group = new CollectionRuleGroup {
            Operator = "and",
            Children = [
                new CollectionRuleCondition {
                    Field = "videoSeriesId",
                    Operator = "equals",
                    Value = JsonSerializer.SerializeToElement(seriesId.ToString("D"))
                }
            ]
        };

        var query = new CollectionRuleEngine(null!).BuildQuery(group, "video");

        Assert.NotNull(query);
        Assert.Contains(query.Value.Parameters, parameter =>
            parameter.NpgsqlDbType == NpgsqlDbType.Uuid &&
            parameter.Value is Guid value &&
            value == seriesId);
    }

    [Fact]
    public void SkipCountRulesUsePlaybackState() {
        var group = new CollectionRuleGroup {
            Operator = "and",
            Children = [
                new CollectionRuleCondition {
                    Field = "skipCount",
                    Operator = "greater_equal",
                    Value = JsonSerializer.SerializeToElement(2)
                }
            ]
        };

        var query = new CollectionRuleEngine(null!).BuildQuery(group, "video");

        Assert.NotNull(query);
        Assert.Contains("LEFT JOIN entity_playback pb ON pb.entity_id = e.id", query.Value.Sql, StringComparison.Ordinal);
        Assert.Contains("COALESCE(pb.skip_count, 0) >=", query.Value.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void RestrictedFieldsOnlyBuildSqlForSupportedKindsWhenEntityTypesAreEmpty() {
        var group = new CollectionRuleGroup {
            Operator = "and",
            Children = [
                new CollectionRuleCondition {
                    Field = "skipCount",
                    Operator = "less_equal",
                    Value = JsonSerializer.SerializeToElement(2)
                }
            ]
        };

        var videoSql = new CollectionRuleEngine(null!).BuildQuery(group, EntityKindRegistry.Video.Code);
        var gallerySql = new CollectionRuleEngine(null!).BuildQuery(group, EntityKindRegistry.Gallery.Code);

        Assert.NotNull(videoSql);
        Assert.Null(gallerySql);
    }

    [Fact]
    public void GeneratedSqlNeverReferencesDroppedDeletedAtColumn() {
        // The entities.deleted_at column was dropped (migration DropEntityDeletedAt);
        // Prismedia is hard-delete only, so any deleted_at predicate is invalid SQL.
        var topLevel = new CollectionRuleGroup {
            Operator = "and",
            Children = [
                new CollectionRuleCondition { EntityTypes = ["video"], Field = "rating", Operator = "is_not_null" }
            ]
        };
        var videoSql = new CollectionRuleEngine(null!).BuildQuery(topLevel, "video");
        Assert.NotNull(videoSql);
        Assert.DoesNotContain("deleted_at", videoSql.Value.Sql, StringComparison.Ordinal);

        // imageCount drives the child-count subquery (the second former deleted_at site).
        var childCount = new CollectionRuleGroup {
            Operator = "and",
            Children = [
                new CollectionRuleCondition {
                    EntityTypes = ["gallery"],
                    Field = "imageCount",
                    Operator = "greater_than",
                    Value = JsonSerializer.SerializeToElement(5)
                }
            ]
        };
        var gallerySql = new CollectionRuleEngine(null!).BuildQuery(childCount, "gallery");
        Assert.NotNull(gallerySql);
        Assert.DoesNotContain("deleted_at", gallerySql.Value.Sql, StringComparison.Ordinal);
    }

    public static IEnumerable<object?[]> RepresentativeRuleCases() {
        var allKinds = new[] {
            EntityKindRegistry.Video.Code,
            EntityKindRegistry.Movie.Code,
            EntityKindRegistry.VideoSeries.Code,
            EntityKindRegistry.Gallery.Code,
            EntityKindRegistry.Image.Code,
            EntityKindRegistry.Book.Code,
            EntityKindRegistry.AudioTrack.Code,
        };

        foreach (var kind in allKinds) {
            yield return Case("title", "contains", "a", kind);
            yield return Case("rating", "greater_equal", 3, kind);
            yield return Case("date", "between", new[] { "2026-01-01", "2026-12-31" }, kind);
            yield return Case("organized", "is_true", null, kind);
            yield return Case("isNsfw", "is_false", null, kind);
            yield return Case("tags", "in", new[] { "Favorite" }, kind);
            yield return Case("performers", "in", new[] { "Performer" }, kind);
            yield return Case("studio", "is_not_null", null, kind);
            yield return Case("createdAt", "greater_than", "2026-01-01T00:00:00Z", kind);
        }

        foreach (var kind in new[] { EntityKindRegistry.Video.Code, EntityKindRegistry.Image.Code, EntityKindRegistry.AudioTrack.Code }) {
            yield return Case("fileSize", "greater_than", 1024, kind);
        }

        foreach (var kind in new[] { EntityKindRegistry.Video.Code, EntityKindRegistry.AudioTrack.Code }) {
            yield return Case("duration", "greater_than", 60, kind);
            yield return Case("playCount", "greater_equal", 1, kind);
            yield return Case("skipCount", "less_equal", 2, kind);
        }

        yield return Case("resolution", "in", new[] { "1080p" }, EntityKindRegistry.Video.Code);
        yield return Case("codec", "equals", "h264", EntityKindRegistry.Video.Code);
        yield return Case("interactive", "is_false", null, EntityKindRegistry.Video.Code);
        yield return Case("videoSeriesId", "equals", "The Chair Company", EntityKindRegistry.Video.Code);
        yield return Case("galleryType", "equals", "folder", EntityKindRegistry.Gallery.Code);
        yield return Case("imageCount", "greater_than", 3, EntityKindRegistry.Gallery.Code);
        yield return Case("width", "greater_than", 640, EntityKindRegistry.Image.Code);
        yield return Case("height", "greater_than", 480, EntityKindRegistry.Image.Code);
        yield return Case("format", "equals", "jpeg", EntityKindRegistry.Image.Code);
        yield return Case("bitRate", "greater_than", 128000, EntityKindRegistry.AudioTrack.Code);
        yield return Case("channels", "equals", 2, EntityKindRegistry.AudioTrack.Code);
    }

    private static object?[] Case(string field, string op, object? value, string kindCode) =>
        [field, op, value, kindCode];
}
