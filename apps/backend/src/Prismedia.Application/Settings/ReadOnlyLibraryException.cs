namespace Prismedia.Application.Settings;

/// <summary>A library configuration change would remove an external file ownership boundary.</summary>
public sealed class ReadOnlyLibraryException(string message) : InvalidOperationException(message);
