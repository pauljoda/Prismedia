using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Security;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Security;

/// <summary>
/// One-time and per-boot user bootstrap, run by the API process right after migrations:
/// ensures the app security row exists, hashes the staged pre-multi-user API key into
/// migrated accounts' passwords, and applies the host recovery environment variables.
/// </summary>
public static class UserBootstrapRunner {
    /// <summary>New password applied to the recovery admin account on every boot while set.</summary>
    public const string RecoveryPasswordVariable = "PRISMEDIA_RECOVERY_PASSWORD";

    /// <summary>Username targeted by the recovery password; defaults to <c>admin</c>.</summary>
    public const string RecoveryUsernameVariable = "PRISMEDIA_RECOVERY_USERNAME";

    private const string DefaultRecoveryUsername = "admin";

    public static async Task RunUserBootstrapAsync(
        IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken = default) {
        if (!ShouldRun(configuration)) {
            return;
        }

        var logger = services.GetService<ILoggerFactory>()?.CreateLogger("Prismedia.UserBootstrap")
            ?? (ILogger)NullLogger.Instance;
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PrismediaDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        await EnsureAppSecurityAsync(db, cancellationToken);
        await ApplyLegacyApiKeyAsync(db, hasher, logger, cancellationToken);
        await ApplyRecoveryEnvironmentAsync(db, hasher, configuration, logger, cancellationToken);
    }

    private static async Task EnsureAppSecurityAsync(PrismediaDbContext db, CancellationToken cancellationToken) {
        if (await db.AppSecurity.AnyAsync(cancellationToken)) {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        db.AppSecurity.Add(new AppSecurityRow {
            Id = 1,
            ServerId = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Migrated installs: every former Jellyfin profile authenticated with the app API key
    /// as its password, so that key becomes each migrated account's initial password. The
    /// staged key is nulled once consumed, making this a one-time, idempotent step.
    /// </summary>
    private static async Task ApplyLegacyApiKeyAsync(
        PrismediaDbContext db,
        IPasswordHasher hasher,
        ILogger logger,
        CancellationToken cancellationToken) {
        var state = await db.AppSecurity.FirstAsync(cancellationToken);
        if (state.LegacyApiKey is not { Length: > 0 } legacyKey) {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var migrated = 0;
        var hash = hasher.Hash(legacyKey);
        var users = await db.Users.Where(user => user.PasswordHash == null).ToArrayAsync(cancellationToken);
        foreach (var user in users) {
            user.PasswordHash = hash;
            user.PasswordUpdatedAt = now;
            user.UpdatedAt = now;
            migrated++;
        }

        state.LegacyApiKey = null;
        state.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        if (migrated > 0) {
            logger.LogWarning(
                "Migrated {Count} account(s) from the pre-multi-user API key: each keeps the old " +
                "key as its password so existing Jellyfin/OPDS clients continue to work. Reset " +
                "their passwords from Settings → Users.",
                migrated);
        }
    }

    /// <summary>
    /// Host recovery: while <c>PRISMEDIA_RECOVERY_PASSWORD</c> is set, the named account is
    /// reset to an enabled admin with that password on every boot (created when missing) and
    /// its other sessions are invalidated. The variable should be unset after use.
    /// </summary>
    private static async Task ApplyRecoveryEnvironmentAsync(
        PrismediaDbContext db,
        IPasswordHasher hasher,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken) {
        var password = configuration[RecoveryPasswordVariable];
        if (string.IsNullOrWhiteSpace(password)) {
            return;
        }

        var username = configuration[RecoveryUsernameVariable];
        if (string.IsNullOrWhiteSpace(username)) {
            username = DefaultRecoveryUsername;
        }

        username = username.Trim();
        var normalized = EfSecurityPersistence.NormalizeUsername(username);
        var now = DateTimeOffset.UtcNow;
        var user = await db.Users.FirstOrDefaultAsync(row => row.NormalizedUsername == normalized, cancellationToken);
        if (user is null) {
            user = new UserRow {
                Id = Guid.NewGuid(),
                Username = username,
                NormalizedUsername = normalized,
                DisplayName = username,
                Role = UserRole.Admin,
                AllowSfw = true,
                AllowNsfw = true,
                CanCreateLibraries = true,
                Enabled = true,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Users.Add(user);
        } else {
            user.Role = UserRole.Admin;
            user.Enabled = true;
            user.UpdatedAt = now;
        }

        user.PasswordHash = hasher.Hash(password);
        user.PasswordUpdatedAt = now;
        var userId = user.Id;
        var sessions = await db.UserSessions
            .Where(session => session.UserId == userId && session.InvalidatedAt == null)
            .ToArrayAsync(cancellationToken);
        foreach (var session in sessions) {
            session.InvalidatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "RECOVERY: password for admin user '{Username}' was reset from {Variable}. " +
            "This re-applies on every start until the environment variable is unset — unset it now.",
            username,
            RecoveryPasswordVariable);
    }

    private static bool ShouldRun(IConfiguration configuration) {
        if (AppDomain.CurrentDomain
            .GetAssemblies()
            .Any(assembly => assembly.GetName().Name == "Microsoft.AspNetCore.Mvc.Testing")) {
            return false;
        }

        var configured = configuration["Prismedia:ApplyMigrations"];
        return configured is null || bool.TryParse(configured, out var enabled) && enabled;
    }
}
