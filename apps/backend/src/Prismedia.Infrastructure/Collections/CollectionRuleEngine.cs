using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Collections;

/// <summary>
/// Evaluates collection rule trees against the unified entity model.
/// Translates a <see cref="CollectionRuleGroup"/> tree into parameterized SQL
/// queries per entity kind, then returns all matching entity references.
/// </summary>
public sealed class CollectionRuleEngine(PrismediaDbContext db) : ICollectionRuleEngine {
    private static readonly Dictionary<string, (int Min, int Max)> ResolutionMap = new() {
        ["4K"] = (2160, 99999),
        ["1080p"] = (1080, 2159),
        ["720p"] = (720, 1079),
        ["480p"] = (0, 719)
    };

    private static readonly IEntityContainmentPolicy CollectionPolicy =
        EntityKindRegistry.Get<CollectionEntityKindDefinition>();

    private static readonly Dictionary<string, HashSet<string>> FieldTargetKinds = new(StringComparer.Ordinal) {
        ["fileSize"] = Kinds(EntityKind.Video.ToCode(), EntityKind.Image.ToCode(), EntityKind.AudioTrack.ToCode()),
        ["duration"] = Kinds(EntityKind.Video.ToCode(), EntityKind.AudioTrack.ToCode()),
        ["height"] = Kinds(EntityKind.Image.ToCode()),
        ["width"] = Kinds(EntityKind.Image.ToCode()),
        ["codec"] = Kinds(EntityKind.Video.ToCode()),
        ["bitRate"] = Kinds(EntityKind.AudioTrack.ToCode()),
        ["bit_rate"] = Kinds(EntityKind.AudioTrack.ToCode()),
        ["channels"] = Kinds(EntityKind.AudioTrack.ToCode()),
        ["sampleRate"] = Kinds(EntityKind.AudioTrack.ToCode()),
        ["sample_rate"] = Kinds(EntityKind.AudioTrack.ToCode()),
        ["playCount"] = Kinds(EntityKind.Video.ToCode(), EntityKind.AudioTrack.ToCode()),
        ["skipCount"] = Kinds(EntityKind.Video.ToCode(), EntityKind.AudioTrack.ToCode()),
        ["resolution"] = Kinds(EntityKind.Video.ToCode()),
        ["videoSeriesId"] = Kinds(EntityKind.Video.ToCode()),
        ["libraryRootId"] = Kinds(CollectionPolicy.ContainableKinds
            .Select(EntityKindRegistry.Describe)
            .Where(definition => definition.LibraryVisibility.Mode != EntityLibraryVisibilityMode.Unscoped)
            .Select(definition => definition.Code)
            .ToArray()),
        ["galleryType"] = Kinds(EntityKind.Gallery.ToCode()),
        ["imageCount"] = Kinds(EntityKind.Gallery.ToCode()),
        ["format"] = Kinds(EntityKind.Image.ToCode()),
        ["interactive"] = Kinds(EntityKind.Video.ToCode()),
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

        IReadOnlySet<Guid>? allowedRootIds = null;
        if (owner.Role != UserRole.Admin) {
            allowedRootIds = await db.UserLibraryAccess.AsNoTracking()
                .Where(access => access.UserId == userId)
                .Select(access => access.LibraryRootId)
                .ToHashSetAsync(cancellationToken);
        }

        var results = new List<CollectionRuleMatch>();

        foreach (var kind in CollectionPolicy.ContainableKinds) {
            var kindCode = EntityKindRegistry.ToCode(kind);
            var query = BuildQuery(group, kindCode, userId, allowedRootIds);
            if (query is null) continue;

            var ids = await ExecuteQueryAsync(query.Value.Sql, query.Value.Parameters, cancellationToken);

            foreach (var id in ids)
                results.Add(new CollectionRuleMatch(kind, id));
        }

        return results;
    }

    internal (string Sql, List<NpgsqlParameter> Parameters)? BuildQuery(
        CollectionRuleGroup group,
        string kindCode,
        Guid userId = default,
        IReadOnlySet<Guid>? allowedRootIds = null) {
        var ctx = new SqlBuildContext(userId);
        var whereFragment = TranslateNode(group, kindCode, ctx);
        if (whereFragment is null) return null;

        var libraryScope = BuildLibraryScope(kindCode, allowedRootIds, ctx);
        return (BuildSql(kindCode, whereFragment, libraryScope, ctx), ctx.Parameters);
    }

