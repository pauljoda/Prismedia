using System.Net;
using Npgsql;

namespace Prismedia.Infrastructure.Database;

public static class PostgresConnectionString {
    /// <summary>
    /// Default per-process connection pool cap. Npgsql's own default (100) exceeds the bundled
    /// PostgreSQL's max_connections on its own — and the API and worker each run a pool against the
    /// same server, so a job burst (e.g. a season's worth of acquisition searches) exhausted the
    /// server and every request 500'd with "sorry, too many clients already". Two processes at this
    /// cap stay comfortably inside even the original 40-connection server limit. Explicit pool
    /// settings in the configured connection string are respected untouched.
    /// </summary>
    private const int DefaultMaxPoolSize = 16;

    /// <summary>Seconds an idle pooled connection is kept before being closed, so bursts drain back down.</summary>
    private const int DefaultConnectionIdleLifetimeSeconds = 60;

    public static string Normalize(string connectionString) {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        if (!Uri.TryCreate(connectionString, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "postgresql" && uri.Scheme != "postgres")) {
            return WithPoolDefaults(connectionString);
        }

        var credentials = uri.UserInfo.Split(':', 2);
        var username = credentials.Length > 0 ? WebUtility.UrlDecode(credentials[0]) : string.Empty;
        var password = credentials.Length > 1 ? WebUtility.UrlDecode(credentials[1]) : string.Empty;
        var database = uri.AbsolutePath.TrimStart('/');

        return WithPoolDefaults($"Host={uri.Host};Port={uri.Port};Database={database};Username={username};Password={password}");
    }

    /// <summary>Applies the conservative pool defaults unless the configured string sets its own pooling.</summary>
    private static string WithPoolDefaults(string connectionString) {
        if (connectionString.Contains("Pool Size", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("Pooling", StringComparison.OrdinalIgnoreCase)) {
            return connectionString;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString) {
            MaxPoolSize = DefaultMaxPoolSize,
            ConnectionIdleLifetime = DefaultConnectionIdleLifetimeSeconds,
        };
        return builder.ConnectionString;
    }
}
