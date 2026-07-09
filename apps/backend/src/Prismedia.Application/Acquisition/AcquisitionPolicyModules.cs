using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Owns every release-search policy that varies by acquisition kind: query construction, Torznab
/// category routing, and release evaluation. A module may serve multiple entity kinds when they share
/// one upstream release vocabulary, such as TV series, season packs, and episodes.
/// </summary>
public interface IAcquisitionPolicyModule {
    /// <summary>The entity kinds whose release searches this module owns.</summary>
    IReadOnlyCollection<EntityKind> SupportedKinds { get; }

    /// <summary>Builds the ordered, most-specific-first query ladder for one acquisition.</summary>
    IReadOnlyList<string> BuildQueries(AcquisitionSearchInput input);

    /// <summary>Narrows an indexer's configured Torznab categories to this module's media range.</summary>
    IReadOnlyList<int> RouteCategories(IReadOnlyList<int> configuredCategories);

    /// <summary>Returns the release decision engine bound to the requested supported kind.</summary>
    IAcquisitionDecisionEngine DecisionEngineFor(EntityKind kind);
}

/// <summary>Resolves the one acquisition policy module registered for an entity kind.</summary>
public interface IAcquisitionPolicyRegistry {
    /// <summary>Returns the module for <paramref name="kind"/>, or throws when none is registered.</summary>
    IAcquisitionPolicyModule Get(EntityKind kind);
}

/// <summary>
/// Deterministic registry over acquisition policy modules. Registration order never changes resolution;
/// duplicate kind ownership is rejected at construction so adding a module cannot silently replace policy.
/// </summary>
public sealed class AcquisitionPolicyRegistry : IAcquisitionPolicyRegistry {
    private readonly IReadOnlyDictionary<EntityKind, IAcquisitionPolicyModule> _byKind;

    public AcquisitionPolicyRegistry(IEnumerable<IAcquisitionPolicyModule> modules) {
        var byKind = new Dictionary<EntityKind, IAcquisitionPolicyModule>();
        foreach (var module in modules.OrderBy(ModuleName, StringComparer.Ordinal)) {
            foreach (var kind in module.SupportedKinds.OrderBy(candidate => candidate.ToCode(), StringComparer.Ordinal)) {
                if (byKind.TryGetValue(kind, out var existing)) {
                    throw new InvalidOperationException(
                        $"Acquisition policy kind '{kind.ToCode()}' is registered by both " +
                        $"'{ModuleName(existing)}' and '{ModuleName(module)}'.");
                }

                byKind.Add(kind, module);
            }
        }

        _byKind = byKind;
    }

    /// <inheritdoc />
    public IAcquisitionPolicyModule Get(EntityKind kind) =>
        _byKind.TryGetValue(kind, out var module)
            ? module
            : throw new InvalidOperationException(
                $"No acquisition policy module is registered for kind '{kind.ToCode()}'.");

    private static string ModuleName(IAcquisitionPolicyModule module) =>
        module.GetType().FullName ?? module.GetType().Name;
}

/// <summary>Release-search policy for books and comics.</summary>
public sealed class BookAcquisitionPolicyModule : AcquisitionPolicyModule {
    public BookAcquisitionPolicyModule()
        : base(TorznabCategoryRange.Books, [new BookReleaseDecisionEngine()]) { }

    /// <inheritdoc />
    public override IReadOnlyList<string> BuildQueries(AcquisitionSearchInput input) =>
        AcquisitionPolicyQueries.FromTitle(input, [
            AcquisitionPolicyQueries.Join(input.Title, input.Author),
            input.Title
        ]);
}

/// <summary>Release-search policy for movies.</summary>
public sealed class MovieAcquisitionPolicyModule : AcquisitionPolicyModule {
    public MovieAcquisitionPolicyModule()
        : base(TorznabCategoryRange.Movies, [new MovieReleaseDecisionEngine()]) { }

    /// <inheritdoc />
    public override IReadOnlyList<string> BuildQueries(AcquisitionSearchInput input) =>
        AcquisitionPolicyQueries.FromTitle(input, [
            AcquisitionPolicyQueries.Join(input.Title, input.Year?.ToString()),
            input.Title
        ]);
}

