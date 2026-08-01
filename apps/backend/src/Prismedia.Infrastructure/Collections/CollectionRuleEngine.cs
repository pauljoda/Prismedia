using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Collections;

/// <summary>
/// Evaluates collection rule trees against the unified entity model.
/// Translates a <see cref="CollectionRuleGroup"/> tree into parameterized SQL
/// queries per entity kind, then returns all matching entity references.
/// </summary>
public sealed class CollectionRuleEngine(
    PrismediaDbContext db,
    EfEntityLibraryVisibilityFilter libraryVisibility) : ICollectionRuleEngine {
    private static readonly Dictionary<string, (int Min, int Max)> ResolutionMap = new() {
        ["4K"] = (2160, 99999),
        ["1080p"] = (1080, 2159),
        ["720p"] = (720, 1079),
        ["480p"] = (0, 719)
    };

    private static readonly IEntityContainmentPolicy CollectionPolicy =
        EntityKindRegistry.Get<CollectionEntityKindDefinition>();

    private static readonly IReadOnlySet<string> PlayableVideoKinds = EntityKindRegistry.All
        .Where(definition => definition is IPlayableVideoKindDefinition)
        .Select(definition => definition.Code)
        .ToHashSet(StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> EpisodicPlayableVideoKinds = EntityKindRegistry.All
        .Where(definition => definition is IPlayableVideoKindDefinition)
        .Where(definition => definition.StructurePolicy.RequiresParent)
        .Where(definition => definition.StructurePolicy.AllowedParentKinds.Any(parentKind =>
            parentKind is EntityKind.VideoSeries or EntityKind.VideoSeason))
        .Select(definition => definition.Code)
        .ToHashSet(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<CollectionRuleField, IReadOnlySet<string>> FieldTargetKinds =
        new Dictionary<CollectionRuleField, IReadOnlySet<string>> {
        [CollectionRuleField.FileSize] = Kinds(PlayableVideoKinds, EntityKind.Image, EntityKind.AudioTrack),
        [CollectionRuleField.Duration] = Kinds(PlayableVideoKinds, EntityKind.AudioTrack),
        [CollectionRuleField.Height] = Kinds(EntityKind.Image),
        [CollectionRuleField.Width] = Kinds(EntityKind.Image),
        [CollectionRuleField.Codec] = PlayableVideoKinds,
        [CollectionRuleField.BitRate] = Kinds(EntityKind.AudioTrack),
        [CollectionRuleField.BitRateLegacy] = Kinds(EntityKind.AudioTrack),
        [CollectionRuleField.Channels] = Kinds(EntityKind.AudioTrack),
        [CollectionRuleField.SampleRate] = Kinds(EntityKind.AudioTrack),
        [CollectionRuleField.SampleRateLegacy] = Kinds(EntityKind.AudioTrack),
        [CollectionRuleField.PlayCount] = Kinds(PlayableVideoKinds, EntityKind.AudioTrack),
        [CollectionRuleField.SkipCount] = Kinds(PlayableVideoKinds, EntityKind.AudioTrack),
        [CollectionRuleField.Resolution] = PlayableVideoKinds,
        [CollectionRuleField.VideoSeriesId] = EpisodicPlayableVideoKinds,
        [CollectionRuleField.LibraryRootId] = Kinds(CollectionPolicy.ContainableKinds
            .Select(EntityKindRegistry.Describe)
            .Where(definition => definition.LibraryVisibility.Mode != EntityLibraryVisibilityMode.Unscoped)
            .Select(definition => definition.Code)
            .ToArray()),
        [CollectionRuleField.GalleryType] = Kinds(EntityKind.Gallery),
        [CollectionRuleField.ImageCount] = Kinds(EntityKind.Gallery),
        [CollectionRuleField.Format] = Kinds(EntityKind.Image),
        [CollectionRuleField.Interactive] = PlayableVideoKinds,
    };

    public async Task<IReadOnlyList<CollectionRuleMatch>> EvaluateAsync(
        string ruleTreeJson,
        Guid userId,
        CancellationToken cancellationToken) {
        var ruleTree = JsonSerializer.Deserialize<CollectionRuleNode>(ruleTreeJson);
        if (ruleTree is not CollectionRuleGroup group || userId == Guid.Empty) return [];

        var owner = await db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new { user.Role })
            .FirstOrDefaultAsync(cancellationToken);
        if (owner is null) return [];

        var candidates = new List<CollectionRuleMatch>();

        foreach (var kind in CollectionPolicy.ContainableKinds) {
            var kindCode = EntityKindRegistry.ToCode(kind);
            var query = BuildQuery(group, kindCode, userId);
            if (query is null) continue;

            var ids = await ExecuteQueryAsync(query.Value.Sql, query.Value.Parameters, cancellationToken);
            candidates.AddRange(ids.Select(id => new CollectionRuleMatch(kind, id)));
        }

        var visibleIds = await libraryVisibility.FilterVisibleIdsAsync(
            candidates.Select(candidate => candidate.EntityId).ToHashSet(),
            userId,
            owner.Role,
            cancellationToken);

        return candidates.Where(candidate => visibleIds.Contains(candidate.EntityId)).ToArray();
    }

    internal (string Sql, List<NpgsqlParameter> Parameters)? BuildQuery(
        CollectionRuleGroup group,
        string kindCode,
        Guid userId = default) {
        var ctx = new SqlBuildContext(userId);
        var whereFragment = TranslateNode(group, kindCode, ctx);
        if (whereFragment is null) return null;

        return (BuildSql(kindCode, whereFragment, ctx), ctx.Parameters);
    }

    private static string BuildSql(
        string kindCode,
        string whereFragment,
        SqlBuildContext ctx) {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("SELECT DISTINCT e.id FROM entities e");

        foreach (var join in ctx.Joins)
            sb.AppendLine(join);

        sb.Append("WHERE e.kind_code = ");
        var kindParam = ctx.AddParam(kindCode, NpgsqlDbType.Text);
        sb.AppendLine(kindParam);
        var catalogPlan = EntityCatalogQueryPolicy.PlanFor(EntityCatalogSurface.Collection, kindCode);
        if (catalogPlan.RequiresTopLevel) {
            sb.AppendLine("AND e.parent_entity_id IS NULL");
        }
        foreach (var hiddenParentKindCode in catalogPlan.HiddenParentKindCodes) {
            var parentKindParam = ctx.AddParam(hiddenParentKindCode, NpgsqlDbType.Text);
            sb.Append("AND NOT EXISTS (SELECT 1 FROM entities parent WHERE parent.id = e.parent_entity_id AND parent.kind_code = ");
            sb.Append(parentKindParam);
            sb.AppendLine(")");
        }
        sb.Append("AND (");
        sb.Append(whereFragment);
        sb.AppendLine(")");
        return sb.ToString();
    }

    private async Task<List<Guid>> ExecuteQueryAsync(
        string sql, List<NpgsqlParameter> parameters, CancellationToken cancellationToken) {
        var ids = new List<Guid>();
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in parameters)
            cmd.Parameters.Add(p);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            ids.Add(reader.GetGuid(0));

        return ids;
    }

    private string? TranslateNode(CollectionRuleNode node, string kindCode, SqlBuildContext ctx) {
        if (node is CollectionRuleCondition condition)
            return TranslateCondition(condition, kindCode, ctx);

        if (node is CollectionRuleGroup group)
            return TranslateGroup(group, kindCode, ctx);

        return null;
    }

    private string? TranslateGroup(CollectionRuleGroup group, string kindCode, SqlBuildContext ctx) {
        if (!group.Operator.TryDecodeAs<CollectionRuleGroupOperator>(out var op)) {
            return null;
        }

        var fragments = new List<string>();
        foreach (var child in group.Children) {
            var fragment = TranslateNode(child, kindCode, ctx);
            if (fragment is not null)
                fragments.Add(fragment);
        }

        if (fragments.Count == 0) return null;

        return op switch {
            CollectionRuleGroupOperator.And => fragments.Count == 1 ? fragments[0] : $"({string.Join(" AND ", fragments)})",
            CollectionRuleGroupOperator.Or => fragments.Count == 1 ? fragments[0] : $"({string.Join(" OR ", fragments)})",
            CollectionRuleGroupOperator.Not => fragments.Count == 1
                ? $"NOT ({fragments[0]})"
                : $"NOT ({string.Join(" AND ", fragments)})",
            _ => null
        };
    }

    private string? TranslateCondition(CollectionRuleCondition condition, string kindCode, SqlBuildContext ctx) {
        if (!condition.Field.TryDecodeAs<CollectionRuleField>(out var field) ||
            !condition.Operator.TryDecodeAs<CollectionRuleOperator>(out var op) ||
            !FieldAppliesToKind(field, kindCode))
            return null;

        if (condition.EntityTypes.Count > 0 && !ConditionAppliesToKind(condition.EntityTypes, kindCode))
            return null;

        return field switch {
            CollectionRuleField.Title => TranslateScalar("e.title", op, condition.Value, ctx),
            CollectionRuleField.Rating => TranslateScalar("e.rating_value", op, condition.Value, ctx),
            CollectionRuleField.Date => TranslateDateField(condition, op, ctx),
            CollectionRuleField.Organized => TranslateFlag("is_organized", op),
            CollectionRuleField.IsNsfw => TranslateFlag("is_nsfw", op),
            CollectionRuleField.Tags => TranslateRelation(RelationshipKind.Tags.ToCode(), EntityKind.Tag.ToCode(), condition, op, ctx),
            CollectionRuleField.Performers => TranslateRelation(RelationshipKind.Cast.ToCode(), EntityKind.Person.ToCode(), condition, op, ctx),
            CollectionRuleField.Studio => TranslateStudioRelation(condition, op, ctx),
            CollectionRuleField.FileSize => TranslateFileSize(condition, op, ctx),
            CollectionRuleField.Duration => TranslateTechnical("duration_seconds", condition, op, ctx),
            CollectionRuleField.Height => TranslateTechnical("height", condition, op, ctx),
            CollectionRuleField.Width => TranslateTechnical("width", condition, op, ctx),
            CollectionRuleField.Codec => TranslateTechnical("codec", condition, op, ctx),
            CollectionRuleField.BitRate or CollectionRuleField.BitRateLegacy => TranslateTechnical("bit_rate", condition, op, ctx),
            CollectionRuleField.Channels => TranslateTechnical("channels", condition, op, ctx),
            CollectionRuleField.SampleRate or CollectionRuleField.SampleRateLegacy => TranslateTechnical("sample_rate", condition, op, ctx),
            CollectionRuleField.PlayCount => TranslatePlayback("play_count", condition, op, ctx),
            CollectionRuleField.SkipCount => TranslatePlayback("skip_count", condition, op, ctx),
            CollectionRuleField.Resolution => TranslateResolution(condition, op, ctx),
            CollectionRuleField.VideoSeriesId => TranslateVideoSeries(condition, op, ctx),
            CollectionRuleField.LibraryRootId => TranslateLibraryRoot(condition, op, kindCode, ctx),
            CollectionRuleField.GalleryType => TranslateGalleryType(condition, op, kindCode, ctx),
            CollectionRuleField.ImageCount => TranslateChildCount(condition, op, kindCode, ctx),
            CollectionRuleField.Format => TranslateTechnical("format", condition, op, ctx),
            CollectionRuleField.CreatedAt => TranslateDateTimeScalar("e.created_at", op, condition.Value, ctx),
            CollectionRuleField.Interactive => TranslateFlag("is_favorite", op),
            _ => null
        };
    }

    private static bool ConditionAppliesToKind(IReadOnlyList<string> entityTypes, string kindCode) =>
        entityTypes.Any(entityType => KindEquals(entityType, kindCode));

    private static bool FieldAppliesToKind(CollectionRuleField field, string kindCode) =>
        !FieldTargetKinds.TryGetValue(field, out var kinds) || kinds.Contains(kindCode);

    private static bool KindEquals(string actual, string expected) =>
        actual.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlySet<string> Kinds(params EntityKind[] kinds) =>
        new HashSet<string>(kinds.Select(EntityKindRegistry.ToCode), StringComparer.Ordinal);

    private static IReadOnlySet<string> Kinds(IReadOnlySet<string> initialKinds, params EntityKind[] kinds) {
        var kindCodes = new HashSet<string>(initialKinds, StringComparer.Ordinal);
        kindCodes.UnionWith(kinds.Select(EntityKindRegistry.ToCode));
        return kindCodes;
    }

    private static IReadOnlySet<string> Kinds(params string[] kindCodes) =>
        new HashSet<string>(kindCodes, StringComparer.Ordinal);

    // ── Scalar field translation ──

    private static string? TranslateScalar(string column, CollectionRuleOperator op, JsonElement? value, SqlBuildContext ctx) {
        return op switch {
            CollectionRuleOperator.Equals => $"{column} = {ctx.AddJsonParam(value)}",
            CollectionRuleOperator.NotEquals => $"{column} != {ctx.AddJsonParam(value)}",
            CollectionRuleOperator.Contains => $"{column} ILIKE {ctx.AddParam($"%{value?.GetString()}%", NpgsqlDbType.Text)}",
            CollectionRuleOperator.NotContains => $"NOT ({column} ILIKE {ctx.AddParam($"%{value?.GetString()}%", NpgsqlDbType.Text)})",
            CollectionRuleOperator.GreaterThan => $"{column} > {ctx.AddJsonParam(value)}",
            CollectionRuleOperator.LessThan => $"{column} < {ctx.AddJsonParam(value)}",
            CollectionRuleOperator.GreaterEqual => $"{column} >= {ctx.AddJsonParam(value)}",
            CollectionRuleOperator.LessEqual => $"{column} <= {ctx.AddJsonParam(value)}",
            CollectionRuleOperator.Between when value?.ValueKind == JsonValueKind.Array =>
                $"{column} BETWEEN {ctx.AddJsonParam(value?.EnumerateArray().ElementAt(0))} AND {ctx.AddJsonParam(value?.EnumerateArray().ElementAt(1))}",
            CollectionRuleOperator.In when value?.ValueKind == JsonValueKind.Array =>
                $"{column} IN ({string.Join(", ", value.Value.EnumerateArray().Select(v => ctx.AddJsonParam(v)))})",
            CollectionRuleOperator.NotIn when value?.ValueKind == JsonValueKind.Array =>
                $"{column} NOT IN ({string.Join(", ", value.Value.EnumerateArray().Select(v => ctx.AddJsonParam(v)))})",
            CollectionRuleOperator.IsNull => $"{column} IS NULL",
            CollectionRuleOperator.IsNotNull => $"{column} IS NOT NULL",
            CollectionRuleOperator.IsTrue => $"{column} = true",
            CollectionRuleOperator.IsFalse => $"{column} = false",
            _ => null
        };
    }

    private string? TranslateTechnical(
        string column,
        CollectionRuleCondition condition,
        CollectionRuleOperator op,
        SqlBuildContext ctx) {
        ctx.EnsureJoin("LEFT JOIN entity_technical t ON t.entity_id = e.id");
        return TranslateScalar($"t.{column}", op, condition.Value, ctx);
    }

    private string? TranslatePlayback(
        string column,
        CollectionRuleCondition condition,
        CollectionRuleOperator op,
        SqlBuildContext ctx) {
        ctx.EnsureJoin(
            $"LEFT JOIN user_entity_states pb ON pb.entity_id = e.id AND pb.user_id = {ctx.UserIdParameter}");
        return TranslateScalar($"COALESCE(pb.{column}, 0)", op, condition.Value, ctx);
    }

    private static string? TranslateFlag(string column, CollectionRuleOperator op) {
        return op switch {
            CollectionRuleOperator.IsTrue => $"e.{column} = true",
            CollectionRuleOperator.IsFalse => $"e.{column} = false",
            _ => null
        };
    }

    private string? TranslateDateField(
        CollectionRuleCondition condition,
        CollectionRuleOperator op,
        SqlBuildContext ctx) {
        ctx.EnsureJoin(
            $"LEFT JOIN entity_dates ed ON ed.entity_id = e.id AND ed.code IN ('{EntityDateType.Release.ToCode()}', '{EntityDateType.Air.ToCode()}')");
        return TranslateDateScalar("ed.sortable_value", op, condition.Value, ctx);
    }

    private string? TranslateFileSize(
        CollectionRuleCondition condition,
        CollectionRuleOperator op,
        SqlBuildContext ctx) {
        ctx.EnsureJoin(
            $"LEFT JOIN entity_files ef_src ON ef_src.entity_id = e.id AND ef_src.role = '{EntityFileRole.Source.ToCode()}'");
        return TranslateScalar("ef_src.size_bytes", op, condition.Value, ctx);
    }

    // ── Relation fields (tags, performers) ──

    private string? TranslateRelation(
        string relationshipCode,
        string taxonomyKindCode,
        CollectionRuleCondition condition,
        CollectionRuleOperator op,
        SqlBuildContext ctx) {
        var names = GetStringArray(condition.Value);
        if (names.Count == 0) return "false";

        var nameParams = string.Join(", ", names.Select(n => ctx.AddParam(n, NpgsqlDbType.Text)));
        var kindParam = ctx.AddParam(taxonomyKindCode, NpgsqlDbType.Text);
        var relationshipParam = ctx.AddParam(relationshipCode, NpgsqlDbType.Text);

        var subquery = $@"e.id IN (
            SELECT rl.entity_id FROM entity_relationship_links rl
            INNER JOIN entities te ON te.id = rl.target_entity_id
            WHERE rl.relationship_code = {relationshipParam}
                AND rl.target_kind_code = {kindParam}
                AND te.kind_code = {kindParam}
                AND te.title IN ({nameParams})
        )";

        return op switch {
            CollectionRuleOperator.In => subquery,
            CollectionRuleOperator.NotIn => $"NOT ({subquery})",
            _ => null
        };
    }

    private string? TranslateStudioRelation(
        CollectionRuleCondition condition,
        CollectionRuleOperator op,
        SqlBuildContext ctx) {
        var relationshipParam = ctx.AddParam(RelationshipKind.Studio.ToCode(), NpgsqlDbType.Text);
        if (op is CollectionRuleOperator.IsNull) {
            return $"NOT EXISTS (SELECT 1 FROM entity_relationship_links sl WHERE sl.entity_id = e.id AND sl.relationship_code = {relationshipParam})";
        }
        if (op is CollectionRuleOperator.IsNotNull) {
            return $"EXISTS (SELECT 1 FROM entity_relationship_links sl WHERE sl.entity_id = e.id AND sl.relationship_code = {relationshipParam})";
        }

        var names = GetStringArray(condition.Value);
        if (names.Count == 0) return "false";

        var nameParams = string.Join(", ", names.Select(n => ctx.AddParam(n, NpgsqlDbType.Text)));
        var kindParam = ctx.AddParam(EntityKind.Studio.ToCode(), NpgsqlDbType.Text);

        var subquery = $@"e.id IN (
            SELECT sl.entity_id FROM entity_relationship_links sl
            INNER JOIN entities se ON se.id = sl.target_entity_id
            WHERE sl.relationship_code = {relationshipParam}
                AND sl.target_kind_code = {kindParam}
                AND se.kind_code = {kindParam}
                AND se.title IN ({nameParams})
        )";

        return op switch {
            CollectionRuleOperator.In => subquery,
            CollectionRuleOperator.NotIn => $"NOT ({subquery})",
            _ => null
        };
    }

    // ── Resolution (maps named tiers to height ranges) ──

    private string? TranslateResolution(
        CollectionRuleCondition condition,
        CollectionRuleOperator op,
        SqlBuildContext ctx) {
        ctx.EnsureJoin("LEFT JOIN entity_technical t ON t.entity_id = e.id");

        var values = GetStringArray(condition.Value);
        var rangeClauses = new List<string>();

        foreach (var val in values) {
            if (!ResolutionMap.TryGetValue(val, out var range)) continue;
            var minP = ctx.AddParam(range.Min, NpgsqlDbType.Integer);
            var maxP = ctx.AddParam(range.Max, NpgsqlDbType.Integer);
            rangeClauses.Add($"(t.height >= {minP} AND t.height <= {maxP})");
        }

        if (rangeClauses.Count == 0) return "false";

        var combined = string.Join(" OR ", rangeClauses);
        return op switch {
            CollectionRuleOperator.In => $"({combined})",
            CollectionRuleOperator.NotIn => $"NOT ({combined})",
            _ => null
        };
    }

    // ── Video series (structural walk: episode -> season -> series) ──

    private string? TranslateVideoSeries(
        CollectionRuleCondition condition,
        CollectionRuleOperator op,
        SqlBuildContext ctx) {
        var predicate = TranslateVideoSeriesPredicate(condition, op, ctx);
        if (predicate is null) return null;

        var seriesKindParam = ctx.AddParam(EntityKind.VideoSeries.ToCode(), NpgsqlDbType.Text);
        var subquery = $@"EXISTS (
            SELECT 1
            FROM entities series_entity
            WHERE series_entity.kind_code = {seriesKindParam}
              AND (
                series_entity.id = e.parent_entity_id
                OR EXISTS (
                    SELECT 1
                    FROM entities parent_entity
                    WHERE parent_entity.id = e.parent_entity_id
                      AND parent_entity.parent_entity_id = series_entity.id
                )
              )
              AND ({predicate})
        )";

        return op is CollectionRuleOperator.NotIn ? $"NOT ({subquery})" : subquery;
    }

    private static string? TranslateVideoSeriesPredicate(
        CollectionRuleCondition condition,
        CollectionRuleOperator op,
        SqlBuildContext ctx) {
        if (op is CollectionRuleOperator.Equals) {
            if (condition.Value?.ValueKind != JsonValueKind.String) {
                return "false";
            }

            var value = condition.Value.Value.GetString();
            if (string.IsNullOrWhiteSpace(value)) {
                return "false";
            }

            return Guid.TryParse(value, out var id)
                ? $"series_entity.id = {ctx.AddParam(id, NpgsqlDbType.Uuid)}"
                : $"series_entity.title = {ctx.AddParam(value, NpgsqlDbType.Text)}";
        }

        if (op is not (CollectionRuleOperator.In or CollectionRuleOperator.NotIn) ||
            condition.Value?.ValueKind != JsonValueKind.Array) {
            return null;
        }

        var ids = new List<Guid>();
        var titles = new List<string>();
        foreach (var item in condition.Value.Value.EnumerateArray()) {
            if (item.ValueKind != JsonValueKind.String) {
                continue;
            }

            var value = item.GetString();
            if (string.IsNullOrWhiteSpace(value)) {
                continue;
            }

            if (Guid.TryParse(value, out var id)) {
                ids.Add(id);
                continue;
            }

            titles.Add(value);
        }

        var fragments = new List<string>();
        if (ids.Count > 0) {
            fragments.Add($"series_entity.id IN ({string.Join(", ", ids.Select(id => ctx.AddParam(id, NpgsqlDbType.Uuid)))})");
        }

        if (titles.Count > 0) {
            fragments.Add($"series_entity.title IN ({string.Join(", ", titles.Select(title => ctx.AddParam(title, NpgsqlDbType.Text)))})");
        }

        return fragments.Count switch {
            0 => "false",
            1 => fragments[0],
            _ => $"({string.Join(" OR ", fragments)})"
        };
    }

    // ── Gallery type (from detail table) ──

    private string? TranslateGalleryType(
        CollectionRuleCondition condition,
        CollectionRuleOperator op,
        string kindCode,
        SqlBuildContext ctx) {
        if (!KindEquals(kindCode, EntityKind.Gallery.ToCode())) return null;
        ctx.EnsureJoin("LEFT JOIN gallery_details gd ON gd.entity_id = e.id");
        return TranslateScalar("gd.gallery_type", op, condition.Value, ctx);
    }

    // ── Child count (count generic structural children) ──

    private string? TranslateChildCount(
        CollectionRuleCondition condition,
        CollectionRuleOperator op,
        string kindCode,
        SqlBuildContext ctx) {
        if (!KindEquals(kindCode, EntityKind.Gallery.ToCode()) &&
            !KindEquals(kindCode, EntityKind.Book.ToCode())) {
            return null;
        }

        var countExpr = "(SELECT COUNT(*) FROM entities child_count WHERE child_count.parent_entity_id = e.id)";
        return TranslateScalar(countExpr, op, condition.Value, ctx);
    }

    // ── Library root membership ──

    private static string? TranslateLibraryRoot(
        CollectionRuleCondition condition,
        CollectionRuleOperator op,
        string kindCode,
        SqlBuildContext ctx) {
        var existsBuilder = LibraryRootExistsBuilder(kindCode, ctx);
        return existsBuilder is null
            ? null
            : QuantifyLibraryRootMatch(condition, op, existsBuilder, ctx);
    }

    private static Func<Func<string, string>, string>? LibraryRootExistsBuilder(string kindCode, SqlBuildContext ctx) {
        if (!EntityKindRegistry.TryDescribe(kindCode, out var definition)) return null;

        return definition.LibraryVisibility.Mode switch {
            EntityLibraryVisibilityMode.DirectRoot => rootPredicate =>
                DirectRootExists("direct_root", "e.id", rootPredicate),
            EntityLibraryVisibilityMode.AncestorRoot => AncestorRootExists,
            EntityLibraryVisibilityMode.DescendantRoot => rootPredicate =>
                DescendantRootExists(definition, rootPredicate, ctx),
            _ => null
        };
    }

    private static string? QuantifyLibraryRootMatch(
        CollectionRuleCondition condition,
        CollectionRuleOperator op,
        Func<Func<string, string>, string> existsBuilder,
        SqlBuildContext ctx) {
        static string NonNullRoot(string column) => $"{column} IS NOT NULL";

        if (op is CollectionRuleOperator.IsNull) {
            return $"NOT ({existsBuilder(NonNullRoot)})";
        }

        if (op is CollectionRuleOperator.IsNotNull) {
            return existsBuilder(NonNullRoot);
        }

        var selectedRoot = BuildSelectedLibraryRootPredicate(condition, op, ctx);
        if (selectedRoot is null) return null;

        return op switch {
            CollectionRuleOperator.Equals or CollectionRuleOperator.In => existsBuilder(selectedRoot),
            CollectionRuleOperator.NotEquals or CollectionRuleOperator.NotIn => $"({existsBuilder(NonNullRoot)} AND NOT ({existsBuilder(selectedRoot)}))",
            _ => null
        };
    }

    private static Func<string, string>? BuildSelectedLibraryRootPredicate(
        CollectionRuleCondition condition,
        CollectionRuleOperator op,
        SqlBuildContext ctx) {
        if (op is not (
                CollectionRuleOperator.Equals or
                CollectionRuleOperator.NotEquals or
                CollectionRuleOperator.In or
                CollectionRuleOperator.NotIn)) {
            return null;
        }

        var ids = GetGuidArray(condition.Value);
        if (ids.Count == 0) {
            return _ => "false";
        }

        var parameters = ids.Select(id => ctx.AddParam(id, NpgsqlDbType.Uuid)).ToArray();
        return column => parameters.Length == 1
            ? $"{column} = {parameters[0]}"
            : $"{column} IN ({string.Join(", ", parameters)})";
    }

    private static string DirectRootExists(
        string alias,
        string entityIdExpression,
        Func<string, string> rootPredicate) =>
        $@"EXISTS (
            SELECT 1
            FROM entity_library_roots {alias}
            WHERE {alias}.entity_id = {entityIdExpression}
                AND {rootPredicate($"{alias}.library_root_id")}
        )";

    private static string DescendantRootExists(
        EntityKindDefinition owner,
        Func<string, string> rootPredicate,
        SqlBuildContext ctx) {
        var policy = owner.LibraryVisibility;
        var descendant = EntityKindRegistry.Describe(policy.DescendantKind!.Value);
        var descendantKindParam = ctx.AddParam(descendant.Code, NpgsqlDbType.Text);
        var ownerMatches = Enumerable.Range(1, policy.MaximumDepth)
            .Select(DescendantOwnerMatch)
            .ToArray();

        return $@"EXISTS (
            SELECT 1
            FROM entities rooted_descendant
            INNER JOIN entity_library_roots rooted_detail
                ON rooted_detail.entity_id = rooted_descendant.id
            WHERE rooted_descendant.kind_code = {descendantKindParam}
                AND ({string.Join(" OR ", ownerMatches)})
                AND {rootPredicate("rooted_detail.library_root_id")}
        )";
    }

    private static string DescendantOwnerMatch(int depth) {
        if (depth == 1) return "rooted_descendant.parent_entity_id = e.id";

        var joins = Enumerable.Range(2, depth - 1)
            .Select(index =>
                $"INNER JOIN entities rooted_parent_{index} ON rooted_parent_{index}.id = rooted_parent_{index - 1}.parent_entity_id");
        return $@"EXISTS (
            SELECT 1
            FROM entities rooted_parent_1
            {string.Join(Environment.NewLine, joins)}
            WHERE rooted_parent_1.id = rooted_descendant.parent_entity_id
                AND rooted_parent_{depth - 1}.parent_entity_id = e.id
        )";
    }

    private static string AncestorRootExists(Func<string, string> rootPredicate) =>
        $@"({RootedEntityMatches("e.id", "self", rootPredicate)} OR EXISTS (
            SELECT 1
            FROM entities parent1
            LEFT JOIN entities parent2 ON parent2.id = parent1.parent_entity_id
            LEFT JOIN entities parent3 ON parent3.id = parent2.parent_entity_id
            WHERE parent1.id = e.parent_entity_id
                AND (
                    {RootedEntityMatches("parent1.id", "p1", rootPredicate)}
                    OR {RootedEntityMatches("parent2.id", "p2", rootPredicate)}
                    OR {RootedEntityMatches("parent3.id", "p3", rootPredicate)}
                )
        ))";

    private static string RootedEntityMatches(
        string entityIdExpression,
        string suffix,
        Func<string, string> rootPredicate) =>
        DirectRootExists($"root_{suffix}", entityIdExpression, rootPredicate);

    // ── Helpers ──

    private static string? TranslateDateScalar(
        string column,
        CollectionRuleOperator op,
        JsonElement? value,
        SqlBuildContext ctx) {
        return op switch {
            CollectionRuleOperator.Equals => $"{column} = {ctx.AddDateParam(value)}",
            CollectionRuleOperator.NotEquals => $"{column} != {ctx.AddDateParam(value)}",
            CollectionRuleOperator.GreaterThan => $"{column} > {ctx.AddDateParam(value)}",
            CollectionRuleOperator.LessThan => $"{column} < {ctx.AddDateParam(value)}",
            CollectionRuleOperator.GreaterEqual => $"{column} >= {ctx.AddDateParam(value)}",
            CollectionRuleOperator.LessEqual => $"{column} <= {ctx.AddDateParam(value)}",
            CollectionRuleOperator.Between when value?.ValueKind == JsonValueKind.Array =>
                $"{column} BETWEEN {ctx.AddDateParam(value?.EnumerateArray().ElementAt(0))} AND {ctx.AddDateParam(value?.EnumerateArray().ElementAt(1))}",
            CollectionRuleOperator.IsNull => $"{column} IS NULL",
            CollectionRuleOperator.IsNotNull => $"{column} IS NOT NULL",
            _ => null
        };
    }

    private static string? TranslateDateTimeScalar(
        string column,
        CollectionRuleOperator op,
        JsonElement? value,
        SqlBuildContext ctx) {
        return op switch {
            CollectionRuleOperator.Equals => $"{column} = {ctx.AddDateTimeParam(value)}",
            CollectionRuleOperator.NotEquals => $"{column} != {ctx.AddDateTimeParam(value)}",
            CollectionRuleOperator.GreaterThan => $"{column} > {ctx.AddDateTimeParam(value)}",
            CollectionRuleOperator.LessThan => $"{column} < {ctx.AddDateTimeParam(value)}",
            CollectionRuleOperator.GreaterEqual => $"{column} >= {ctx.AddDateTimeParam(value)}",
            CollectionRuleOperator.LessEqual => $"{column} <= {ctx.AddDateTimeParam(value)}",
            CollectionRuleOperator.Between when value?.ValueKind == JsonValueKind.Array =>
                $"{column} BETWEEN {ctx.AddDateTimeParam(value?.EnumerateArray().ElementAt(0))} AND {ctx.AddDateTimeParam(value?.EnumerateArray().ElementAt(1))}",
            CollectionRuleOperator.IsNull => $"{column} IS NULL",
            CollectionRuleOperator.IsNotNull => $"{column} IS NOT NULL",
            _ => null
        };
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement? value) {
        if (value is null) return [];
        if (value.Value.ValueKind == JsonValueKind.String)
            return [value.Value.GetString()!];
        if (value.Value.ValueKind == JsonValueKind.Array)
            return value.Value.EnumerateArray()
                .Where(v => v.ValueKind == JsonValueKind.String)
                .Select(v => v.GetString()!)
                .ToList();
        return [];
    }

    private static IReadOnlyList<Guid> GetGuidArray(JsonElement? value) {
        if (value is null) return [];
        if (value.Value.ValueKind == JsonValueKind.String) {
            return Guid.TryParse(value.Value.GetString(), out var id) ? [id] : [];
        }

        if (value.Value.ValueKind != JsonValueKind.Array) {
            return [];
        }

        var ids = new List<Guid>();
        foreach (var item in value.Value.EnumerateArray()) {
            if (item.ValueKind == JsonValueKind.String &&
                Guid.TryParse(item.GetString(), out var id)) {
                ids.Add(id);
            }
        }

        return ids;
    }

    private sealed class SqlBuildContext(Guid userId) {
        private int _paramIndex;
        private readonly HashSet<string> _joinSet = new(StringComparer.Ordinal);
        private string? _userIdParameter;

        public List<NpgsqlParameter> Parameters { get; } = [];
        public List<string> Joins { get; } = [];

        public string UserIdParameter =>
            _userIdParameter ??= AddParam(userId, NpgsqlDbType.Uuid);

        public void EnsureJoin(string joinClause) {
            if (_joinSet.Add(joinClause))
                Joins.Add(joinClause);
        }

        public string AddParam(object value, NpgsqlDbType dbType) {
            var name = $"@p{_paramIndex++}";
            var param = new NpgsqlParameter(name, dbType) { Value = value };
            Parameters.Add(param);
            return name;
        }

        public string AddJsonParam(JsonElement? value) {
            if (value is null) return "NULL";

            return value.Value.ValueKind switch {
                JsonValueKind.String => AddParam(value.Value.GetString()!, NpgsqlDbType.Text),
                JsonValueKind.Number when value.Value.TryGetInt32(out var i) => AddParam(i, NpgsqlDbType.Integer),
                JsonValueKind.Number when value.Value.TryGetInt64(out var l) => AddParam(l, NpgsqlDbType.Bigint),
                JsonValueKind.Number => AddParam(value.Value.GetDouble(), NpgsqlDbType.Double),
                JsonValueKind.True => AddParam(true, NpgsqlDbType.Boolean),
                JsonValueKind.False => AddParam(false, NpgsqlDbType.Boolean),
                _ => "NULL"
            };
        }

        public string AddDateParam(JsonElement? value) {
            if (value?.ValueKind != JsonValueKind.String ||
                !DateOnly.TryParse(value.Value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) {
                return "NULL";
            }

            return AddParam(parsed, NpgsqlDbType.Date);
        }

        public string AddDateTimeParam(JsonElement? value) {
            if (value?.ValueKind != JsonValueKind.String ||
                !DateTimeOffset.TryParse(value.Value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) {
                return "NULL";
            }

            return AddParam(parsed, NpgsqlDbType.TimestampTz);
        }
    }
}
