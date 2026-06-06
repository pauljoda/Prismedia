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
}
