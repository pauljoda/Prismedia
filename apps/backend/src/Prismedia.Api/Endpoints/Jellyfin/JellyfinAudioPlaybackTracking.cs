using System.Security.Cryptography;
using System.Text;
using Prismedia.Api.Security;

namespace Prismedia.Api.Endpoints;

internal static class JellyfinAudioPlaybackTracking {
    internal static string ClientKey(HttpContext httpContext) {
        var auth = httpContext.GetPrismediaAuth();
        var client = httpContext.Request.GetJellyfinClientIdentity();
        var deviceId = Normalized(client.DeviceId) ??
            Normalized(auth?.Session?.DeviceId);

        if (auth is { Session: { } session, User: { } user }) {
            return deviceId is not null
                ? $"user:{user.Id:N}:device:{Hash(deviceId)}"
                : $"user:{user.Id:N}:session:{session.Id:N}";
        }

        if (auth is not null) {
            return deviceId is not null
                ? $"user:{auth.User.Id:N}:device:{Hash(deviceId)}"
                : $"user:{auth.User.Id:N}";
        }

        var remote = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = httpContext.Request.Headers.UserAgent.FirstOrDefault() ?? "unknown";
        return $"anonymous:{Hash(remote)}:{Hash(userAgent)}";
    }

    internal static bool IsRangeRequest(HttpContext httpContext) =>
        httpContext.Request.Headers.Range.Count > 0;

    private static string? Normalized(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
