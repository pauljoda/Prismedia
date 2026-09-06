using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Infrastructure.Collections;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Tests;

public sealed class CollectionRuleEngineSqlTests {
    [Theory]
    [InlineData(CollectionRuleOperator.In)]
    [InlineData(CollectionRuleOperator.NotIn)]
    public void ResolutionSqlUsesTheOrderedSourceClassification(CollectionRuleOperator op) {
        var query = new CollectionRuleEngine(null!, null!).BuildQuery(
            Rule(CollectionRuleField.Resolution, op, new[] { MediaResolutionTier.FullHd.ToCode() }),
            EntityKind.Movie.ToCode());
        Assert.NotNull(query);
        var (sql, parameters) = query.Value;
        Assert.Contains("CASE WHEN", sql);
        Assert.Contains(op == CollectionRuleOperator.In ? "ELSE NULL END) IN" : "ELSE NULL END) NOT IN", sql);
        var parameterIndex = 1; // First parameter is the selected tier.
        foreach (var tier in MediaResolutionPolicy.Tiers) {
            var width = parameters[parameterIndex++];
            var height = parameters[parameterIndex++];
            var code = parameters[parameterIndex++];
            Assert.Equal(tier.MinimumWidth, width.Value);
            Assert.Equal(tier.MinimumHeight, height.Value);
            Assert.Equal(tier.Tier.ToCode(), code.Value);
            Assert.Contains($"WHEN (t.width >= {width.ParameterName} OR t.height >= {height.ParameterName}) THEN {code.ParameterName}", sql);
        }
    }

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

        var query = new CollectionRuleEngine(null!, null!).BuildQuery(group, kindCode);

