using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>Limits catalog file retrieval to the connection or explicitly declared anonymous HTTPS origins.</summary>
public static class IntegrationDeliveryOriginPolicy {
    /// <summary>Checks the bounded manifest declaration without granting authority from a source response.</summary>
    public static bool HasValidDeclaration(PluginIntegrationDefinition definition) {
        if (definition.AnonymousArtifactOrigins is not { Count: > 0 } origins) return true;
        if (origins.Count > 8 || definition.Capabilities?.Any(capability => capability?.Kind == PluginCapability.AcquisitionSource
                && capability.Operations?.Contains(IntegrationOperation.Resolve) == true) != true) return false;
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in origins) {
            if (!TryOrigin(value, out var origin) || !identities.Add(OriginKey(origin))) return false;
        }
        return true;
    }

    /// <summary>Returns the single origin the HTTP transport must enforce, including on redirects. Foreign origins never receive plugin-supplied headers.</summary>
    public static string RequireAllowedOrigin(PluginIntegrationDefinition? definition, string baseUrl, HttpArtifactDelivery delivery) {
        if (!TryAddress(baseUrl, out var connection) || !TryAddress(delivery.Url, out var target)
            || delivery.Headers is null) throw Rejected();
        if (OriginKey(connection) == OriginKey(target)) return connection.GetLeftPart(UriPartial.Authority);
        if (definition is null || !HasValidDeclaration(definition) || delivery.Headers.Count != 0
            || definition.AnonymousArtifactOrigins?.Any(value => TryOrigin(value, out var origin)
                && OriginKey(origin) == OriginKey(target)) != true) throw Rejected();
        return target.GetLeftPart(UriPartial.Authority);
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