    private static string BuildSql(
        string kindCode,
        string whereFragment,
        string? libraryScope,
        SqlBuildContext ctx) {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("SELECT DISTINCT e.id FROM entities e");

        foreach (var join in ctx.Joins)
            sb.AppendLine(join);

        sb.Append("WHERE e.kind_code = ");
        var kindParam = ctx.AddParam(kindCode, NpgsqlDbType.Text);
        sb.AppendLine(kindParam);
        if (KindEquals(kindCode, EntityKind.AudioTrack.ToCode())) {
            // Book-owned tracks are audiobook parts, not music collection candidates.
            var bookKindParam = ctx.AddParam(EntityKind.Book.ToCode(), NpgsqlDbType.Text);
            sb.Append("AND NOT EXISTS (SELECT 1 FROM entities parent WHERE parent.id = e.parent_entity_id AND parent.kind_code = ");
            sb.Append(bookKindParam);
            sb.AppendLine(")");
        }
        sb.Append("AND (");
        sb.Append(whereFragment);
        sb.AppendLine(")");
        if (libraryScope is not null) {
            sb.Append("AND (");
            sb.Append(libraryScope);
            sb.AppendLine(")");
        }

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
        var fragments = new List<string>();
        foreach (var child in group.Children) {
            var fragment = TranslateNode(child, kindCode, ctx);
            if (fragment is not null)
                fragments.Add(fragment);
        }

        if (fragments.Count == 0) return null;

        return group.Operator switch {
            "and" => fragments.Count == 1 ? fragments[0] : $"({string.Join(" AND ", fragments)})",
            "or" => fragments.Count == 1 ? fragments[0] : $"({string.Join(" OR ", fragments)})",
            "not" => fragments.Count == 1
                ? $"NOT ({fragments[0]})"
                : $"NOT ({string.Join(" AND ", fragments)})",
            _ => null
        };
    }

    private string? TranslateCondition(CollectionRuleCondition condition, string kindCode, SqlBuildContext ctx) {
        if (!FieldAppliesToKind(condition.Field, kindCode))
            return null;

        if (condition.EntityTypes.Count > 0 && !ConditionAppliesToKind(condition.EntityTypes, kindCode))
            return null;

        return condition.Field switch {
            "title" => TranslateScalar("e.title", condition.Operator, condition.Value, ctx),
            "rating" => TranslateScalar("e.rating_value", condition.Operator, condition.Value, ctx),
            "date" => TranslateDateField(condition, ctx),
            "organized" => TranslateFlag("is_organized", condition.Operator),
            "isNsfw" => TranslateFlag("is_nsfw", condition.Operator),
            "tags" => TranslateRelation(RelationshipKind.Tags.ToCode(), "tag", condition, ctx),
            "performers" => TranslateRelation(RelationshipKind.Cast.ToCode(), "person", condition, ctx),
            "studio" => TranslateStudioRelation(condition, ctx),
            "fileSize" => TranslateFileSize(condition, ctx),
            "duration" => TranslateTechnical("duration_seconds", condition, ctx),
            "height" => TranslateTechnical("height", condition, ctx),
            "width" => TranslateTechnical("width", condition, ctx),
            "codec" => TranslateTechnical("codec", condition, ctx),
            "bitRate" or "bit_rate" => TranslateTechnical("bit_rate", condition, ctx),
            "channels" => TranslateTechnical("channels", condition, ctx),
            "sampleRate" or "sample_rate" => TranslateTechnical("sample_rate", condition, ctx),
            "playCount" => TranslatePlayback("play_count", condition, ctx),
            "skipCount" => TranslatePlayback("skip_count", condition, ctx),
            "resolution" => TranslateResolution(condition, ctx),
            "videoSeriesId" => TranslateVideoSeries(condition, kindCode, ctx),
            "libraryRootId" => TranslateLibraryRoot(condition, kindCode, ctx),
            "galleryType" => TranslateGalleryType(condition, kindCode, ctx),
            "imageCount" => TranslateChildCount(condition, kindCode, ctx),
            "format" => TranslateTechnical("format", condition, ctx),
            "createdAt" => TranslateDateTimeScalar("e.created_at", condition.Operator, condition.Value, ctx),
            "interactive" => TranslateFlag("is_favorite", condition.Operator),
            _ => null
        };
    }