        Assert.NotNull(query);
        Assert.Contains("WHERE e.kind_code =", query.Value.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void UniversalRulesUseCollapsedEntityColumns() {
        var group = new CollectionRuleGroup {
            Operator = "and",
            Children = [
                new CollectionRuleCondition {
                    EntityTypes = [EntityKind.Video.ToCode()],
                    Field = CollectionRuleField.IsNsfw.ToCode(),
                    Operator = "is_true"
                },
                new CollectionRuleCondition {
                    EntityTypes = [EntityKind.Video.ToCode()],
                    Field = CollectionRuleField.Rating.ToCode(),
                    Operator = "greater_equal",
                    Value = JsonSerializer.SerializeToElement(3)
                }
            ]
        };

        var query = new CollectionRuleEngine(null!, null!).BuildQuery(group, EntityKind.Video.ToCode());

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
                    Field = CollectionRuleField.Date.ToCode(),
                    Operator = "greater_than",
                    Value = JsonSerializer.SerializeToElement("2026-06-01")
                },
                new CollectionRuleCondition {
                    Field = CollectionRuleField.CreatedAt.ToCode(),
                    Operator = "less_than",
                    Value = JsonSerializer.SerializeToElement("2026-06-06T00:00:00Z")
                }
            ]
        };

        var query = new CollectionRuleEngine(null!, null!).BuildQuery(group, EntityKind.Video.ToCode());

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
                    Field = CollectionRuleField.VideoSeriesId.ToCode(),
                    Operator = "equals",
                    Value = JsonSerializer.SerializeToElement("The Chair Company")
                }
            ]
        };

        var query = new CollectionRuleEngine(null!, null!).BuildQuery(group, EntityKind.VideoEpisode.ToCode());

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
                    Field = CollectionRuleField.VideoSeriesId.ToCode(),
                    Operator = "equals",
                    Value = JsonSerializer.SerializeToElement(seriesId.ToString("D"))
                }
            ]
        };

        var query = new CollectionRuleEngine(null!, null!).BuildQuery(group, EntityKind.VideoEpisode.ToCode());

        Assert.NotNull(query);
        Assert.Contains(query.Value.Parameters, parameter =>
            parameter.NpgsqlDbType == NpgsqlDbType.Uuid &&
            parameter.Value is Guid value &&
            value == seriesId);
    }

    [Fact]
    public void SeriesRuleTargetsExactlyTheDefinitionsMarkedEpisodic() {
        var group = new CollectionRuleGroup {
            Operator = "and",
            Children = [
                new CollectionRuleCondition {
                    Field = CollectionRuleField.VideoSeriesId.ToCode(),
                    Operator = "equals",
                    Value = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString("D"))
                }
            ]
        };
        var engine = new CollectionRuleEngine(null!, null!);

        foreach (var definition in EntityKindRegistry.All.OfType<IPlayableVideoKindDefinition>()) {
            var query = engine.BuildQuery(group, EntityKindRegistry.ToCode(definition.Kind));

            if (definition.IsEpisodic) {
                Assert.NotNull(query);
            } else {
                Assert.Null(query);
            }
        }
    }

    [Fact]
    public void SkipCountRulesUsePlaybackState() {
        var ownerUserId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        var group = new CollectionRuleGroup {
            Operator = "and",
            Children = [
                new CollectionRuleCondition {
                    Field = CollectionRuleField.SkipCount.ToCode(),
                    Operator = "greater_equal",
                    Value = JsonSerializer.SerializeToElement(2)
                }
            ]
        };

        var query = new CollectionRuleEngine(null!, null!).BuildQuery(group, EntityKind.Video.ToCode(), ownerUserId);

        Assert.NotNull(query);
        Assert.Contains("pb.user_id =", query.Value.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("GROUP BY entity_id", query.Value.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("entity_playback pb", query.Value.Sql, StringComparison.Ordinal);
        Assert.Contains("COALESCE(pb.skip_count, 0) >=", query.Value.Sql, StringComparison.Ordinal);
        Assert.Contains(query.Value.Parameters, parameter =>
            parameter.NpgsqlDbType == NpgsqlDbType.Uuid &&
            parameter.Value is Guid value &&
            value == ownerUserId);
    }

    [Fact]
    public void LibraryRuleUsesTypedUuidParameter() {
        var rootId = Guid.NewGuid();
        var group = new CollectionRuleGroup {
            Operator = "and",
            Children = [
                new CollectionRuleCondition {
                    Field = CollectionRuleField.LibraryRootId.ToCode(),
                    Operator = "equals",
                    Value = JsonSerializer.SerializeToElement(rootId.ToString("D"))
                }
            ]
        };

        var query = new CollectionRuleEngine(null!, null!).BuildQuery(group, EntityKind.Video.ToCode());

        Assert.NotNull(query);
        Assert.Contains("entity_library_roots", query.Value.Sql, StringComparison.Ordinal);
        Assert.Contains(query.Value.Parameters, parameter =>
            parameter.NpgsqlDbType == NpgsqlDbType.Uuid &&
            parameter.Value is Guid value &&
            value == rootId);
    }

    [Fact]
    public void AncestorLibraryRootRulesAlsoMatchTheEntityItself() {
        var group = new CollectionRuleGroup {
            Operator = "and",
            Children = [
                new CollectionRuleCondition {
                    Field = CollectionRuleField.LibraryRootId.ToCode(),
                    Operator = "equals",
                    Value = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString("D"))
                }
            ]
        };

        var query = new CollectionRuleEngine(null!, null!).BuildQuery(group, EntityKind.AudioTrack.ToCode());

        Assert.NotNull(query);
        Assert.Contains("root_self", query.Value.Sql, StringComparison.Ordinal);
        Assert.Contains("root_self.entity_id = e.id", query.Value.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void LibraryRuleForSeriesUsesChildVideoRoots() {
        var group = new CollectionRuleGroup {
            Operator = "and",
            Children = [
                new CollectionRuleCondition {
                    Field = CollectionRuleField.LibraryRootId.ToCode(),
                    Operator = "equals",
                    Value = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString("D"))
                }
            ]
        };

        var query = new CollectionRuleEngine(null!, null!).BuildQuery(group, EntityKind.VideoSeries.ToCode());

        Assert.NotNull(query);
        Assert.Contains("FROM entities rooted_descendant", query.Value.Sql, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN entity_library_roots rooted_detail", query.Value.Sql, StringComparison.Ordinal);
        Assert.Contains("rooted_parent_1.parent_entity_id = e.id", query.Value.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectLibraryRootPoliciesResolveSharedPersistenceRows() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseNpgsql("Host=localhost;Database=collection_rule_model;Username=unused;Password=unused")
            .Options;
        using var db = new PrismediaDbContext(options);

        var directDefinitions = EntityKindRegistry.All
            .Where(definition => definition.LibraryVisibility.Mode == EntityLibraryVisibilityMode.DirectRoot)
            .ToArray();
        Assert.NotEmpty(directDefinitions);

        Assert.All(directDefinitions, definition =>
            Assert.Equal(EntityLibraryVisibilityMode.DirectRoot, definition.LibraryVisibility.Mode));
        Assert.Contains(db.Model.GetEntityTypes(), entityType =>
            entityType.GetTableName() == "entity_library_roots" &&
            entityType.FindProperty("EntityId") is not null &&
            entityType.FindProperty("LibraryRootId") is not null);
    }

    [Fact]
    public void LibraryRuleForAudioTracksUsesInheritedRootDetails() {
        var group = new CollectionRuleGroup {
            Operator = "and",
            Children = [
                new CollectionRuleCondition {
                    Field = CollectionRuleField.LibraryRootId.ToCode(),
                    Operator = "equals",
                    Value = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString("D"))
                }
            ]
        };

        var query = new CollectionRuleEngine(null!, null!).BuildQuery(group, EntityKind.AudioTrack.ToCode());

        Assert.NotNull(query);
        Assert.Contains("FROM entities parent1", query.Value.Sql, StringComparison.Ordinal);
        Assert.Contains("entity_library_roots", query.Value.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void CollectionCatalogPlanExcludesBookOwnedAudioTracksInGenericSql() {
        var group = new CollectionRuleGroup {
            Operator = "and",
            Children = [
                new CollectionRuleCondition {
                    Field = CollectionRuleField.Title.ToCode(),
                    Operator = "contains",
                    Value = JsonSerializer.SerializeToElement("Chapter")
                }
            ]
        };

        var query = new CollectionRuleEngine(null!, null!).BuildQuery(group, EntityKind.AudioTrack.ToCode());

        Assert.NotNull(query);
        Assert.Contains("parent.id = e.parent_entity_id", query.Value.Sql, StringComparison.Ordinal);
        Assert.Contains(query.Value.Parameters, parameter =>
            parameter.NpgsqlDbType == NpgsqlDbType.Text &&
            Equals(parameter.Value, EntityKind.Book.ToCode()));
    }

    [Fact]
    public void RestrictedFieldsOnlyBuildSqlForSupportedKindsWhenEntityTypesAreEmpty() {
        var group = new CollectionRuleGroup {
            Operator = "and",
            Children = [
                new CollectionRuleCondition {
                    Field = CollectionRuleField.SkipCount.ToCode(),
                    Operator = "less_equal",
                    Value = JsonSerializer.SerializeToElement(2)
                }
            ]
        };

        var videoSql = new CollectionRuleEngine(null!, null!).BuildQuery(group, EntityKind.Video.ToCode());
        var gallerySql = new CollectionRuleEngine(null!, null!).BuildQuery(group, EntityKind.Gallery.ToCode());

        Assert.NotNull(videoSql);
        Assert.Null(gallerySql);
    }

    [Fact]
    public void SharedVideoFieldsApplyToEveryDiscoveredPlayableVideoKind() {
        var playableKinds = EntityKindRegistry.All
            .Where(definition => definition is IPlayableVideoKindDefinition)
            .Select(definition => definition.Kind)
            .ToArray();

        Assert.Contains(EntityKind.Movie, playableKinds);
        Assert.Contains(EntityKind.Video, playableKinds);
        Assert.Contains(EntityKind.VideoEpisode, playableKinds);

        foreach (var field in new[] {
                     CollectionRuleField.FileSize,
                     CollectionRuleField.Duration,
                     CollectionRuleField.Codec,
                     CollectionRuleField.AccessCount,
                     CollectionRuleField.SkipCount,
                     CollectionRuleField.Resolution,
                     CollectionRuleField.Interactive
                 }) {
            var op = field == CollectionRuleField.Interactive
                ? CollectionRuleOperator.IsTrue
                : CollectionRuleOperator.GreaterThan;
            foreach (var kind in playableKinds) {
                var query = new CollectionRuleEngine(null!, null!).BuildQuery(
                    Rule(field, op, 1),
                    kind.ToCode());
                Assert.NotNull(query);
            }
        }
    }

    [Fact]
    public void VideoSeriesRulesApplyOnlyToStructurallyEpisodicPlayableKinds() {
        var group = Rule(
            CollectionRuleField.VideoSeriesId,
            CollectionRuleOperator.Equals,
            "The Chair Company");
        var engine = new CollectionRuleEngine(null!, null!);

        Assert.NotNull(engine.BuildQuery(group, EntityKind.VideoEpisode.ToCode()));
        Assert.Null(engine.BuildQuery(group, EntityKind.Movie.ToCode()));
        Assert.Null(engine.BuildQuery(group, EntityKind.Video.ToCode()));
    }

    [Fact]
    public void CollectionsCanContainVideoEpisodes() {
        Assert.True(Collection.CanContain(EntityKind.VideoEpisode));
        Assert.True(Collection.CanContain(EntityKind.Video));
    }

    [Fact]
    public void ImageAndAudioRulesRetainTheirExistingApplicability() {
        var engine = new CollectionRuleEngine(null!, null!);

        Assert.NotNull(engine.BuildQuery(
            Rule(CollectionRuleField.FileSize, CollectionRuleOperator.GreaterThan, 1024),
            EntityKind.Image.ToCode()));
        Assert.NotNull(engine.BuildQuery(
            Rule(CollectionRuleField.Duration, CollectionRuleOperator.GreaterThan, 60),
            EntityKind.AudioTrack.ToCode()));
        Assert.Null(engine.BuildQuery(
            Rule(CollectionRuleField.Duration, CollectionRuleOperator.GreaterThan, 60),
            EntityKind.Image.ToCode()));
    }

    [Fact]
    public void GeneratedSqlNeverReferencesDroppedDeletedAtColumn() {
        // The entities.deleted_at column was dropped (migration DropEntityDeletedAt);
        // Prismedia is hard-delete only, so any deleted_at predicate is invalid SQL.
        var topLevel = new CollectionRuleGroup {
            Operator = "and",
            Children = [
                new CollectionRuleCondition {
                    EntityTypes = [EntityKind.Video.ToCode()],
                    Field = CollectionRuleField.Rating.ToCode(),
                    Operator = "is_not_null"
                }
            ]
        };
        var videoSql = new CollectionRuleEngine(null!, null!).BuildQuery(topLevel, EntityKind.Video.ToCode());
        Assert.NotNull(videoSql);
        Assert.DoesNotContain("deleted_at", videoSql.Value.Sql, StringComparison.Ordinal);

        // imageCount drives the child-count subquery (the second former deleted_at site).
        var childCount = new CollectionRuleGroup {
            Operator = "and",
            Children = [
                new CollectionRuleCondition {
                    EntityTypes = [EntityKind.Gallery.ToCode()],
                    Field = CollectionRuleField.ImageCount.ToCode(),
                    Operator = "greater_than",
                    Value = JsonSerializer.SerializeToElement(5)
                }
            ]
        };
        var gallerySql = new CollectionRuleEngine(null!, null!).BuildQuery(childCount, EntityKind.Gallery.ToCode());
        Assert.NotNull(gallerySql);
        Assert.DoesNotContain("deleted_at", gallerySql.Value.Sql, StringComparison.Ordinal);
    }

    public static IEnumerable<object?[]> RepresentativeRuleCases() {
        var allKinds = new[] {
            EntityKind.Video.ToCode(),
            EntityKind.VideoEpisode.ToCode(),
            EntityKind.Movie.ToCode(),
            EntityKind.VideoSeries.ToCode(),
            EntityKind.Gallery.ToCode(),
            EntityKind.Image.ToCode(),
            EntityKind.Book.ToCode(),
            EntityKind.MusicArtist.ToCode(),
            EntityKind.AudioLibrary.ToCode(),
            EntityKind.AudioTrack.ToCode(),
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
            yield return Case("libraryRootId", "equals", Guid.NewGuid().ToString("D"), kind);
            yield return Case("createdAt", "greater_than", "2026-01-01T00:00:00Z", kind);
        }

        foreach (var kind in new[] { EntityKind.Video.ToCode(), EntityKind.Image.ToCode(), EntityKind.AudioTrack.ToCode() }) {
            yield return Case("fileSize", "greater_than", 1024, kind);
        }

        foreach (var kind in new[] { EntityKind.Video.ToCode(), EntityKind.AudioTrack.ToCode() }) {
            yield return Case("duration", "greater_than", 60, kind);
            yield return Case(CollectionRuleField.AccessCount.ToCode(), "greater_equal", 1, kind);
            yield return Case("skipCount", "less_equal", 2, kind);
        }

        yield return Case("resolution", "in", new[] { "1080p" }, EntityKind.Video.ToCode());
        yield return Case("codec", "equals", "h264", EntityKind.Video.ToCode());
        yield return Case("interactive", "is_false", null, EntityKind.Video.ToCode());
        yield return Case("videoSeriesId", "equals", "The Chair Company", EntityKind.VideoEpisode.ToCode());
        yield return Case("galleryType", "equals", "folder", EntityKind.Gallery.ToCode());
        yield return Case("imageCount", "greater_than", 3, EntityKind.Gallery.ToCode());
        yield return Case("width", "greater_than", 640, EntityKind.Image.ToCode());
        yield return Case("height", "greater_than", 480, EntityKind.Image.ToCode());
        yield return Case("format", "equals", "jpeg", EntityKind.Image.ToCode());
        yield return Case("bitRate", "greater_than", 128000, EntityKind.AudioTrack.ToCode());
        yield return Case("sampleRate", "greater_than", 44100, EntityKind.AudioTrack.ToCode());
        yield return Case("channels", "equals", 2, EntityKind.AudioTrack.ToCode());
    }

    private static object?[] Case(string field, string op, object? value, string kindCode) =>
        [field, op, value, kindCode];

    private static CollectionRuleGroup Rule(
        CollectionRuleField field,
        CollectionRuleOperator op,
        object? value) =>
        new() {
            Operator = CollectionRuleGroupOperator.And.ToCode(),
            Children = [new CollectionRuleCondition {
                Field = field.ToCode(),
                Operator = op.ToCode(),
                Value = value is null ? null : JsonSerializer.SerializeToElement(value, value.GetType())
            }]
        };
}
