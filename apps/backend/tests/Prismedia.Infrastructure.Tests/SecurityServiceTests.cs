using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Security;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Security;

namespace Prismedia.Infrastructure.Tests;

public sealed class SecurityServiceTests {
    [Fact]
    public void HumanApiKeyWordListIsLargeShortAndAscii() {
        Assert.True(HumanApiKeyPassphraseGenerator.Words.Count >= 2048);
        Assert.All(HumanApiKeyPassphraseGenerator.Words, word => {
            Assert.InRange(word.Length, 3, 5);
            Assert.Matches("^[a-z]+$", word);
        });
    }

    [Fact]
    public void HumanApiKeyGenerationReturnsThreeShortWords() {
        var key = HumanApiKeyPassphraseGenerator.Generate();

        Assert.Matches("^[a-z]{3,5}-[a-z]{3,5}-[a-z]{3,5}$", key);
    }

    [Fact]
    public void ApiKeyNormalizationAcceptsWhitespaceAndCase() {
        var normalized = PrismediaSecurityService.NormalizeApiKey("  Fox Lima_ALPHA  ");

        Assert.Equal("fox-lima-alpha", normalized);
    }

    [Fact]
    public async Task CreateSessionTruncatesClientIdentityToDatabaseLimits() {
        await using var db = CreateContext();
        var persistence = new EfSecurityPersistence(db);
        var profile = await persistence.CreateProfileAsync(
            "reader",
            "Reader",
            allowSfw: true,
            allowNsfw: false,
            enabled: true,
            CancellationToken.None);
        var longClient = new string('c', 200);
        var longDeviceName = new string('d', 200);
        var longDeviceId = new string('i', 300);
        var longVersion = new string('v', 100);

        var session = await persistence.CreateSessionAsync(
            profile.Id,
            new string('a', 64),
            new JellyfinClientIdentity(longClient, longDeviceName, longDeviceId, longVersion),
            CancellationToken.None);

        Assert.Equal(128, session.Client?.Length);
        Assert.Equal(128, session.DeviceName?.Length);
        Assert.Equal(256, session.DeviceId?.Length);
        Assert.Equal(64, session.ApplicationVersion?.Length);
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"security-{Guid.NewGuid():N}")
            .Options);
}