/// <summary>Release-search policy shared by albums, tracks, and artists.</summary>
public sealed class MusicAcquisitionPolicyModule : AcquisitionPolicyModule {
    public MusicAcquisitionPolicyModule()
        : base(TorznabCategoryRange.Audio, [
            new MusicReleaseDecisionEngine(EntityKind.AudioLibrary),
            new MusicReleaseDecisionEngine(EntityKind.AudioTrack),
            new MusicReleaseDecisionEngine(EntityKind.MusicArtist)
        ]) { }

    /// <inheritdoc />
    public override IReadOnlyList<string> BuildQueries(AcquisitionSearchInput input) =>
        AcquisitionPolicyQueries.FromTitle(input, [
            AcquisitionPolicyQueries.Join(input.Author, input.Title),
            input.Title
        ]);
}

/// <summary>Release-search policy shared by TV series, season packs, and episodes.</summary>
public sealed class TvAcquisitionPolicyModule : AcquisitionPolicyModule {
    public TvAcquisitionPolicyModule()
        : base(TorznabCategoryRange.Tv, [
            new TvReleaseDecisionEngine(EntityKind.VideoSeries),
            new TvReleaseDecisionEngine(EntityKind.VideoSeason),
            new TvReleaseDecisionEngine(EntityKind.Video)
        ]) { }

    /// <inheritdoc />
    public override IReadOnlyList<string> BuildQueries(AcquisitionSearchInput input) {
        if (string.IsNullOrWhiteSpace(input.Title)) {
            return [];
        }

        var tvBase = string.IsNullOrWhiteSpace(input.Series) ? input.Title : input.Series;
        if (input.Kind == EntityKind.Video && input is { SeasonNumber: { } season, EpisodeNumber: { } episode }) {
            return AcquisitionPolicyQueries.Normalize([
                AcquisitionPolicyQueries.Join(tvBase, $"S{season:00}E{episode:00}"),
                AcquisitionPolicyQueries.Join(tvBase, $"{season}x{episode:00}")
            ]);
        }

        // A direct video without complete TV-unit context keeps the established movie-style fallback.
        if (input.Kind == EntityKind.Video) {
            return AcquisitionPolicyQueries.Normalize([
                AcquisitionPolicyQueries.Join(input.Title, input.Year?.ToString()),
                input.Title
            ]);
        }

        if (input.Kind == EntityKind.VideoSeason && input.SeasonNumber is { } seasonNumber) {
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
}

/// <summary>Shared mechanics for modules whose supported kinds are defined by their decision engines.</summary>
public abstract class AcquisitionPolicyModule : IAcquisitionPolicyModule {
    private readonly IReadOnlyDictionary<EntityKind, IAcquisitionDecisionEngine> _decisionEngines;
    private readonly TorznabCategoryRange _categoryRange;

    private protected AcquisitionPolicyModule(
        TorznabCategoryRange categoryRange,
        IEnumerable<IAcquisitionDecisionEngine> decisionEngines) {
        _categoryRange = categoryRange;
        _decisionEngines = decisionEngines.ToDictionary(engine => engine.Kind);
        SupportedKinds = _decisionEngines.Keys.ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<EntityKind> SupportedKinds { get; }

    /// <inheritdoc />
    public abstract IReadOnlyList<string> BuildQueries(AcquisitionSearchInput input);

    /// <inheritdoc />
    public IReadOnlyList<int> RouteCategories(IReadOnlyList<int> configuredCategories) =>
        _categoryRange.Route(configuredCategories);

    /// <inheritdoc />
    public IAcquisitionDecisionEngine DecisionEngineFor(EntityKind kind) =>
        _decisionEngines.TryGetValue(kind, out var engine)
            ? engine
            : throw new InvalidOperationException(
                $"Acquisition policy module '{GetType().Name}' does not support kind '{kind.ToCode()}'.");
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

    /// <summary>
    /// Preserves configured categories in this media range, falling back to its top-level category, and
    /// always carries configured kind-neutral Other-range categories through.
    /// </summary>
    public IReadOnlyList<int> Route(IReadOnlyList<int> configuredCategories) {
        var kindPicks = configuredCategories
            .Where(category => category >= start && category < start + RangeSize)
            .ToArray();
        var otherPicks = configuredCategories
            .Where(category => category >= Other && category < Other + RangeSize);
        return (kindPicks.Length > 0 ? kindPicks : [start]).Concat(otherPicks).ToArray();
    }
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
}
