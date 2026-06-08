using System.Text.RegularExpressions;

namespace Prismedia.Domain.Tests;

/// <summary>
/// Enforces the Identifier Discipline contract for API problem codes: every
/// <c>ApiProblem</c> / <c>FileOperationException</c> is constructed with a code drawn from
/// the canonical <c>ApiProblemCodes</c> constants, never a bare string literal. A literal
/// first argument reintroduces the magic-string drift the constants class exists to prevent,
/// so this test fails in review instead.
/// </summary>
public sealed class ApiProblemCodeDisciplineTests {
    // The first argument is a bare string literal (starts with a quote) rather than a
    // constant reference. Variable/expression first args (e.g. error.Code) are allowed.
    private static readonly Regex BareApiProblem =
        new("""new\s+ApiProblem\(\s*"[a-z]""", RegexOptions.Compiled);
    private static readonly Regex BareFileOperationException =
        new("""new\s+FileOperationException\(\s*"[a-z]""", RegexOptions.Compiled);

    [Fact]
    public void ProblemCodesAreNeverConstructedFromBareLiterals() {
        var srcRoot = LocateBackendSrcRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)) {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) {
                continue;
            }

            var fileName = Path.GetFileName(file);
            // The constants class itself is the one declaration site for the literals.
            if (string.Equals(fileName, "ApiProblemCodes.cs", StringComparison.Ordinal)) continue;

            var text = File.ReadAllText(file);
            if (BareApiProblem.IsMatch(text) || BareFileOperationException.IsMatch(text)) {
                offenders.Add(fileName);
            }
        }

        Assert.True(offenders.Count == 0,
            "API problem codes must reference ApiProblemCodes constants, not bare literals. Offending files:\n"
            + string.Join("\n", offenders.Distinct()));
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
