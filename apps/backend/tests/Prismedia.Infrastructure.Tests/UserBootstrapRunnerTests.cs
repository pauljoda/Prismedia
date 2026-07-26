using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Prismedia.Application.Security;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Security;

namespace Prismedia.Infrastructure.Tests;

public sealed class UserBootstrapRunnerTests {
    private static readonly IdentityPasswordHasher Hasher = new();

    [Fact]
    public async Task FreshInstallCreatesAppSecurityRow() {
        await using var provider = BuildProvider(out var db);

        await UserBootstrapRunner.RunUserBootstrapAsync(provider, Config());

        var state = await db.AppSecurity.SingleAsync();
        Assert.NotEqual(Guid.Empty, state.ServerId);
        Assert.Null(state.LegacyApiKey);
        Assert.Empty(await db.Users.ToArrayAsync());
    }

    [Fact]
    public async Task LegacyApiKeyBecomesMigratedUsersPasswordExactlyOnce() {
        await using var provider = BuildProvider(out var db);
        var now = DateTimeOffset.UtcNow;
        db.AppSecurity.Add(new AppSecurityRow {
            Id = 1, ServerId = Guid.NewGuid(), LegacyApiKey = "fox-lima-alpha", CreatedAt = now, UpdatedAt = now
        });
        db.Users.Add(NewUser("migrated", passwordHash: null));
        db.Users.Add(NewUser("already-set", passwordHash: Hasher.Hash("existing-password")));
        await db.SaveChangesAsync();

        await UserBootstrapRunner.RunUserBootstrapAsync(provider, Config());

        db.ChangeTracker.Clear();
        var migrated = await db.Users.SingleAsync(user => user.Username == "migrated");
        Assert.NotNull(migrated.PasswordHash);
        Assert.Equal(PasswordVerification.Success, Hasher.Verify(migrated.PasswordHash!, "fox-lima-alpha"));
        var untouched = await db.Users.SingleAsync(user => user.Username == "already-set");
        Assert.Equal(PasswordVerification.Success, Hasher.Verify(untouched.PasswordHash!, "existing-password"));
        Assert.Null((await db.AppSecurity.SingleAsync()).LegacyApiKey);

        // Re-running is a no-op (the staged key was consumed).
        var hashBefore = migrated.PasswordHash;
        await UserBootstrapRunner.RunUserBootstrapAsync(provider, Config());
        db.ChangeTracker.Clear();
        Assert.Equal(hashBefore, (await db.Users.SingleAsync(user => user.Username == "migrated")).PasswordHash);
    }

    [Fact]
    public async Task RecoveryPasswordResetsExistingAccountToEnabledAdminAndInvalidatesSessions() {
        await using var provider = BuildProvider(out var db);
        var user = NewUser("recovery-user", passwordHash: Hasher.Hash("forgotten"));
        user.Role = UserRole.Member;
        user.Enabled = false;
        db.Users.Add(user);
        db.UserSessions.Add(new UserSessionRow {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = new string('a', 64),
            CreatedAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        await UserBootstrapRunner.RunUserBootstrapAsync(provider, Config(
            (UserBootstrapRunner.RecoveryPasswordVariable, "rescued-password"),
            (UserBootstrapRunner.RecoveryUsernameVariable, "recovery-user")));

        db.ChangeTracker.Clear();
        var rescued = await db.Users.SingleAsync();
        Assert.Equal(UserRole.Admin, rescued.Role);
        Assert.True(rescued.Enabled);
        Assert.Equal(PasswordVerification.Success, Hasher.Verify(rescued.PasswordHash!, "rescued-password"));
        Assert.NotNull((await db.UserSessions.SingleAsync()).InvalidatedAt);
    }

    [Fact]
    public async Task RecoveryPasswordCreatesAdminWhenUsernameIsMissing() {
        await using var provider = BuildProvider(out var db);

        await UserBootstrapRunner.RunUserBootstrapAsync(provider, Config(
            (UserBootstrapRunner.RecoveryPasswordVariable, "rescued-password")));

        var admin = await db.Users.SingleAsync();
        Assert.Equal("admin", admin.Username);
        Assert.Equal(UserRole.Admin, admin.Role);
        Assert.True(admin.Enabled);
        Assert.Equal(PasswordVerification.Success, Hasher.Verify(admin.PasswordHash!, "rescued-password"));
    }

    private static UserRow NewUser(string username, string? passwordHash) {
        var now = DateTimeOffset.UtcNow;
        return new UserRow {
            Id = Guid.NewGuid(),
            Username = username,
            NormalizedUsername = username.ToLowerInvariant(),
            DisplayName = username,
            PasswordHash = passwordHash,
            Role = UserRole.Member,
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static ServiceProvider BuildProvider(out PrismediaDbContext db) {
        var services = new ServiceCollection();
        var databaseName = $"bootstrap-{Guid.NewGuid():N}";
        services.AddDbContext<PrismediaDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddSingleton<IPasswordHasher>(Hasher);
        var provider = services.BuildServiceProvider();
        db = provider.GetRequiredService<PrismediaDbContext>();
        return provider;
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(pair => pair.Key, pair => (string?)pair.Value))
            .Build();
}
