using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Prismedia.Application.Audio;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Media;

namespace Prismedia.Api.Tests;

public sealed class AudioStreamEndpointTests : IDisposable {
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"prismedia-audio-stream-{Guid.NewGuid():N}");

    public AudioStreamEndpointTests() {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task OggOpusStreamAdvertisesItsContainerAndCodec() {
        var filePath = Path.Combine(_tempDir, "source.opus");
        await File.WriteAllTextAsync(filePath, "ogg-opus-bytes");
        using var factory = CreateFactory(new FakeAudioStreamService(
            new AudioStreamPlan(
                filePath,
                MediaContentTypes.AudioOggOpus,
                DirectPlayable: true,
                Codec: MediaCodecs.Opus,
                FfmpegPath: "ffmpeg")));
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Head,
            $"/api/audio-stream/{FakeAudioStreamService.TrackId}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaContentTypes.AudioOgg, response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(
            response.Content.Headers.ContentType?.Parameters ?? [],
            parameter => parameter.Name == "codecs" && parameter.Value == MediaCodecs.Opus);
        Assert.Equal("bytes", response.Headers.AcceptRanges.Single());
    }

    public void Dispose() {
        if (Directory.Exists(_tempDir)) {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(IAudioStreamService streams) {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddSingleton(streams);
                    services.AddSingleton<IEntityReadService, TestAuth.VisibleEntityReadService>();
                });
            })
            .WithTestAuth();
    }

    private sealed class FakeAudioStreamService(AudioStreamPlan? stream) : IAudioStreamService {
        public static readonly Guid TrackId = Guid.Parse("55555555-5555-5555-5555-555555555555");

        public Task<AudioStreamPlan?> GetStreamAsync(Guid entityId, CancellationToken cancellationToken) {
            return Task.FromResult(entityId == TrackId ? stream : null);
        }
    }
}
