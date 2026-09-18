using Prismedia.Archiver;

namespace Prismedia.ArchiverConformance;

/// <summary>Explicit endpoint and finite test selection. Without execute/resume, only the system endpoint is read.</summary>
public sealed record ConformanceOptions(Uri Endpoint, string? Input, string? Kind, string? Format,
    string? OutputDirectory, string? ResumePath) {
    /// <summary>Parses the documented command line and rejects ambiguous execution or credential-bearing endpoints.</summary>
    public static ConformanceOptions Parse(string[] args) {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var execute = false;
        for (var i = 0; i < args.Length; i++) {
            if (args[i] == "--execute") { if (execute) throw new ArgumentException("Duplicate --execute."); execute = true; continue; }
            if (args[i] is not ("--endpoint" or "--input" or "--kind" or "--format" or "--output-directory" or "--resume")
                || i + 1 >= args.Length || !values.TryAdd(args[i], args[++i]))
                throw new ArgumentException("Unknown, duplicate, or incomplete option.");
        }
        if (!values.TryGetValue("--endpoint", out var endpoint) || !Uri.TryCreate(endpoint.TrimEnd('/') + "/", UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Supply an HTTP(S) endpoint without credentials, query, or fragment.");
        var input = values.GetValueOrDefault("--input"); var kind = values.GetValueOrDefault("--kind");
        var format = values.GetValueOrDefault("--format"); var output = values.GetValueOrDefault("--output-directory");
        var resume = values.GetValueOrDefault("--resume");
        if (resume is not null && (execute || input is not null || kind is not null || format is not null || output is not null))
            throw new ArgumentException("Resume uses the saved plan; do not combine it with a new selection.");
        if (!execute && resume is null && values.Count != 1) throw new ArgumentException("Use --execute to create test work.");
        if (execute && (input is null || output is null || !Compatible(kind, format)))
            throw new ArgumentException("Execution needs input, output-directory, and a compatible kind/format: book/epub, comic/cbz, image/png, gallery/image-set.");
        return new(uri, input, kind, format, output, resume);
    }

    private static bool Compatible(string? kind, string? format) => (kind, format) is
        (ArchiverWire.Book, ArchiverWire.Epub) or (ArchiverWire.Comic, ArchiverWire.Cbz)
        or (ArchiverWire.Image, ArchiverWire.Png) or (ArchiverWire.Gallery, ArchiverWire.ImageSet);
}
