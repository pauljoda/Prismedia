using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Owns every release-search policy that varies by acquisition naming family: query construction,
/// Torznab category routing, and release evaluation. Participating acquisition kinds are derived from
/// their Entity definitions; modules only declare the shared family algorithm they implement.
/// </summary>
public interface IAcquisitionPolicyModule {
    /// <summary>Builds the ordered, most-specific-first query ladder for one acquisition.</summary>
    IReadOnlyList<string> BuildQueries(AcquisitionSearchInput input);

    /// <summary>
    /// Builds queries used only when the primary ladder produces no acceptable release. Most naming
    /// families have no separate fallback; TV episodes use their provider title after exact unit queries
    /// fail so a title-only release can still be found without adding traffic to successful searches.
    /// </summary>
    IReadOnlyList<string> BuildFallbackQueries(AcquisitionSearchInput input) => [];

    /// <summary>Narrows an indexer's configured Torznab categories to this module's media range.</summary>
    IReadOnlyList<int> RouteCategories(AcquisitionSearchInput input, IReadOnlyList<int> configuredCategories);

    /// <summary>Returns the release decision engine specialized to the requested family-owned kind.</summary>
    IAcquisitionDecisionEngine DecisionEngineFor(EntityKind kind);
}

/// <summary>Resolves the one acquisition policy module registered for an entity kind.</summary>
public interface IAcquisitionPolicyRegistry {
    /// <summary>Returns the module for <paramref name="kind"/>, or throws when none is registered.</summary>
    IAcquisitionPolicyModule Get(EntityKind kind);
}

/// <summary>
/// Deterministic registry over acquisition policy modules. Entity-kind definitions and request descriptors
/// derive the kind-to-family map; decorated modules provide exactly one policy per resulting family.
/// </summary>
public sealed class AcquisitionPolicyRegistry : IAcquisitionPolicyRegistry {
    private readonly IReadOnlyDictionary<EntityKind, IAcquisitionPolicyModule> _byKind;

    public AcquisitionPolicyRegistry(IEnumerable<IAcquisitionPolicyModule> modules) =>
        _byKind = AcquisitionStrategyRegistration.ResolveByAcquisitionKind(modules, "search policy");

    /// <inheritdoc />
    public IAcquisitionPolicyModule Get(EntityKind kind) =>
        _byKind.TryGetValue(kind, out var module)
            ? module
            : throw new InvalidOperationException(
                $"No acquisition policy module is registered for kind '{kind.ToCode()}'.");

}

/// <summary>Release-search policy for books and comics.</summary>
[AcquisitionStrategy(AcquisitionNamingFamily.Book)]
public sealed class BookAcquisitionPolicyModule : AcquisitionPolicyModule {
    public BookAcquisitionPolicyModule()
        : base(TorznabCategoryRange.Books, static kind => new BookReleaseDecisionEngine(kind)) { }

    /// <inheritdoc />
    public override IReadOnlyList<string> BuildQueries(AcquisitionSearchInput input) =>
        AcquisitionPolicyQueries.FromTitle(input, [
            AcquisitionPolicyQueries.Join(input.Title, input.Author),
            input.Title
        ]);

    /// <inheritdoc />
    public override IReadOnlyList<int> RouteCategories(
        AcquisitionSearchInput input,
        IReadOnlyList<int> configuredCategories) {
        var category = input.BookRendition == BookRendition.Audiobook
            ? TorznabCategory.AudioAudiobook
            : TorznabCategory.BooksEbook;
        return [category, .. TorznabCategoryRange.OtherCategories(configuredCategories)];
    }
}

/// <summary>Release-search policy for movies.</summary>
[AcquisitionStrategy(AcquisitionNamingFamily.Movie)]
public sealed class MovieAcquisitionPolicyModule : AcquisitionPolicyModule {
    public MovieAcquisitionPolicyModule()
        : base(TorznabCategoryRange.Movies, static kind => new MovieReleaseDecisionEngine(kind)) { }

    /// <inheritdoc />
    public override IReadOnlyList<string> BuildQueries(AcquisitionSearchInput input) =>
        AcquisitionPolicyQueries.FromTitle(input, [
            AcquisitionPolicyQueries.Join(input.Title, input.Year?.ToString()),
            input.Title
        ]);
}

