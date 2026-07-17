using System.Text.RegularExpressions;
using Prismedia.Api.Codegen;

namespace Prismedia.Api.Tests;

/// <summary>
/// Offline parity guard between the backend code registries and the committed frontend
/// <c>codes.ts</c>. The full regenerate-and-diff check (<c>pnpm api:check</c>) needs a
/// running dev API, so it only runs in the manual validate workflow; this test compares
/// <see cref="CodesManifest.Build()"/> against the committed file by reflection alone,
/// letting push CI catch a backend [Code] enum change that was not regenerated.
/// </summary>
public sealed partial class GeneratedCodesParityTests {
    private const string GeneratedCodesPath = "apps/web-svelte/src/lib/api/generated/codes.ts";

    [GeneratedRegex(
        """// source: (?<source>[^\n]+)\nexport const (?<name>[A-Z0-9_]+) = \{(?<body>[^}]*)\} as const;""",
        RegexOptions.Singleline)]
    private static partial Regex ConstBlockRegex();

    [GeneratedRegex(""": "(?<value>[^"]*)",""")]
    private static partial Regex ValueRegex();

    /// <summary>Parses the generated const blocks keyed by their source annotation.</summary>
    private static IReadOnlyDictionary<string, IReadOnlySet<string>> ParseGeneratedBlocks() {
        var source = File.ReadAllText(RepoPath(GeneratedCodesPath));
        var blocks = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (Match match in ConstBlockRegex().Matches(source)) {
            var values = ValueRegex().Matches(match.Groups["body"].Value)
                .Select(value => value.Groups["value"].Value)
                .ToHashSet(StringComparer.Ordinal);
            blocks[match.Groups["source"].Value] = values;
        }

        return blocks;
    }

    [Fact]
    public void EveryBackendCodeEnumIsSurfacedWithMatchingValues() {
        var manifest = CodesManifest.Build();
        var blocks = ParseGeneratedBlocks();

        foreach (var (enumName, entries) in manifest.Enums) {
            var expected = entries.Select(entry => entry.Code).ToHashSet(StringComparer.Ordinal);
            Assert.True(
                blocks.TryGetValue($"enum {enumName}", out var actual),
                $"codes.ts has no const generated from enum '{enumName}'. Run `pnpm api:generate` and commit the result.");
            Assert.True(
                expected.SetEquals(actual!),
                $"codes.ts values for enum '{enumName}' are stale. Missing: [{string.Join(", ", expected.Except(actual!))}]; " +
                $"extra: [{string.Join(", ", actual!.Except(expected))}]. Run `pnpm api:generate` and commit the result.");
        }
    }

    [Fact]
    public void EveryBackendConstantRegistryIsSurfacedWithMatchingValues() {
        var manifest = CodesManifest.Build();
        var blocks = ParseGeneratedBlocks();
        var registries = new (string Source, IEnumerable<string> Expected)[]
        {
            ("registry CapabilityKinds", manifest.CapabilityKinds),
            ("registry ExternalIdProviders", manifest.ExternalIdProviders.Select(entry => entry.Value)),
            ("registry AppSettingKeys", manifest.SettingKeys.Select(entry => entry.Value)),
            ("registry ApiProblemCodes", manifest.ProblemCodes.Select(entry => entry.Value)),
        };

        foreach (var (source, expected) in registries) {
            var expectedSet = expected.ToHashSet(StringComparer.Ordinal);
            Assert.True(
                blocks.TryGetValue(source, out var actual),
                $"codes.ts has no const generated from '{source}'. Run `pnpm api:generate` and commit the result.");
            Assert.True(
                expectedSet.SetEquals(actual!),
                $"codes.ts values for '{source}' are stale. Missing: [{string.Join(", ", expectedSet.Except(actual!))}]; " +
                $"extra: [{string.Join(", ", actual!.Except(expectedSet))}]. Run `pnpm api:generate` and commit the result.");
        }
    }

    private static string RepoPath(string relativePath) {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate) || Directory.Exists(candidate)) {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not resolve repo path '{relativePath}'.");
    }
}