    private static bool ConditionAppliesToKind(IReadOnlyList<string> entityTypes, string kindCode) =>
        entityTypes.Any(entityType => KindEquals(entityType, kindCode));

    private static bool FieldAppliesToKind(string field, string kindCode) =>
        !FieldTargetKinds.TryGetValue(field, out var kinds) || kinds.Contains(kindCode);

    private static bool KindEquals(string actual, string expected) =>
        actual.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> Kinds(params string[] kindCodes) =>
        new(kindCodes, StringComparer.Ordinal);

    // ── Scalar field translation ──

    private static string? TranslateScalar(string column, string op, JsonElement? value, SqlBuildContext ctx) {
        return op switch {
            "equals" => $"{column} = {ctx.AddJsonParam(value)}",
            "not_equals" => $"{column} != {ctx.AddJsonParam(value)}",
            "contains" => $"{column} ILIKE {ctx.AddParam($"%{value?.GetString()}%", NpgsqlDbType.Text)}",
            "not_contains" => $"NOT ({column} ILIKE {ctx.AddParam($"%{value?.GetString()}%", NpgsqlDbType.Text)})",
            "greater_than" => $"{column} > {ctx.AddJsonParam(value)}",
            "less_than" => $"{column} < {ctx.AddJsonParam(value)}",
            "greater_equal" => $"{column} >= {ctx.AddJsonParam(value)}",
            "less_equal" => $"{column} <= {ctx.AddJsonParam(value)}",
            "between" when value?.ValueKind == JsonValueKind.Array =>
                $"{column} BETWEEN {ctx.AddJsonParam(value?.EnumerateArray().ElementAt(0))} AND {ctx.AddJsonParam(value?.EnumerateArray().ElementAt(1))}",
            "in" when value?.ValueKind == JsonValueKind.Array =>
                $"{column} IN ({string.Join(", ", value.Value.EnumerateArray().Select(v => ctx.AddJsonParam(v)))})",
            "not_in" when value?.ValueKind == JsonValueKind.Array =>
                $"{column} NOT IN ({string.Join(", ", value.Value.EnumerateArray().Select(v => ctx.AddJsonParam(v)))})",
            "is_null" => $"{column} IS NULL",
            "is_not_null" => $"{column} IS NOT NULL",
            "is_true" => $"{column} = true",
            "is_false" => $"{column} = false",
            _ => null
        };
    }

    private string? TranslateTechnical(string column, CollectionRuleCondition condition, SqlBuildContext ctx) {
        ctx.EnsureJoin("LEFT JOIN entity_technical t ON t.entity_id = e.id");
        return TranslateScalar($"t.{column}", condition.Operator, condition.Value, ctx);
    }

    private string? TranslatePlayback(string column, CollectionRuleCondition condition, SqlBuildContext ctx) {
        ctx.EnsureJoin(
            $"LEFT JOIN user_entity_states pb ON pb.entity_id = e.id AND pb.user_id = {ctx.UserIdParameter}");
        return TranslateScalar($"COALESCE(pb.{column}, 0)", condition.Operator, condition.Value, ctx);
    }

    private static string? TranslateFlag(string column, string op) {
        return op switch {
            "is_true" => $"e.{column} = true",
            "is_false" => $"e.{column} = false",
            _ => null
        };
    }

    private string? TranslateDateField(CollectionRuleCondition condition, SqlBuildContext ctx) {
        ctx.EnsureJoin("LEFT JOIN entity_dates ed ON ed.entity_id = e.id AND ed.code IN ('release', 'air')");
        return TranslateDateScalar("ed.sortable_value", condition.Operator, condition.Value, ctx);
    }

    private string? TranslateFileSize(CollectionRuleCondition condition, SqlBuildContext ctx) {
        ctx.EnsureJoin("LEFT JOIN entity_files ef_src ON ef_src.entity_id = e.id AND ef_src.role = 'source'");
        return TranslateScalar("ef_src.size_bytes", condition.Operator, condition.Value, ctx);
    }

