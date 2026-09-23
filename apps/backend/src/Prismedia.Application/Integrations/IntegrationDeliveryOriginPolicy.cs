using System.Net;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>Limits catalog file retrieval to the connection or explicitly declared anonymous HTTPS origins.</summary>
public static class IntegrationDeliveryOriginPolicy {
    /// <summary>Checks the bounded manifest declaration without granting authority from a source response.</summary>
    public static bool HasValidDeclaration(PluginIntegrationDefinition definition) {
        var origins = definition.AnonymousArtifactOrigins ?? [];
        var suffixes = definition.AnonymousArtifactHostSuffixes ?? [];
        if (origins.Count == 0 && suffixes.Count == 0) return true;
        if (origins.Count + suffixes.Count > 8 || definition.Capabilities?.Any(capability => capability?.Kind == PluginCapability.AcquisitionSource
                && capability.Operations?.Contains(IntegrationOperation.Resolve) == true) != true) return false;
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in origins) {
            if (!TryOrigin(value, out var origin) || !identities.Add(OriginKey(origin))) return false;
        }
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in suffixes) {
            if (!TryHostSuffix(value) || !hosts.Add(value)) return false;
        }
        return true;
    }

    /// <summary>Returns the single origin the HTTP transport must enforce, including on redirects. Foreign origins never receive plugin-supplied headers.</summary>
    public static string RequireAllowedOrigin(PluginIntegrationDefinition? definition, string baseUrl, HttpArtifactDelivery delivery) {
        if (!TryAddress(baseUrl, out var connection) || !TryAddress(delivery.Url, out var target)
            || delivery.Headers is null) throw Rejected();
        if (OriginKey(connection) == OriginKey(target)) return connection.GetLeftPart(UriPartial.Authority);
        if (definition is null || !HasValidDeclaration(definition) || delivery.Headers.Count != 0) throw Rejected();
        var exact = definition.AnonymousArtifactOrigins?.Any(value => TryOrigin(value, out var origin)
            && OriginKey(origin) == OriginKey(target)) == true;
        var catalogSubdomain = connection.Scheme == Uri.UriSchemeHttps && connection.IsDefaultPort
            && target.Scheme == Uri.UriSchemeHttps && target.IsDefaultPort
            && definition.AnonymousArtifactHostSuffixes?.Any(suffix =>
                connection.IdnHost.Equals(suffix, StringComparison.OrdinalIgnoreCase)
                && target.IdnHost.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase)) == true;
        if (!exact && !catalogSubdomain) throw Rejected();
        return target.GetLeftPart(UriPartial.Authority);
    }

    private static bool TryHostSuffix(string? value) {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 253 || value != value.Trim()
            || value.Contains(':') || value.Contains('/') || value.Contains('\\')
            || IPAddress.TryParse(value, out _)) return false;
        var labels = value.Split('.');
        return labels.Length >= 2 && labels.All(label => label.Length is > 0 and <= 63
            && char.IsAsciiLetterOrDigit(label[0]) && char.IsAsciiLetterOrDigit(label[^1])
            && label.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'));
    }

    private static bool TryOrigin(string? value, out Uri origin) {
        if (!TryAddress(value, out origin) || value!.Length > 2048 || !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || origin.Scheme != Uri.UriSchemeHttps || value.Contains('?')) return false;
        var path = value!.IndexOf('/', value.IndexOf("://", StringComparison.Ordinal) + 3);
        return path < 0 || value[path..] == "/";
    }

    private static bool TryAddress(string? value, out Uri address) {
        address = null!;
        return !string.IsNullOrWhiteSpace(value) && value.Length <= 16384
            && !value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) || character is '\\' or '#')
            && Uri.TryCreate(value, UriKind.Absolute, out address!)
            && (address.Scheme == Uri.UriSchemeHttp || address.Scheme == Uri.UriSchemeHttps)
            && address.UserInfo.Length == 0 && address.Host.Length > 0;
    }

    private static string OriginKey(Uri uri) => $"{uri.Scheme}|{uri.IdnHost.ToLowerInvariant()}|{uri.Port}";
    private static IntegrationInvocationException Rejected() => new("The source returned a download outside its declared retrieval scope.");
}
