using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Prismedia.Application.Settings;
using Prismedia.Application.Subtitles;
using Prismedia.Infrastructure.Media.Adapters;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Processes;
using Prismedia.Infrastructure.Security;
using Prismedia.Infrastructure.Settings;
using Prismedia.Infrastructure.Subtitles;

namespace Prismedia.Infrastructure.Tests;

public sealed class SubtitleCredentialStorageTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"subtitle-credentials-{Guid.NewGuid():N}");

    [Fact]
    public async Task ConfigurationEncryptsSecretsAndBlankEditsPreserveTheirSavedValues() {
        await using var db = Context();
        var service = Service(db);
        var saved = await service.SaveOpenSubtitlesConfigurationAsync(new(true, "fixture-key", "fixture-user", "fixture-password", true, false), default);
        Assert.True(saved.ApiKeyConfigured && saved.UsernameConfigured && saved.PasswordConfigured);
        Assert.True(saved.IncludeAiTranslated);
        var rows = await db.ProviderCredentials.AsNoTracking().ToArrayAsync();
        Assert.Equal(3, rows.Length);
        Assert.All(rows, row => { Assert.Equal(ProviderCredentialProtector.CurrentVersion, row.ProtectionVersion); Assert.DoesNotContain("fixture-", row.EncryptedValue); });
        var edited = await service.SaveOpenSubtitlesConfigurationAsync(new(false, "", null, "  ", false, true), default);
        Assert.False(edited.Enabled);
        Assert.True(edited.ApiKeyConfigured && edited.UsernameConfigured && edited.PasswordConfigured);
        Assert.True(edited.IncludeMachineTranslated);
        var stored = await new ProviderCredentialStore(db, new(_root)).ReadAsync(rows[0].ProviderConfigId,
            [OpenSubtitlesCredentialKeys.ApiKey, OpenSubtitlesCredentialKeys.Username, OpenSubtitlesCredentialKeys.Password], default);
        Assert.Equal("fixture-key", stored[OpenSubtitlesCredentialKeys.ApiKey]);
        Assert.Equal("fixture-user", stored[OpenSubtitlesCredentialKeys.Username]);
        Assert.Equal("fixture-password", stored[OpenSubtitlesCredentialKeys.Password]);
        Assert.Equal(edited, await Service(db).GetOpenSubtitlesConfigurationAsync(default));
    }

    [Fact]
    public async Task EnvironmentOverridesCanReplaceUnreadableStoredSubtitleCredentials() {
        await using var db = Context();
        await Service(db).SaveOpenSubtitlesConfigurationAsync(new(true, "fixture-key", "fixture-user", "fixture-password", false, false), default);
        foreach (var row in await db.ProviderCredentials.ToArrayAsync()) row.EncryptedValue = "unreadable";
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).GetOpenSubtitlesConfigurationAsync(default));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            [OpenSubtitlesProtocol.ApiKeyEnvironment] = "environment-key",
            [OpenSubtitlesProtocol.UsernameEnvironment] = "environment-user",
            [OpenSubtitlesProtocol.PasswordEnvironment] = "environment-password"
        }).Build();
        var saved = await Service(db, configuration).GetOpenSubtitlesConfigurationAsync(default);
        Assert.True(saved.Enabled && saved.ApiKeyConfigured && saved.UsernameConfigured && saved.PasswordConfigured);
    }

    private SubtitleAcquisitionService Service(PrismediaDbContext db, IConfiguration? configuration = null) => new(
        db, new ProviderCredentialStore(db, new(_root)), new OpenSubtitlesClient(new HttpClient()),
        new SubtitleAssetImportService(new ProcessExecutor(), new AssetPathService(_root), new()),
        new MediaHashingAdapter(new HashingService()), new SettingsService(new EfSettingsPersistence(db)),
        configuration ?? new ConfigurationBuilder().Build());
    private static PrismediaDbContext Context() => new(new DbContextOptionsBuilder<PrismediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