    // ── Relation fields (tags, performers) ──

    private string? TranslateRelation(
        string relationshipCode,
        string taxonomyKindCode,
        CollectionRuleCondition condition, SqlBuildContext ctx) {
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

        return condition.Operator switch {
            "in" => subquery,
            "not_in" => $"NOT ({subquery})",
            _ => null
        };
    }

    private string? TranslateStudioRelation(CollectionRuleCondition condition, SqlBuildContext ctx) {
        var relationshipParam = ctx.AddParam(RelationshipKind.Studio.ToCode(), NpgsqlDbType.Text);
        if (condition.Operator is "is_null") {
            return $"NOT EXISTS (SELECT 1 FROM entity_relationship_links sl WHERE sl.entity_id = e.id AND sl.relationship_code = {relationshipParam})";
        }
        if (condition.Operator is "is_not_null") {
            return $"EXISTS (SELECT 1 FROM entity_relationship_links sl WHERE sl.entity_id = e.id AND sl.relationship_code = {relationshipParam})";
        }

        var names = GetStringArray(condition.Value);
        if (names.Count == 0) return "false";

        var nameParams = string.Join(", ", names.Select(n => ctx.AddParam(n, NpgsqlDbType.Text)));
        var kindParam = ctx.AddParam("studio", NpgsqlDbType.Text);

        var subquery = $@"e.id IN (
            SELECT sl.entity_id FROM entity_relationship_links sl
            INNER JOIN entities se ON se.id = sl.target_entity_id
            WHERE sl.relationship_code = {relationshipParam}
                AND sl.target_kind_code = {kindParam}
                AND se.kind_code = {kindParam}
                AND se.title IN ({nameParams})
        )";

