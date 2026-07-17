namespace Prismedia.Domain.Tests;

/// <summary>
/// Guards the Phase 1 constants consolidation: each centralized, unambiguous identifier
/// literal must live only in its owning constants/enum file. If one of these strings is
/// reintroduced as an inline literal elsewhere in the backend source, this test fails so
/// the drift is caught in review rather than silently spreading again.
/// </summary>
/// <remarks>
/// Only unambiguous literals are guarded. Tokens that legitimately appear in multiple
/// vocabularies (for example <c>"cast"</c> as a relationship code, a credit label, and a
/// metadata field selector, or <c>"scan"</c>/<c>"custom"</c> as substrings) are validated
/// by their round-trip enum tests instead, not by source scanning.
/// </remarks>
public sealed class ConstantsDriftGuardTests {
    /// <summary>Literal value mapped to the single source file (by name) allowed to contain it.</summary>
    private static readonly IReadOnlyDictionary<string, string> OwnedLiterals = new Dictionary<string, string> {
        // Jellyfin auth headers — owned by JellyfinProtocol.
        ["\"X-Emby-Authorization\""] = "JellyfinProtocol.cs",
        ["\"X-Emby-Token\""] = "JellyfinProtocol.cs",
        ["\"X-MediaBrowser-Token\""] = "JellyfinProtocol.cs",
        ["\"X-Prismedia-Api-Key\""] = "JellyfinProtocol.cs",
        // Jellyfin ImageType values — owned by JellyfinProtocol.ImageTypes. Unambiguous:
        // these PascalCase tokens appear nowhere else in the backend (lowercase asset-role
        // spellings like "backdrop"/"logo" are a separate vocabulary and are not guarded here).
        ["\"Primary\""] = "JellyfinProtocol.cs",
        ["\"Backdrop\""] = "JellyfinProtocol.cs",
        ["\"Logo\""] = "JellyfinProtocol.cs",
        ["\"Thumb\""] = "JellyfinProtocol.cs",
        ["\"Banner\""] = "JellyfinProtocol.cs",
        ["\"Art\""] = "JellyfinProtocol.cs",
        ["\"Disc\""] = "JellyfinProtocol.cs",
        ["\"Box\""] = "JellyfinProtocol.cs",
        ["\"Screenshot\""] = "JellyfinProtocol.cs",
        // MIME types — owned by MediaContentTypes.
        ["\"application/vnd.apple.mpegurl\""] = "MediaContentTypes.cs",
        ["\"video/mp2t\""] = "MediaContentTypes.cs",
        ["\"video/x-matroska\""] = "MediaContentTypes.cs",
        ["\"text/x-ssa; charset=utf-8\""] = "MediaContentTypes.cs",
    };

    [Fact]
    public void OwnedLiteralsLiveOnlyInTheirConstantsFile() {
        var srcRoot = LocateBackendSrcRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)) {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) {
                continue;
            }

            var fileName = Path.GetFileName(file);
            var text = File.ReadAllText(file);
            foreach (var (literal, owner) in OwnedLiterals) {
                if (!string.Equals(fileName, owner, StringComparison.Ordinal) && text.Contains(literal, StringComparison.Ordinal)) {
                    offenders.Add($"{literal} found in {fileName} (owned by {owner})");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Centralized literals must not be retyped inline:\n" + string.Join("\n", offenders));
    }

    private static string LocateBackendSrcRoot() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null) {
            var candidate = Path.Combine(dir.FullName, "apps", "backend", "src");
            if (Directory.Exists(candidate)) {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate apps/backend/src from the test base directory.");
    }
}
