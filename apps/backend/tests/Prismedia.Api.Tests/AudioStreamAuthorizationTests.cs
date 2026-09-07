using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prismedia.Application.Audio;
using Prismedia.Application.Entities;
using Prismedia.Application.Security;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Security;

namespace Prismedia.Api.Tests;

/// <summary>
/// Exercises HTTP authentication, real library visibility, source resolution, and stream planning
/// together using synthetic files and an isolated database. Only the account/session store is fake.
/// </summary>
public sealed class AudioStreamAuthorizationTests : IDisposable {
    private const string AudioBytes = "synthetic-audio-bytes";
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"prismedia-audio-auth-{Guid.NewGuid():N}");
    private readonly Guid _trackId = Guid.NewGuid();
    private readonly Guid _rootId = Guid.NewGuid();
    private readonly RecordingTranscodeOptions _transcodeOptions = new();

    public AudioStreamAuthorizationTests() => Directory.CreateDirectory(_tempDir);

    public static IEnumerable<object[]> HiddenAudioRequests() {
        foreach (var (method, range) in new[] { ("GET", false), ("HEAD", false), ("GET", true) }) {
            foreach (var codec in new[] { MediaCodecs.Mp3, MediaCodecs.Ac3 }) {
                foreach (var (enabled, granted, nsfw) in new[] {
                    (true, false, false), (false, true, false), (true, true, true)
                }) {
                    yield return [method, range, codec, enabled, granted, nsfw];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(HiddenAudioRequests))]
    public async Task HiddenAudioIsIndistinguishableFromMissingAudio(
        string method, bool range, string codec, bool enabled, bool granted, bool nsfw) {
        using var factory = CreateFactory();
        await SeedAsync(factory, codec, enabled, granted, nsfw);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.SendAsync(Request(method, range, _trackId));
        var missingId = Guid.NewGuid();
        using var missing = await client.SendAsync(Request(method, range, missingId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(missing.StatusCode, response.StatusCode);
        // Error messages echo the requested ID; no other difference may disclose source existence.
        Assert.Equal(
            (await missing.Content.ReadAsStringAsync()).Replace(missingId.ToString(), "{id}"),
            (await response.Content.ReadAsStringAsync()).Replace(_trackId.ToString(), "{id}"));
        Assert.Empty(response.Headers.AcceptRanges);
        Assert.Null(response.Content.Headers.ContentRange);
        Assert.Equal(0, _transcodeOptions.ReadCount);
    }

    [Theory]
    [InlineData("GET", false)]
    [InlineData("HEAD", false)]
    [InlineData("GET", true)]
    public async Task AnonymousAudioRequestsRequireAuthentication(string method, bool range) {
        using var factory = CreateFactory();
        await SeedAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(Request(method, range, _trackId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, _transcodeOptions.ReadCount);
    }

    [Theory]
    [InlineData("GET", false)]
    [InlineData("HEAD", false)]
    [InlineData("GET", true)]
    public async Task GrantedAudioStillSupportsPlaybackAndRanges(string method, bool range) {
        using var factory = CreateFactory();
        await SeedAsync(factory);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.SendAsync(Request(method, range, _trackId));

        Assert.Equal(range ? HttpStatusCode.PartialContent : HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaContentTypes.AudioMpeg, response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("bytes", response.Headers.AcceptRanges);
        Assert.Equal(method == "HEAD" ? "" : range ? AudioBytes[..4] : AudioBytes,
            await response.Content.ReadAsStringAsync());
        if (range) Assert.Equal($"bytes 0-3/{AudioBytes.Length}", response.Content.Headers.ContentRange?.ToString());
    }

    [Fact]
    public async Task RevokedLibraryAccessStopsTheNextRangeRequest() {
        using var factory = CreateFactory();
        var userId = await SeedAsync(factory);
        using var client = factory.CreateAuthenticatedClient();
        using var before = await client.SendAsync(Request("GET", true, _trackId));
        Assert.Equal(HttpStatusCode.PartialContent, before.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope()) {
            await scope.ServiceProvider.GetRequiredService<ILibraryAccessStore>()
                .ReplaceUserAccessAsync(userId, [], CancellationToken.None);
        }

        using var after = await client.SendAsync(Request("GET", true, _trackId));
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
        Assert.Equal(1, _transcodeOptions.ReadCount);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private WebApplicationFactory<Program> CreateFactory() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"audio-stream-auth-{Guid.NewGuid():N}").Options;
        return new WebApplicationFactory<Program>()
            .WithTestAuth()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services => {
                services.RemoveAll<PrismediaDbContext>();
                services.AddScoped(_ => new PrismediaDbContext(options));
                // Restore production authorization: WithTestAuth normally makes every Entity visible.
                services.RemoveAll<IEntityVisibilityChecker>();
                services.AddScoped<IEntityVisibilityChecker, EfEntityVisibilityChecker>();
                services.RemoveAll<ILibraryAccessReader>();
                services.RemoveAll<ILibraryAccessStore>();
                services.AddScoped<ILibraryAccessReader, EfLibraryAccessReader>();
                services.AddScoped<ILibraryAccessStore, EfLibraryAccessReader>();
                services.RemoveAll<IAudioTranscodeOptions>();
                services.AddSingleton<IAudioTranscodeOptions>(_transcodeOptions);
            }));
    }

    private async Task<Guid> SeedAsync(WebApplicationFactory<Program> factory,
        string codec = MediaCodecs.Mp3, bool enabled = true, bool granted = true, bool nsfw = false) {
        var path = Path.Combine(_tempDir, "source.mp3");
        await File.WriteAllTextAsync(path, AudioBytes);
        await using var scope = factory.Services.CreateAsyncScope();
        var security = scope.ServiceProvider.GetRequiredService<ISecurityPersistence>();
        var account = (await security.ListUsersAsync(false, CancellationToken.None)).Single();
        await security.UpdateUserAsync(account.Id, null, null, UserRole.Member, false,
            false, false, true, CancellationToken.None);
        var db = scope.ServiceProvider.GetRequiredService<PrismediaDbContext>();
        db.Users.Add(new UserRow { Id = account.Id, Role = UserRole.Member, AllowNsfw = false });
        db.Entities.Add(new EntityRow {
            Id = _trackId, KindCode = EntityKind.AudioTrack.ToCode(), Title = "Test audio"
        });
        db.AudioTrackDetails.Add(new AudioTrackDetailRow { EntityId = _trackId });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(), EntityId = _trackId, Role = EntityFileRole.Source, Path = path
        });
        db.EntityTechnical.Add(new EntityTechnicalRow { EntityId = _trackId, Codec = codec });
        db.LibraryRoots.Add(new LibraryRootRow {
            Id = _rootId, Path = _tempDir, Label = "Test library", Enabled = enabled, IsNsfw = nsfw
        });
        db.EntityLibraryRoots.Add(new EntityLibraryRootRow { EntityId = _trackId, LibraryRootId = _rootId });
        await db.SaveChangesAsync();
        if (granted) {
            await scope.ServiceProvider.GetRequiredService<ILibraryAccessStore>()
                .ReplaceUserAccessAsync(account.Id, [_rootId], CancellationToken.None);
        }
        return account.Id;
    }

    private static HttpRequestMessage Request(string method, bool range, Guid id) {
        var request = new HttpRequestMessage(new HttpMethod(method), $"/api/audio-stream/{id}");
        if (range) request.Headers.Range = new RangeHeaderValue(0, 3);
        return request;
    }

    private sealed class RecordingTranscodeOptions : IAudioTranscodeOptions {
        private readonly string _unusedPath = Path.Combine(Path.GetTempPath(), $"unused-ffmpeg-{Guid.NewGuid():N}");
        public int ReadCount { get; private set; }
        public string FfmpegPath {
            get {
                ReadCount++;
                return _unusedPath;
            }
        }
    }
}
