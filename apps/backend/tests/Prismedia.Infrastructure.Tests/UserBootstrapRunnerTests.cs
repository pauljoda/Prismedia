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