/// <summary>Release-search policy shared by albums, tracks, and artists.</summary>
[AcquisitionStrategy(AcquisitionNamingFamily.Music)]
public sealed class MusicAcquisitionPolicyModule : AcquisitionPolicyModule {
    public MusicAcquisitionPolicyModule()
        : base(TorznabCategoryRange.Audio, static kind => new MusicReleaseDecisionEngine(kind)) { }

    /// <inheritdoc />
    public override IReadOnlyList<string> BuildQueries(AcquisitionSearchInput input) =>
        AcquisitionPolicyQueries.FromTitle(input, IsFileUnit(input.Kind)
            ? [
                AcquisitionPolicyQueries.JoinDistinct(input.Author, input.Series, input.Title),
                AcquisitionPolicyQueries.JoinDistinct(input.Series, input.Title),
                AcquisitionPolicyQueries.JoinDistinct(input.Author, input.Title),
                input.Title
            ]
            : [
                AcquisitionPolicyQueries.Join(input.Author, input.Title),
                input.Title
            ]);
}

/// <summary>Release-search policy shared by TV series, season packs, and episodes.</summary>
[AcquisitionStrategy(AcquisitionNamingFamily.Television)]
public sealed class TvAcquisitionPolicyModule : AcquisitionPolicyModule {
    public TvAcquisitionPolicyModule()
        : base(TorznabCategoryRange.Tv, static kind => new TvReleaseDecisionEngine(kind)) { }

    /// <inheritdoc />
    public override IReadOnlyList<string> BuildQueries(AcquisitionSearchInput input) {
        if (string.IsNullOrWhiteSpace(input.Title)) {
            return [];
        }

        var tvBase = string.IsNullOrWhiteSpace(input.Series) ? input.Title : input.Series;
        if (IsFileUnit(input.Kind) && input is { SeasonNumber: { } season, EpisodeNumber: { } episode }) {
            return AcquisitionPolicyQueries.Normalize([
                AcquisitionPolicyQueries.Join(tvBase, $"S{season:00}E{episode:00}"),
                AcquisitionPolicyQueries.Join(tvBase, $"{season}x{episode:00}")
            ]);
        }

        if (IsNestedContainer(input.Kind) && input.SeasonNumber is { } seasonNumber) {
            return AcquisitionPolicyQueries.Normalize([
                AcquisitionPolicyQueries.Join(tvBase, $"S{seasonNumber:00}"),
                AcquisitionPolicyQueries.Join(tvBase, $"Season {seasonNumber}"),
                AcquisitionPolicyQueries.Join(tvBase, "complete")
            ]);
        }

        return AcquisitionPolicyQueries.Normalize([
            AcquisitionPolicyQueries.Join(tvBase, "complete"),
            tvBase
        ]);
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> BuildFallbackQueries(AcquisitionSearchInput input) {
        if (!IsFileUnit(input.Kind)
            || input.EpisodeNumber is null
            || string.IsNullOrWhiteSpace(input.Title)) {
            return [];
        }

        var tvBase = string.IsNullOrWhiteSpace(input.Series) ? null : input.Series;
        return AcquisitionPolicyQueries.Normalize([
            AcquisitionPolicyQueries.JoinDistinct(tvBase, input.Title)
        ]);
    }
}

/// <summary>Shared mechanics for family-owned policy modules.</summary>
public abstract class AcquisitionPolicyModule : IAcquisitionPolicyModule {
    private readonly TorznabCategoryRange _categoryRange;
    private readonly Func<EntityKind, IAcquisitionDecisionEngine> _decisionEngineFactory;

    private protected AcquisitionPolicyModule(
        TorznabCategoryRange categoryRange,
        Func<EntityKind, IAcquisitionDecisionEngine> decisionEngineFactory) {
        _categoryRange = categoryRange;
        _decisionEngineFactory = decisionEngineFactory;
    }

    /// <inheritdoc />
    public abstract IReadOnlyList<string> BuildQueries(AcquisitionSearchInput input);

