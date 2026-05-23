using System.Net;

namespace Prismedia.Infrastructure.Database;

public static class PostgresConnectionString {
    public static string Normalize(string connectionString) {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        if (!Uri.TryCreate(connectionString, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "postgresql" && uri.Scheme != "postgres")) {
            return connectionString;
        }

        var credentials = uri.UserInfo.Split(':', 2);
        var username = credentials.Length > 0 ? WebUtility.UrlDecode(credentials[0]) : string.Empty;
        var password = credentials.Length > 1 ? WebUtility.UrlDecode(credentials[1]) : string.Empty;
        var database = uri.AbsolutePath.TrimStart('/');

        return $"Host={uri.Host};Port={uri.Port};Database={database};Username={username};Password={password}";
    }
}
