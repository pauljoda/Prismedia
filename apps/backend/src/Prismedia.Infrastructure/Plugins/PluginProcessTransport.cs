using System.Text;
using System.Text.Json;
using Prismedia.Infrastructure.Processes;
using Prismedia.Infrastructure.Serialization;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Bounded invocation transport shared by executable plugin capabilities. Plugins remain trusted
/// processes; environment filtering and private request files are not an operating-system sandbox.
/// </summary>
public sealed class PluginProcessTransport(ProcessExecutor processes, PluginCatalogOptions options) {
    private const int MaximumRequestCharacters = 4 * 1024 * 1024;
    private static readonly ProcessExecutionOptions Execution = new(
        MaxStandardOutputCharacters: 16 * 1024 * 1024,
        MaxStandardErrorCharacters: 64 * 1024,
        InheritEnvironment: false);

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 64,
        Converters = { new CodecJsonConverterFactory() }
    };

    /// <summary>Writes a private invocation request, captures bounded output, and removes the request on every exit path.</summary>
    /// <param name="descriptor">Installed executable and manifest.</param>
    /// <param name="request">Typed request envelope for the selected capability protocol.</param>
    /// <param name="cancellationToken">Operation deadline or caller cancellation.</param>
    /// <returns>Complete bounded process output and exit status.</returns>
    public async Task<ProcessExecutionResult> RunAsync<TRequest>(PluginDescriptor descriptor, TRequest request,
        CancellationToken cancellationToken) where TRequest : notnull {
        var json = JsonSerializer.Serialize(request, JsonOptions);
        if (json.Length > MaximumRequestCharacters) throw new InvalidDataException("Plugin request exceeded its size limit.");
        var directory = Path.Combine(options.CacheRoot, "plugins", "requests");
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}.json");
        try {
            var fileOptions = new FileStreamOptions {
                Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
                Options = FileOptions.Asynchronous
            };
            if (!OperatingSystem.IsWindows()) fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var file = new FileStream(path, fileOptions)) {
                await file.WriteAsync(Encoding.UTF8.GetBytes(json), cancellationToken);
            }
            return await processes.RunAsync("dotnet", [descriptor.EntryPath, path],
                PluginProcessEnvironment.Create(), cancellationToken, Execution);
        } finally {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Redacts supplied credentials, including encoded forms, before exposing plugin errors.</summary>
    public static string? RedactError(string? message, IEnumerable<string> secrets) {
        if (message is null) return null;
        var variants = secrets.Where(value => !string.IsNullOrEmpty(value))
            .SelectMany(value => new[] { value, Uri.EscapeDataString(value), JsonSerializer.Serialize(value)[1..^1] })
            .Distinct(StringComparer.Ordinal).OrderByDescending(value => value.Length);
        foreach (var value in variants) message = message.Replace(value, "[redacted]", StringComparison.Ordinal);
        return message.Length <= 4096 ? message : message[..4096];
    }
}

/// <summary>Canonical platform environment names permitted in trusted plugin child processes.</summary>
internal static class PluginProcessEnvironment {
    private const string PathKey = "PATH";
    private const string HomeKey = "HOME";
    private const string ProfileKey = "USERPROFILE";
    private const string SystemRootKey = "SystemRoot";
    private const string LanguageKey = "LANG";
    private const string LocaleKey = "LC_ALL";
    private const string TemporaryKey = "TMPDIR";
    private const string TempKey = "TEMP";
    private const string TmpKey = "TMP";
    private const string DotnetRootKey = "DOTNET_ROOT";
    private const string CertificateFileKey = "SSL_CERT_FILE";
    private const string CertificateDirectoryKey = "SSL_CERT_DIR";

    internal static IReadOnlyDictionary<string, string> Create() =>
        new[] { PathKey, HomeKey, ProfileKey, SystemRootKey, LanguageKey, LocaleKey,
            TemporaryKey, TempKey, TmpKey, DotnetRootKey, CertificateFileKey, CertificateDirectoryKey }
        .Select(key => new KeyValuePair<string, string?>(key, Environment.GetEnvironmentVariable(key)))
        .Where(pair => pair.Value is not null)
        .ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.Ordinal);
}