    /// <inheritdoc />
    public virtual IReadOnlyList<string> BuildFallbackQueries(AcquisitionSearchInput input) => [];

    /// <inheritdoc />
    public virtual IReadOnlyList<int> RouteCategories(
        AcquisitionSearchInput input,
        IReadOnlyList<int> configuredCategories) =>
        _categoryRange.Route(configuredCategories);

    /// <inheritdoc />
    public IAcquisitionDecisionEngine DecisionEngineFor(EntityKind kind) {
        var family = AcquisitionStrategyRegistration.TryGetNamingFamily(kind);
        var ownedFamily = AcquisitionStrategyRegistration.FamilyOf(this);
        if (family != ownedFamily) {
            throw new InvalidOperationException(
                $"Acquisition policy module '{GetType().Name}' serves family '{ownedFamily.ToCode()}', " +
                $"not kind '{kind.ToCode()}' (family '{family?.ToCode() ?? "none"}').");
        }

        var engine = _decisionEngineFactory(kind);
        if (engine.Kind != kind) {
            throw new InvalidOperationException(
                $"Acquisition policy module '{GetType().Name}' returned decision engine '{engine.GetType().Name}' " +
                $"for '{engine.Kind.ToCode()}', not requested kind '{kind.ToCode()}'.");
        }

        return engine;
    }

    /// <summary>Whether a family acquisition unit is represented by one physical file.</summary>
    protected static bool IsFileUnit(EntityKind kind) =>
        EntityKindRegistry.Describe(kind).StorageShape == EntityStorageShape.File;

    /// <summary>Whether a family acquisition unit is a structural container beneath another entity.</summary>
    protected static bool IsNestedContainer(EntityKind kind) {
        var definition = EntityKindRegistry.Describe(kind);
        return definition.StorageShape == EntityStorageShape.Folder && definition.StructurePolicy.RequiresParent;
    }
}

/// <summary>One Torznab top-level category range and its configured-category routing behavior.</summary>
internal sealed class TorznabCategoryRange(int start) {
    // Torznab top-level numeric category ranges. prism-vocab: external (Torznab category standard).
    public static TorznabCategoryRange Movies { get; } = new(2000);
    public static TorznabCategoryRange Audio { get; } = new(3000);
    public static TorznabCategoryRange Tv { get; } = new(5000);
    public static TorznabCategoryRange Books { get; } = new(7000);

    private const int Other = 8000;
    private const int RangeSize = 1000;

    internal static IEnumerable<int> OtherCategories(IReadOnlyList<int> configuredCategories) =>
        configuredCategories.Where(category => category >= Other && category < Other + RangeSize);

    /// <summary>
    /// Preserves configured categories in this media range, falling back to its top-level category, and
    /// always carries configured kind-neutral Other-range categories through.
    /// </summary>
    public IReadOnlyList<int> Route(IReadOnlyList<int> configuredCategories) {
        var kindPicks = configuredCategories
            .Where(category => category >= start && category < start + RangeSize)
            .ToArray();
        var otherPicks = OtherCategories(configuredCategories);
        return (kindPicks.Length > 0 ? kindPicks : [start]).Concat(otherPicks).ToArray();
    }
}

/// <summary>External Torznab leaf categories used as book-rendition routing boundaries.</summary>
internal static class TorznabCategory {
    // prism-vocab: external (Torznab/Newznab category standard).
    public const int AudioAudiobook = 3030;
    public const int BooksEbook = 7020;
}

/// <summary>Formatting and duplicate-collapse mechanics shared by per-kind query builders.</summary>
internal static class AcquisitionPolicyQueries {
    public static IReadOnlyList<string> FromTitle(
        AcquisitionSearchInput input,
        IEnumerable<string?> queries) =>
        string.IsNullOrWhiteSpace(input.Title) ? [] : Normalize(queries);

    public static IReadOnlyList<string> Normalize(IEnumerable<string?> queries) =>
        queries
            .Where(query => !string.IsNullOrWhiteSpace(query))
            .Select(query => query!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static string Join(string? left, string? right) =>
        string.Join(' ', new[] { left, right }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim()));

    public static string JoinDistinct(params string?[] parts) =>
        string.Join(' ', parts
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase));
}
