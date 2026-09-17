using Prismedia.Contracts.Plugins;

namespace Prismedia.Application.Integrations;

/// <summary>Limits decrypted connection credentials to the current package declaration at each invocation boundary.</summary>
public static class IntegrationCredentialScope {
    /// <summary>Copies only exactly declared keys and verifies required values. Undeclared saved credentials remain available for recovery but never enter this invocation.</summary>
    public static IReadOnlyDictionary<string, string> ForManifest(PluginManifest manifest, IReadOnlyDictionary<string, string> saved) {
        var keys = manifest.Auth.Select(field => field.Key).ToHashSet(StringComparer.Ordinal);
        var scoped = saved.Where(pair => keys.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (manifest.Auth.Any(field => field.Required && (!scoped.TryGetValue(field.Key, out var value) || string.IsNullOrWhiteSpace(value))))
            throw new IntegrationInvocationException("Required connection credentials are missing.");
        return scoped;
    }
}