        return condition.Operator switch {
            "in" => subquery,
            "not_in" => $"NOT ({subquery})",
            _ => null
        };
    }

    // ── Resolution (maps named tiers to height ranges) ──

    private string? TranslateResolution(CollectionRuleCondition condition, SqlBuildContext ctx) {
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
        return condition.Operator switch {
            "in" => $"({combined})",
            "not_in" => $"NOT ({combined})",
            _ => null
        };
    }

    // ── Video series (structural walk: video -> season -> series) ──

    private string? TranslateVideoSeries(CollectionRuleCondition condition, string kindCode, SqlBuildContext ctx) {
        if (kindCode != "video") return null;

        var predicate = TranslateVideoSeriesPredicate(condition, ctx);
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

        return condition.Operator is "not_in" ? $"NOT ({subquery})" : subquery;
    }

    private static string? TranslateVideoSeriesPredicate(CollectionRuleCondition condition, SqlBuildContext ctx) {
        if (condition.Operator is "equals") {
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

        if (condition.Operator is not ("in" or "not_in") ||
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

    private string? TranslateGalleryType(CollectionRuleCondition condition, string kindCode, SqlBuildContext ctx) {
        if (!KindEquals(kindCode, EntityKind.Gallery.ToCode())) return null;
        ctx.EnsureJoin("LEFT JOIN gallery_details gd ON gd.entity_id = e.id");
        return TranslateScalar("gd.gallery_type", condition.Operator, condition.Value, ctx);
    }

    // ── Child count (count generic structural children) ──

    private string? TranslateChildCount(CollectionRuleCondition condition, string kindCode, SqlBuildContext ctx) {
        if (!KindEquals(kindCode, EntityKind.Gallery.ToCode()) &&
            !KindEquals(kindCode, EntityKind.Book.ToCode())) {
            return null;
        }

        var countExpr = "(SELECT COUNT(*) FROM entities child_count WHERE child_count.parent_entity_id = e.id)";
        return TranslateScalar(countExpr, condition.Operator, condition.Value, ctx);
    }

    // ── Library root membership ──

    private static string? TranslateLibraryRoot(CollectionRuleCondition condition, string kindCode, SqlBuildContext ctx) {
        var existsBuilder = LibraryRootExistsBuilder(kindCode, ctx);
        return existsBuilder is null
            ? null
            : QuantifyLibraryRootMatch(condition, existsBuilder, ctx);
    }

    private static string? BuildLibraryScope(
        string kindCode,
        IReadOnlySet<Guid>? allowedRootIds,
        SqlBuildContext ctx) {
        if (allowedRootIds is null) {
            return null;
        }

        var existsBuilder = LibraryRootExistsBuilder(kindCode, ctx);
        if (existsBuilder is null) {
            return null;
        }

        if (allowedRootIds.Count == 0) {
            return "false";
        }

        var parameters = allowedRootIds
            .Order()
            .Select(id => ctx.AddParam(id, NpgsqlDbType.Uuid))
            .ToArray();
        return existsBuilder(column => parameters.Length == 1
            ? $"{column} = {parameters[0]}"
            : $"{column} IN ({string.Join(", ", parameters)})");
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
        Func<Func<string, string>, string> existsBuilder,
        SqlBuildContext ctx) {
        static string NonNullRoot(string column) => $"{column} IS NOT NULL";

        if (condition.Operator is "is_null") {
            return $"NOT ({existsBuilder(NonNullRoot)})";
        }

        if (condition.Operator is "is_not_null") {
            return existsBuilder(NonNullRoot);
        }

        var selectedRoot = BuildSelectedLibraryRootPredicate(condition, ctx);
        if (selectedRoot is null) return null;

        return condition.Operator switch {
            "equals" or "in" => existsBuilder(selectedRoot),
            "not_equals" or "not_in" => $"({existsBuilder(NonNullRoot)} AND NOT ({existsBuilder(selectedRoot)}))",
            _ => null
        };
    }

    private static Func<string, string>? BuildSelectedLibraryRootPredicate(
        CollectionRuleCondition condition,
        SqlBuildContext ctx) {
        if (condition.Operator is not ("equals" or "not_equals" or "in" or "not_in")) {
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
        $@"EXISTS (
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
        )";

    private static string RootedEntityMatches(
        string entityIdExpression,
        string suffix,
        Func<string, string> rootPredicate) =>
        DirectRootExists($"root_{suffix}", entityIdExpression, rootPredicate);

    // ── Helpers ──

    private static string? TranslateDateScalar(string column, string op, JsonElement? value, SqlBuildContext ctx) {
        return op switch {
            "equals" => $"{column} = {ctx.AddDateParam(value)}",
            "not_equals" => $"{column} != {ctx.AddDateParam(value)}",
            "greater_than" => $"{column} > {ctx.AddDateParam(value)}",
            "less_than" => $"{column} < {ctx.AddDateParam(value)}",
            "greater_equal" => $"{column} >= {ctx.AddDateParam(value)}",
            "less_equal" => $"{column} <= {ctx.AddDateParam(value)}",
            "between" when value?.ValueKind == JsonValueKind.Array =>
                $"{column} BETWEEN {ctx.AddDateParam(value?.EnumerateArray().ElementAt(0))} AND {ctx.AddDateParam(value?.EnumerateArray().ElementAt(1))}",
            "is_null" => $"{column} IS NULL",
            "is_not_null" => $"{column} IS NOT NULL",
            _ => null
        };
    }

    private static string? TranslateDateTimeScalar(string column, string op, JsonElement? value, SqlBuildContext ctx) {
        return op switch {
            "equals" => $"{column} = {ctx.AddDateTimeParam(value)}",
            "not_equals" => $"{column} != {ctx.AddDateTimeParam(value)}",
            "greater_than" => $"{column} > {ctx.AddDateTimeParam(value)}",
            "less_than" => $"{column} < {ctx.AddDateTimeParam(value)}",
            "greater_equal" => $"{column} >= {ctx.AddDateTimeParam(value)}",
            "less_equal" => $"{column} <= {ctx.AddDateTimeParam(value)}",
            "between" when value?.ValueKind == JsonValueKind.Array =>
                $"{column} BETWEEN {ctx.AddDateTimeParam(value?.EnumerateArray().ElementAt(0))} AND {ctx.AddDateTimeParam(value?.EnumerateArray().ElementAt(1))}",
            "is_null" => $"{column} IS NULL",
            "is_not_null" => $"{column} IS NOT NULL",
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
