namespace Prismedia.Application.Acquisition;

/// <summary>Protects publication byte format when a profile renders a standalone book's filename.</summary>
public static class BookNamingTemplates {
    /// <summary>The book renderer substitutes the selected source file's extension at this token.</summary>
    public const string ExtensionToken = "{ext}";
    private const string InvalidFileTemplate = "A book file template needs a filename ending with .{ext} so the imported file keeps its EPUB or PDF format.";

    /// <summary>Rejects filenames that discard or mislabel the source format before any placement is accepted.</summary>
    public static string? ValidateFileTemplate(string template) {
        var fileName = template.Trim().Split('/')[^1];
        var suffix = "." + ExtensionToken;
        return fileName.EndsWith(suffix, StringComparison.Ordinal) && fileName[..^suffix.Length].Trim().Length > 0
            ? null : InvalidFileTemplate;
    }

    /// <summary>Checks the rendered filename after optional metadata and empty punctuation have been removed.</summary>
    public static string? ValidateRenderedFile(string path, string expectedExtension) =>
        Path.GetExtension(path).Equals(expectedExtension, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(path)) ? null : InvalidFileTemplate;
}
