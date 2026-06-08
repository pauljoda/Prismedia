using System.Text.Json;
using NpgsqlTypes;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Infrastructure.Collections;

namespace Prismedia.Infrastructure.Tests;

public sealed class CollectionRuleEngineSqlTests {
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
}
